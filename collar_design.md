# Collar Design Reference — Linkset & Prim Conventions

This document defines the prim layout and the **prim name/description conventions**
the D/s Collar scripts rely on. If you are building or rescripting a collar object
(or the leash holder), follow this so every feature works without code changes.

All markers are **case-insensitive** and live in the prim's **Description** field
(the prim **Name** stays free for the designer), unless noted otherwise.

---

## 1. The collar object (worn)

### 1.1 Minimum viable build

- A single prim is enough. Put **all scripts** (`collar_kernel.lsl`, every
  `kmod_*.lsl`, and the `plugin_*.lsl` set) in the **root prim**.
- Every linkset scanner below falls back to the root prim gracefully, so nothing
  breaks without child prims — you just lose the optional visual features.
- Optional inventory items:
  - `settings` notecard — pre-configured owners/trustees (syntax: `settings.md`).
  - `D/s Collar outfits setup` notecard — delivered by the Outfits Help button
    (source text: `OUTFITS_SETUP.txt`).

### 1.2 Child prims (optional)

Child prims are pure presentation; the scripts locate them by description:

| Feature | Marker (in prim Description) | Match | Consumer | Behavior |
|---|---|---|---|---|
| Leash point | `leashpoint` | substring | `kmod_leash_engine`, `kmod_particles` | Leash beam emits from and docks at this prim; Lockmeister handshake target. Falls back to the root prim. |
| Lock indicator (locked) | `lock.locked` | substring | `plugin_lock` | Prim is shown (alpha 1.0) when the collar is locked, hidden (alpha 0.0) when unlocked. |
| Lock indicator (unlocked) | `lock.unlocked` | substring | `plugin_lock` | Shown when unlocked, hidden when locked. |
| Bell | `bell` | substring | `plugin_bell` | Every matching prim (root included) is alpha-toggled with the bell visibility setting and jingles on events. |

Notes:

- **Substring matching** means the marker may be followed by other configuration
  text in the same description (OpenCollar-style), e.g.
  `LeashPoint ~ ring 0.5 0.9 1.0` still matches `leashpoint`.
- `lock.locked` / `lock.unlocked` substring matching is unambiguous because
  `lock.locked` is not a substring of `lock.unlocked` (the `lock.` prefix breaks
  the overlap).
- One prim can carry several markers, e.g. description `leashpoint lock.locked`
  is a valid combined leash point + locked-indicator prim.
- The bell scan includes the root prim (a single-prim bell collar works).
- Avoid accidental matches: `bell` matches any description containing the word
  (e.g. "doorbell", "belle"), so keep decorative text away from these words.

### 1.3 Example linkset

```
Collar (root)          desc: (anything — all scripts live here)
├── Ring               desc: leashpoint lock.unlocked
├── Padlock            desc: lock.locked
├── Bell Charm         desc: bell
└── Gem                desc: (no markers)
```

With this build: the leash beam attaches at the Ring; the Padlock appears only
when locked, the Ring's unlocked marker hides when locked; the Bell Charm
toggles with the bell visibility setting.

---

## 2. The leash holder object (separate)

`leash_holder.lsl` is a minimal target responder for hand-held leashes or
leash posts. It answers both D/s Collar native JSON requests and Lockmeister
grab pings on channel **-8888**, so OpenCollar/Lockmeister devices can also
leash to it.

Place the script anywhere in the holder; the leash point resolves in this
priority order:

1. A child prim **named** `LeashPoint` (case-insensitive exact name match).
2. A child prim whose **description** is exactly `leash:point`
   (case-insensitive).
3. The prim containing the script (child or root).

For a leash post, one prim with the script in it is sufficient.

---

## 3. The control HUD object (separate)

`control_hud.lsl` is self-contained: it scans for nearby collars, connects
with ACL enforcement, and renders the remote menu. It places no requirements
on prim names or descriptions — put the script in any prim (root of the HUD).

---

## 4. Why descriptions, not names?

Every collar script locates its prims through the **Description** field so the
**Name** field remains usable by the designer and by inventory tooling. The one
exception is the leash holder's name-based `LeashPoint` lookup (first choice),
which exists because the holder is a standalone object with no competing
conventions; its description fallback keeps it flexible.

If you rename or re-describe prims on an existing collar, the affected
features silently fall back (leash point to root, lock indicators/bell stop
toggling) — no script errors result.
