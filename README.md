# D/s Collar

**D/s Collar** is a modular, script-driven D/s collar system for Second Life.
It provides a modern, lightweight, and extensible framework for collars, emphasizing **security, modularity, and performance**.

This project replaces the old monolithic collars with a clean kernel + module + plugin architecture: shared state lives in linkset data (LSD), control traffic runs over consolidated buses, and strict ABI conventions govern every script.

---

## Features

- **Kernel + module + plugin architecture** — a central kernel supervises 12 kernel modules (kmods) and 18 user-facing plugins; everything registers itself and is hot-pluggable in-world.
- **Central menu service UI** — one rendering service (kmod_menu) owns every dialog layout, page, and picker; plugins only send data and receive picks. No plugin opens dialogs or manages listeners itself.
- **Per-ACL menu views** — each plugin declares an ACL visibility mask; the UI composes a precomputed menu view per access level, so every toucher sees exactly what they are entitled to.
- **Central pickers** — `menu.picker` (list-and-pick) and `menu.sensor` (scan-and-pick: nearby agents, objects, collars) with paging, session handling, and display-name resolution done once, centrally.
- **Single name-resolution authority** — kmod_names resolves every avatar name (TTL cache, same-region fast path, paced dataserver queue), eliminating duplicate resolvers and viewer throttle warnings.
- **Chat command parity** — prefixed chat commands (kmod_chat) open the exact same menus as touch; aliases are declared per plugin.
- **Unified ACL** — owners, trustees, public access, blacklist, and TPE mode through one policy engine (kmod_auth) with user-record roster storage.
- **Full RLV stack** — relay with auth modes and per-source bookkeeping (kmod_rlv), refcounted restriction claims shared by all plugins, strip/folders/outfits/restrictions/exceptions plugins, and RLVa-aware behavior.
- **Leash system** — multi-mode leashing (grab, coffle, post), Lockmeister compatibility, particle rendering, and a separate leash-holder object for hand-held or post use.
- **Outfits & wardrobe** — RLV folder browsing, strip, and outfit wear with protected base-kit handling (see `OUTFITS_SETUP.txt`).
- **Safety first** — always-available SOS menu (long-touch), system-wide safeword, emergency release paths.
- **External HUD support** — remote control via a separate HUD (control_hud) with ACL enforcement and rate limiting.
- **Heartbeat & auto-recover** — the kernel monitors plugin health, prunes dead plugins, and handles script additions/removals.
- **Automated testing** — an in-vitro LSL test harness (C#/YEngine) plus an in-world SmokeTest with scripted agents.

---

## Project Structure

```
ds_collar/
├── README.md, USER_GUIDE.md, SETUP_GUIDE.txt   # user + setup documentation
├── collar_design.md                            # linkset / prim-name conventions
├── OUTFITS_SETUP.txt                           # RLV wardrobe inventory layout
├── settings.md                                 # settings notecard syntax
├── agents.md / agents-style.md                 # LSL technical + style requirements
├── src/lsl/collar/v1.2/
│   ├── dev/                                    # development (authoritative)
│   ├── rc/                                     # release candidate (testing in-world)
│   ├── stable/                                 # stable (public release)
│   ├── gen_versions.sh                         # regenerates the revision dashboard
│   └── script_versions.html                    # live per-script revision table
└── test_harness/                               # C# test harness + in-world SmokeTest
```

Each stage directory (`dev`, `rc`, `stable`) contains the full script set:

- **Kernel** — `collar_kernel.lsl` — plugin registry, lifecycle, heartbeat, resets.
- **Kernel modules (12)** — `kmod_auth.lsl` (ACL/policy engine), `kmod_bootstrap.lsl` (startup coordination, RLV detection, notecard streaming), `kmod_chat.lsl` (chat command aliases), `kmod_dialogs.lsl` (centralized dialog sessions), `kmod_leash_engine.lsl` (leashing engine), `kmod_menu.lsl` (menu rendering + pickers), `kmod_names.lsl` (avatar name resolution authority), `kmod_particles.lsl` (leash beam + Lockmeister), `kmod_remote.lsl` (external HUD bridge), `kmod_rlv.lsl` (RLV relay + shared restriction claims), `kmod_settings.lsl` (persistent key-value store), `kmod_ui.lsl` (root touch menu, sessions, views).
- **Plugins (18)** — `plugin_animate.lsl`, `plugin_bell.lsl`, `plugin_blacklist.lsl`, `plugin_chat.lsl`, `plugin_folders.lsl` (RLV folder browser), `plugin_leash.lsl`, `plugin_lock.lsl`, `plugin_maint.lsl`, `plugin_outfits.lsl` (RLV outfits), `plugin_owners.lsl`, `plugin_public.lsl`, `plugin_relay.lsl`, `plugin_restrict.lsl`, `plugin_rlvex.lsl` (RLV exceptions), `plugin_sos.lsl`, `plugin_status.lsl`, `plugin_strip.lsl`, `plugin_tpe.lsl`.
- **Standalone scripts** — `control_hud.lsl` (external HUD object), `leash_holder.lsl` (leash holder object).

The authoritative source of per-script revisions is the [Script version dashboard](https://d-s-collar-project.github.io/ds_collar/src/lsl/collar/v1.2/script_versions.html) (regenerate locally with `gen_versions.sh` after promotions).

---

## Architecture & Message Buses

The architecture is **LSD-first**: linkset data (`llLinksetData`) is the system of record for shared state — plugin registrations (`reg.*`), ACL user records (`user.*`), per-ACL menu views (`ui.view.*`), settings, rosters, and lock state all live in LSD, where any script reads them directly without a messaging round-trip. Bus messages are kept deliberately small: JSON is used only for compact control envelopes (a `type` plus a few scalar fields) and is avoided on hot paths — button lists and picker items travel as delimited text rows rather than JSON arrays, since JSON parsing is expensive in both memory and time on the Mono VM.

Control traffic runs over consolidated `llMessageLinked` buses (see `src/lsl/collar/v1.2/MESSAGE_BUS_REFERENCE.md` for the wire protocol):

| Constant | Channel | Purpose |
|----------|---------|---------|
| `KERNEL_LIFECYCLE` | 500 | Plugin registration, heartbeat, soft/factory reset |
| `REMOTE_BUS` | 600 | External HUD communication |
| `AUTH_BUS` | 700 | ACL queries and cache invalidation |
| `SETTINGS_BUS` | 800 | Settings read/write, sync, name resolution |
| `UI_BUS` | 900 | Menu routing, UI updates, RLV operations, leash state |
| `DIALOG_BUS` | 950 | Dialog open/close/response/timeout |

External protocols (unified leash/Lockmeister on -8888, the RLV relay channel, and the HUD ACL scan channel) use `llRegionSay` and are documented in the same reference.

### The UI system

The UI is a layered pipeline — plugins never touch `llDialog` or `llListen`:

1. **`kmod_ui`** owns touch, sessions, and the menu *views*. Each plugin self-declares `reg.<ctx> = {cat, label, script, mask}` in linkset data; the mask bit `L` makes the plugin visible at ACL level `L`. Views are precomputed per ACL level and rebuilt on registration changes. A touch resolves the toucher's ACL live, reads their view, and dispatches one render request.
2. **`kmod_menu`** is the single rendering service. It owns the canonical button layout, paging, session bookkeeping, and every picker:
   - Four render shapes — fixed menus, pagers, unordered (A-Z name buttons) and ordered (numbered buttons + body) pickers.
   - **`menu.picker`** — a plugin ships an item list plus optional fixed action buttons (ACL-gated); kmod_menu owns paging and returns `ui.menu.picker.result` with the picked key and page.
   - **`menu.sensor`** — kmod_menu runs the scan (agents, objects, or collars in range), resolves display names via kmod_names, and returns `ui.sensor.result`.
   - Delimited button transport: every button is a `context\tlabel` row, so labels containing `[ ] { }` or commas render correctly and route by context, never by label text.
3. **`kmod_dialogs`** owns the dialog sessions themselves — one listen on a random negative channel per open dialog, timeouts, and click routing.
4. **`kmod_chat`** gives every menu a chat alias; a chat command opens the identical menu session as a touch.

Consequences: buttons can be relabeled without breaking routing, no plugin duplicates paging or listener logic, and access control is enforced at the view layer before any plugin is even contacted.

### ACL Levels

| Level | Name | Description |
|-------|------|-------------|
| -1 | Blacklisted | Explicitly denied access |
| 0 | No Access | Default for unknown users; wearer in TPE mode |
| 1 | Public | Any user, when public mode is enabled |
| 2 | Owned | Wearer, when an owner is set |
| 3 | Trustee | Trusted users with elevated permissions |
| 4 | Unowned | Wearer, when no owner is set |
| 5 | Primary Owner | Full administrative control |

---

## Installation & Setup

1. Build or rez the collar object. Prim conventions (leashpoint, lock indicators, bell) are documented in [collar_design.md](./collar_design.md) — a plain single-prim object works with all scripts' fallbacks.
2. Drop the scripts from the stage you want (`stable/` recommended for use, `rc/` for testing) into the collar's root prim: `collar_kernel.lsl`, all 12 `kmod_*.lsl`, and the `plugin_*.lsl` set you want (all 18 recommended).
3. (Optional) Add a `settings` notecard for pre-configured owners/trustees — syntax in [settings.md](./settings.md).
4. Wear the collar. On reset it bootstraps, detects RLVa/RLV, loads the notecard, and registers all plugins.
5. Touch to open the main menu (long-touch for the SOS menu).

Separate objects:
- **Leash holder** — a new object with `leash_holder.lsl` (prim conventions in [collar_design.md](./collar_design.md)).
- **Control HUD** — a new object with `control_hud.lsl`; it scans for nearby collars and connects automatically.

RLV users: set up the `#RLV` wardrobe layout per [OUTFITS_SETUP.txt](./OUTFITS_SETUP.txt) and put the `D/s Collar outfits setup` notecard in the collar inventory for the Outfits Help button.

---

## Testing

- **`test_harness/`** — a C# harness that compiles the LSL against an in-process YEngine and runs unit tests per script (`DSCollarTests`).
- **`SmokeTest/`** — an automated in-world smoketest with scripted agents; see `test_harness/SmokeTest/README.md`.

Run the harness from `test_harness/` (see `test_harness/QUICKSTART.md`). New plugin behavior should land with harness coverage.

The harness also enforces the cross-script ABI: `ConsistencyTests` parses every dev-stage script and fails if a re-declared constant (bus number, channel, context, LSD key) drifts between files — LSL has no headers, so this check is the compile-time guard the language can't give you.

---

## Documentation Index

| Document | Contents |
|---|---|
| [USER_GUIDE.md](./USER_GUIDE.md) | Feature walkthrough for wearers and owners |
| [SETUP_GUIDE.txt](./SETUP_GUIDE.txt) | Quick-start guide for play |
| [collar_design.md](./collar_design.md) | Linkset layout, prim name/description conventions |
| [OUTFITS_SETUP.txt](./OUTFITS_SETUP.txt) | RLV wardrobe inventory layout |
| [settings.md](./settings.md) | Settings notecard syntax |
| [MESSAGE_BUS_REFERENCE.md](https://github.com/D-s-Collar-project/ds_collar/blob/main/src/lsl/collar/v1.2/MESSAGE_BUS_REFERENCE.md) | Wire protocol (bus message types) |
| [SETTINGS_REFERENCE.md](https://github.com/D-s-Collar-project/ds_collar/blob/main/src/lsl/collar/v1.2/SETTINGS_REFERENCE.md) | Persistent settings keys |
| [Script version dashboard](https://d-s-collar-project.github.io/ds_collar/src/lsl/collar/v1.2/script_versions.html) | Live per-script revision table (dev/rc/stable) |

---

## Contributing

1. Fork the repo.
2. Read [agents.md](https://github.com/D-s-Collar-project/ds_collar/blob/main/agents.md) (LSL technical requirements) and [agents-style.md](https://github.com/D-s-Collar-project/ds_collar/blob/main/agents-style.md) (coding and documentation style) thoroughly — they are binding.
3. Work in `dev/` only; promotions to `rc/` and `stable/` are reconciliation steps (compare the revision dashboard before and after).
4. Bump `REVISION` and record a `CHANGES` entry in the script header for every behavior change.
5. Verify with `lslint` and extend the test harness for new behavior.
6. Submit a pull request with a clear description.

---

## License

GPL v3 License – see [LICENSE](./LICENSE) for details.
