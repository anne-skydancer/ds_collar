using System.Text.RegularExpressions;
using NUnit.Framework;

namespace DSCollarTests;

/// <summary>
/// Cross-script ABI consistency checks.
///
/// LSL has no #include or preprocessor, so every script re-declares the
/// shared ABI constants: bus numbers, external channels, root/SOS contexts,
/// and LSD settings keys. A rename that lands in some files but not others
/// fails silently in-world (messages stop routing, LSD keys never match).
/// These tests parse every dev-stage script and turn that drift into a red
/// test instead.
///
/// Scope: column-0 UPPER_SNAKE declarations (the agents-style convention for
/// constants). Indented lowercase/PascalCase symbols are per-script state
/// and are ignored by the parser.
///
/// Two classes of re-used constant names are recognized:
/// - FROZEN value sets (Allowlist): the name carries a genuine cross-script
///   contract (e.g. KEY_LOCKED must match kmod_settings' known keys, RLV_CHAN
///   must not collide between plugins). The set freezes the known-good
///   values, so a new/typo'd value still fails.
/// - PER-SCRIPT free-form names (PerScriptNames): the name is re-used in each
///   plugin with a plugin-specific value (labels, categories, ACL masks).
///   Any value is allowed there; the value's contract is with the declaring
///   script alone. Names whose values must additionally be UNIQUE per script
///   (channels, consumer ids, plugin contexts) are covered by dedicated
///   distinctness tests below.
/// </summary>
[TestFixture]
public class ConsistencyTests
{
    // ---------- source access (mirrors TestHelpers.LoadScript's path) ----------

    static string SourceDir() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..",
        "src", "lsl", "collar", "v1.2", "dev"));

    // ---------- parsing ----------

    sealed record Decl(string File, string Name, string Value);

    static readonly Regex IntDecl = new(
        @"^integer\s+([A-Z][A-Z0-9_]*)\s*=\s*(-?\d+)\s*;",
        RegexOptions.Compiled);

    static readonly Regex StrDecl = new(
        @"^string\s+([A-Z][A-Z0-9_]*)\s*=\s*""([^""]*)""\s*;",
        RegexOptions.Compiled);

    static List<Decl> ParseFile(string path)
    {
        var decls = new List<Decl>();
        var file = Path.GetFileName(path);
        foreach (var line in File.ReadAllLines(path))
        {
            var m = IntDecl.Match(line);
            if (m.Success)
            {
                decls.Add(new Decl(file, m.Groups[1].Value, m.Groups[2].Value));
                continue;
            }
            m = StrDecl.Match(line);
            if (m.Success)
                decls.Add(new Decl(file, m.Groups[1].Value, m.Groups[2].Value));
        }
        return decls;
    }

    static List<Decl> AllDeclarations() =>
        Directory.GetFiles(SourceDir(), "*.lsl")
            .OrderBy(p => p, StringComparer.Ordinal)
            .SelectMany(ParseFile)
            .ToList();

    // ---------- constant classification ----------

    /// <summary>
    /// Frozen cross-script contracts: every value the name may legally
    /// carry is listed. Anything new fails and must be added consciously.
    /// </summary>
    static readonly Dictionary<string, HashSet<string>> Allowlist = new()
    {
        // Plugin-local LSD settings keys (must match kmod_settings' key lists)
        ["KEY_LOCKED"] = new HashSet<string>
        { "lock.locked", "folders.locked", "outfits.locked" },
        // Private per-plugin RLV response channels (frozen + distinctness-tested)
        ["RLV_CHAN"] = new HashSet<string>
        { "1888753", "1888771", "1888772" },
    };

    /// <summary>
    /// Names re-declared per script with script-specific values by design.
    /// Rule A ignores them; uniqueness (where it matters) is enforced by
    /// the dedicated tests.
    /// </summary>
    static readonly HashSet<string> PerScriptNames = new()
    {
        "PLUGIN_CONTEXT",       // uniqueness + format: PluginContexts test
        "PLUGIN_LABEL",        // free-form menu label
        "PLUGIN_LABEL_ON",     // free-form toggle label
        "PLUGIN_LABEL_OFF",    // free-form toggle label
        "PLUGIN_CATEGORY",     // free-form menu category
        "PLUGIN_ACL_MASK",     // per-plugin visibility mask
        "RLV_CONSUMER",        // distinctness-tested
        "MAX_PENDING_QUERIES", // per-module internal queue cap
    };

    // ---------- registry: exact values every declaration must carry ----------

    static readonly (string Name, string Value)[] Registry =
    {
        // Consolidated buses
        ("KERNEL_LIFECYCLE", "500"),
        ("REMOTE_BUS", "600"),
        ("AUTH_BUS", "700"),
        ("SETTINGS_BUS", "800"),
        ("UI_BUS", "900"),
        ("DIALOG_BUS", "950"),
        // External protocol channels
        ("LEASH_CHAN", "-8888"),
        ("LEASH_CHAN_LM", "-8888"),
        ("LEASH_HOLDER_CHAN", "-8888"),
        ("RELAY_CHAN", "-1812221819"),
        ("RELAY_CHANNEL", "-1812221819"),
        // Shared contexts (kmod_ui / control_hud / kmod_remote / kmod_menu contract)
        ("ROOT_CONTEXT", "ui.core.root"),
        ("SOS_CONTEXT", "ui.sos.root"),
    };

    // ---------- the check ----------

    /// <summary>
    /// A constant name re-declared across scripts must carry the same value
    /// everywhere, unless it is a frozen or per-script name.
    /// </summary>
    static List<string> FindInconsistentNames(IEnumerable<Decl> decls)
    {
        var errors = new List<string>();
        foreach (var g in decls.GroupBy(d => d.Name).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var values = g.Select(d => d.Value).Distinct().ToList();
            if (values.Count <= 1)
                continue;
            if (PerScriptNames.Contains(g.Key))
                continue;

            if (Allowlist.TryGetValue(g.Key, out var allowed))
            {
                var unknown = values.Where(v => !allowed.Contains(v)).ToList();
                if (unknown.Count > 0)
                    errors.Add($"{g.Key} (allowlisted cross-script contract) has an UNRECOGNIZED value " +
                        $"[{string.Join(", ", unknown)}]; allowed: [{string.Join(", ", allowed)}]. " +
                        string.Join("; ", g.Select(d => $"{d.File}={d.Value}")));
                continue;
            }

            errors.Add($"{g.Key} declared with conflicting values: " +
                string.Join("; ", g.Select(d => $"{d.File}={d.Value}")));
        }
        return errors;
    }

    // ---------- tests ----------

    [Test]
    public void SharedConstantNames_HaveConsistentValues()
    {
        var errors = FindInconsistentNames(AllDeclarations());
        Assert.That(errors, Is.Empty,
            "Constants re-declared across scripts with conflicting values.\n" +
            "LSL has no headers, so every re-declaration must stay in sync by hand.\n" +
            "Fix the values, or classify the name in ConsistencyTests (Allowlist for a\n" +
            "frozen cross-script contract, PerScriptNames for a per-plugin value).\n" +
            string.Join("\n", errors));
    }

    [Test]
    public void RegistryConstants_MatchExactValues()
    {
        var decls = AllDeclarations();
        var errors = new List<string>();

        foreach (var (name, value) in Registry)
        {
            var hits = decls.Where(d => d.Name == name).ToList();
            if (hits.Count == 0)
            {
                errors.Add($"{name} (registry expects {value}) is declared NOWHERE — " +
                    "the constant was renamed; update the registry");
                continue;
            }
            foreach (var d in hits.Where(d => d.Value != value))
                errors.Add($"{d.File}: {name}={d.Value}, registry expects {value}");
        }

        // The harness's own channel constants must match the scripts they simulate.
        Assert.That(TestHelpers.KERNEL_LIFECYCLE, Is.EqualTo(500), "TestHelpers.KERNEL_LIFECYCLE");
        Assert.That(TestHelpers.AUTH_BUS, Is.EqualTo(700), "TestHelpers.AUTH_BUS");
        Assert.That(TestHelpers.SETTINGS_BUS, Is.EqualTo(800), "TestHelpers.SETTINGS_BUS");
        Assert.That(TestHelpers.UI_BUS, Is.EqualTo(900), "TestHelpers.UI_BUS");
        Assert.That(TestHelpers.DIALOG_BUS, Is.EqualTo(950), "TestHelpers.DIALOG_BUS");

        Assert.That(errors, Is.Empty,
            "Registry (ABI) constants drifted from their contract values:\n" +
            string.Join("\n", errors));
    }

    [Test]
    public void PluginContexts_AreUniqueAndNamespaced()
    {
        var decls = AllDeclarations();

        // Among plugins, every context must be unique.
        var plugins = decls.Where(d => d.Name == "PLUGIN_CONTEXT" && d.File.StartsWith("plugin_"));
        var dupes = plugins.GroupBy(d => d.Value)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(d => d.File))}")
            .ToList();
        Assert.That(dupes, Is.Empty, "PLUGIN_CONTEXT must be unique per plugin:\n" + string.Join("\n", dupes));

        // kmod_leash_engine deliberately mirrors plugin_leash's context: it is
        // the headless backend and attributes its state notifications to the
        // leash feature context. No other kmod may declare a plugin context.
        var kmods = decls.Where(d => d.Name == "PLUGIN_CONTEXT" && !d.File.StartsWith("plugin_"));
        foreach (var k in kmods)
        {
            var mirrored = plugins.Any(p => p.Value == k.Value);
            var isKnownMirror = k.File == "kmod_leash_engine.lsl" && k.Value == "ui.core.leash";
            Assert.That(mirrored && isKnownMirror,
                Is.True,
                $"{k.File}: PLUGIN_CONTEXT={k.Value}. Only kmod_leash_engine may mirror a " +
                "plugin's context (backend identification); any other kmod context is drift.");
        }

        var malformed = decls.Where(d => d.Name == "PLUGIN_CONTEXT")
            .Where(d => !Regex.IsMatch(d.Value, @"^ui\.(core|sos)\.[a-z0-9_]+$"))
            .Select(d => $"{d.File}: {d.Value}")
            .ToList();
        Assert.That(malformed, Is.Empty,
            "PLUGIN_CONTEXT values must follow ui.(core|sos).<name>:\n" + string.Join("\n", malformed));
    }

    [Test]
    public void PerScriptIdentifiers_AreDistinctAcrossScripts()
    {
        var decls = AllDeclarations();

        // Two plugins sharing a private RLV response channel would cross-talk.
        var chanDupes = decls.Where(d => d.Name == "RLV_CHAN")
            .GroupBy(d => d.Value)
            .Where(g => g.Count() > 1)
            .Select(g => $"RLV_CHAN={g.Key} in {string.Join(", ", g.Select(d => d.File))}")
            .ToList();
        Assert.That(chanDupes, Is.Empty,
            "Private RLV response channels must be distinct per plugin:\n" + string.Join("\n", chanDupes));

        // kmod_rlv refcounts claims per consumer id; a shared id would
        // let one plugin's release drop another's claim.
        var consumerDupes = decls.Where(d => d.Name == "RLV_CONSUMER")
            .GroupBy(d => d.Value)
            .Where(g => g.Count() > 1)
            .Select(g => $"RLV_CONSUMER='{g.Key}' in {string.Join(", ", g.Select(d => d.File))}")
            .ToList();
        Assert.That(consumerDupes, Is.Empty,
            "RLV_CONSUMER ids must be distinct per plugin (claim refcount domain):\n" +
            string.Join("\n", consumerDupes));
    }

    [Test]
    public void MenuSlotContract_Holds()
    {
        // kmod_menu: "MENU_SLOTS minus the reserved low slots ... which
        // kmod_ui's MAX_FUNC_BTNS must match (cross-module contract)."
        var decls = AllDeclarations();
        var menuSlots = decls.First(d => d.File == "kmod_menu.lsl" && d.Name == "MENU_SLOTS").Value;
        var maxFuncBtns = decls.First(d => d.File == "kmod_ui.lsl" && d.Name == "MAX_FUNC_BTNS").Value;
        Assert.That(int.Parse(menuSlots) - 3, Is.EqualTo(int.Parse(maxFuncBtns)),
            $"kmod_menu MENU_SLOTS={menuSlots} minus 3 nav slots must equal kmod_ui MAX_FUNC_BTNS={maxFuncBtns}");
    }

    [Test]
    public void Detector_FlagsPlantedDrift()
    {
        // Regression test for the checker itself: a synthetic AUTH_BUS drift
        // (one script at 701) must be reported, and the frozen allowlist
        // must still catch an unrecognized value for an allowlisted name.
        var planted = new List<Decl>
        {
            new("a.lsl", "AUTH_BUS", "700"),
            new("b.lsl", "AUTH_BUS", "701"),
            new("a.lsl", "ROOT_CONTEXT", "ui.core.root"),
            new("b.lsl", "ROOT_CONTEXT", "ui.core.root"),
            new("a.lsl", "KEY_LOCKED", "lock.locked"),
            new("b.lsl", "KEY_LOCKED", "lock.lockedd"),   // typo: not in frozen set
            new("a.lsl", "PLUGIN_LABEL", "Alpha"),
            new("b.lsl", "PLUGIN_LABEL", "Beta"),         // per-script: must be ignored
        };

        var errors = FindInconsistentNames(planted);
        Assert.That(errors.Count, Is.EqualTo(2));
        Assert.That(errors[0], Does.Contain("AUTH_BUS"));
        Assert.That(errors[1], Does.Contain("UNRECOGNIZED"));
    }
}
