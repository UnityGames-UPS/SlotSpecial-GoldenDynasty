# Hold & Spin — Intended Behaviour

**Draft.** Everything below is taken from the info page and `gdn_config.json`. The detailed
behavioural walkthrough and the reference-video observations are still to come, and no backend
response has ever populated this feature's fields — so treat the wire format section as unverified.

Open gaps and deferred decisions are tracked in `TODO.md`, not here.

---

## 1. The Orb symbol

- **Every Orb always displays an ORB prize on it**, in the base game as well as in the feature.
  An Orb is never a plain symbol.
- Prize values are assigned when the Orb lands.
- Possible prizes: **250, 200, 100, 50, 20, 15, 10, 5, 4, 3, 2, 1**.
- Per the info page these are multiplied by **total credits bet** — see the caution in section 5.

---

## 2. Trigger

- **6 or more** scattered Orbs trigger the feature. The info page enumerates 6 through 14.
- The Orbs that triggered it are **held in position**.
- **All other positions turn into individual spinning reels** — per cell, not per column.
- **3 free spins** are awarded.

---

## 3. During the feature

- If one or more additional Orbs spin up, those Orbs are **also held**, and the free spins remaining
  **reset to 3**.
- **Paytable prizes are not awarded during the feature** — no line wins at all.
- Bet multiplier is locked to the spin that triggered the feature.
- Bonus reels are in play (server-side; the client just renders what arrives).

## 4. Ending

The feature ends when **either**:

- no free spins remain, or
- every Orb position has been collected (all 15 filled).

**All prizes appearing on Orbs are awarded at the end of the feature**, summed.

---

## 5. Notes carried forward

**Individual cell reels are a separate presentation system.** `SlotView`'s spin is column-based —
one transform per reel, one tween each, a shared buffer strip behind a display block. Up to 15
independently spinning cells does not map onto that. This wants its own view rather than a mode of
the existing reels.

**Orbs need to render a number.** No symbol does today — `ApplySymbol` writes a sprite and a size,
nothing more. The per-cell prize values presumably arrive in `payload.orbPrizeMap`, keyed by
position the way `cellMetadata` is for Mystery, but that has been `{}` in every capture so far.

**"Total credits bet" needs verifying before it is trusted.** The Scatter's info text says the same
thing — *"Wins multiplied by total bet"* — yet captured spins pay it at **× 0.10** (bet per line),
not × 5.00. The same wording has already misled us once. For a 250 Orb the difference is **1250 vs
25**, so this needs confirming with the backend team rather than assuming.

**Trigger range stops at 14.** Possibly just enumeration, possibly meaningful — worth asking what
happens if all 15 land at once.

---

## 6. Wire format — declared but never populated

`features.holdAndSpin` already carries these field names; none has been seen with a real value:

| Field | Expected meaning |
|---|---|
| `active` | feature currently running |
| `triggered` | this spin started it |
| `spinsRemaining` | free spins left, presumably post-decrement like `freeGameCount` |
| `orbCount` | Orbs held in total |
| `newOrbCount` | Orbs added this spin (the reset trigger) |
| `totalOrbPayout` | summed prize, presumably paid at the end |
| `heldPositions` | which cells hold Orbs — shape unknown |

`payload.orbPrizeMap` is the other unknown, presumably position → prize value.

**Known bug:** `orbCount` reported **0** on three separate base-game spins that visibly had an Orb
on the board. Logged in `TODO.md`. It matters here — if it stays broken it cannot be used to drive
any build-up toward the trigger.

---

## Still to define

The behavioural walkthrough and reference-video detail, covering at least: trigger anticipation,
the award/announcement presentation, whether a Start button is involved, how held Orbs read against
spinning cells, feedback when a new Orb lands and resets the count, and the closing summary.
