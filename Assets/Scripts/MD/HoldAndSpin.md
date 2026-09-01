# Hold & Spin — Intended Behaviour

**Draft.** Sections 1–2 come from the info page and `gdn_config.json`. Sections 3–7 are the
reference-video walkthrough — what the feature should look like, not how it will be built. Section
10 is the wire format, now **confirmed** against a complete captured round rather than inferred.

No code has been written for this feature yet. The per-cell reel problem in section 8 is the thing
that decides how much of it is new infrastructure.

---

## 1. The Orb symbol

- **Every Orb always displays an ORB prize on it**, in the base game as well as in the feature.
  An Orb is never a plain symbol.
- Prize values are assigned when the Orb lands.
- Possible prizes: **250, 200, 100, 50, 20, 15, 10, 5, 4, 3, 2, 1**.
- **Multiplied by bet per line, NOT "total credits bet"** as the info page claims. Confirmed across
  27 captured prize values, every one of which divides by the bet into a valid tier — including
  `0.1 → 1` and `5 → 50`. The info page is wrong here in exactly the way it is wrong for the
  Scatter.

---

## 2. Trigger

Orbs land and behave completely normally on every spin — same as any other symbol, animating on
landing per the existing rule. **The client never counts them or decides the trigger.** The server
sends the decision; 6 or more scattered Orbs is simply the published rule (the info page enumerates
6 through 14).

---

## 3. Trigger presentation

Once a triggering spin lands:

1. The triggering Orbs **hold in position for about 1 second** before anything else happens.
2. A **full-screen animation** plays.
3. When it ends, several things swap together:
   - the SlotShed sprite swaps
   - the background sprite swaps
   - the Orbs' own animation changes to a different one (they have been animating continuously
     since they landed and never stop)
   - the **"KingAnimation" object is deactivated**
4. A graphic appears above the SlotShed — **the same graphic object used above Free Spins** —
   reading **"PRESS START FEATURE BUTTON"**.
5. The **`top` object fades out**.
6. The Spin button becomes the **Start** button.
7. The Orbs keep animating throughout this whole wait. The player has to press Start to continue.

---

## 4. Starting the feature

When the player presses Start:

- The graphic that showed the prompt now shows a **number** — the current Orb count. It starts at
  whatever count triggered the feature (e.g. 6) and **counts up by 1 the moment each Orb lands**,
  not once per full reel stop. So the counter can climb mid-spin, cell by cell, rather than jumping
  once when all reels are down.
- A **separate free-spins-remaining count** is also shown.
- The spin begins, and it looks visually different from a normal spin: **each of the 15 positions
  appears to be its own independent reel**, with images scrolling through it individually rather
  than 5 columns moving together. See section 8 — how this is actually built is still completely
  open.

---

## 5. During the feature

- Every Orb animates **the instant it lands**, without waiting for the rest of the reels — same
  immediate-animate rule as section 2, and the same rule the counter follows (per Orb, not per
  full stop).
- If one or more additional Orbs land, they are also held, and free spins remaining **resets to 3**.
- **Paytable prizes are not awarded during the feature** — no line wins at all.
- Bet multiplier is locked to the spin that triggered the feature.
- Bonus reels are in play (server-side only — the client just renders what arrives).

---

## 6. Ending

The feature ends when **either**:

- no free spins remain, or
- every position has been collected (all 15 filled).

(Server-decided, same as the trigger — the client does not compute this.)

---

## 7. Win presentation

1. Everything shown above the SlotShed during the feature — the prompt/counter graphic and the
   free-spins-remaining count — **deactivates**.
2. A new graphic appears reading **"Winner"**, with a text box underneath it for the total win.
3. **The count-up is not a simple roll from 0 to the total.** Instead, one Orb at a time:
   - a line/"dragon" effect travels from that Orb's position to the Winner graphic
   - once it arrives, the Winner graphic plays an animation
   - the total-win text updates, adding that Orb's prize to the running total
   - the next Orb's line starts **right after the current Orb's Winner-graphic animation ends** —
     sequential, not parallel, and not one line per frame
   - repeats until every held Orb has been processed
4. **Once every Orb has been through that loop**, the win text's holder graphic **sprite-swaps**
   and starts a **looping** animation, and the "Winner" graphic **also** starts its own (separate)
   animation.
5. **Then the text counts up from 0 to the total win amount.**
   > This is written exactly as described, and it reads as contradictory: step 3 already brings the
   > total-win text up to the full total, one Orb at a time. Step 5 says the same text then counts
   > up from 0 again. Flagging rather than resolving — see the open question in section 8.
6. Once that count-up finishes:
   - the screen fades to black — **the same dark overlay used in the Free Games summary** —
     while the `top` object fades back in **at the same time**
   - the black overlay then fades back out
   - the background and most of the feature's swapped elements revert (including the
     **KingAnimation reactivating**)
   - the only thing left on screen from the feature is the **win count-up number and its holder**,
     which is still animating (the loop from step 4)
7. The Spin button is now **Take**. Pressing it removes the win amount and its holder, and the
   base game resumes.

> Noted as-is: the fade-to-black happening before the background reverts, while the win text stays
> up throughout, does not read as a clean sequence on paper — same caveat as Free Games' own
> mistimed-looking fade. Treat this as intentional unless told otherwise.

---

## 8. Open questions from this walkthrough

**The 15-independent-reel presentation — likely 15 real mini-reels.** A colleague has built the
same thing in another game and we may be able to clone their approach; that is the current
direction rather than trying to bend the existing column reels into it.
- `SlotView`'s spin is column-based — one transform per reel, one tween per column, a shared
  buffer strip behind a display block. Nothing about that maps onto 15 independent strips, so this
  wants its own system regardless of which approach wins.
- Masking a shared strip was considered and rejected: it doesn't answer the **hold** problem. Once
  an Orb is held, the reel behind it can't keep scrolling through the same visible area without the
  Orb either scrolling with it or being composited on a separate layer cut off *per cell* rather
  than across the whole reel band. That per-cell cutoff is the crux.
- Whatever the mechanism, it has to support Orbs held mid-feature while other cells keep spinning
  around them — not a start/stop state for the whole grid.
- Confirmed from the captures (section 10): the non-held cells cycle the **full normal symbol
  set**, Wilds and Scatters included, not Orbs and blanks. So each mini-reel needs a real strip of
  ordinary symbols, not a two-symbol placeholder.

**The double count-up in section 7 (steps 3 and 5) is intentional.** Confirmed — the total climbs
once as each Orb's line arrives, then the text counts up from 0 to the same total again. It reads
oddly on paper but that is what the reference does.

**The line/"dragon" trace effect technique is unconfirmed.** "Linetrace" was mentioned as a
possible approach but not verified — needs checking with whoever suggested it.

**No anticipation build-up is described for the Orb trigger**, unlike the scatter build-up in Free
Games. Worth confirming whether one exists in the reference and simply wasn't covered, or whether
Hold & Spin genuinely has no build-up and the 1-second hold in section 3 is the entire cue.

---

## 9. Notes carried forward

**Orbs need to render a number.** No symbol does today — `ApplySymbol` writes a sprite and a size,
nothing more. The per-cell prize values arrive in `payload.orbPrizeMap` (see section 10), so the
data side is solved; what is missing is a way to draw text on a symbol.

**Trigger range stops at 14.** Possibly just enumeration, possibly meaningful — worth asking what
happens if all 15 land at once. Still unconfirmed: the captured round peaked at 10.

---

## 10. Wire format — confirmed

Decoded from a complete captured round: a triggering spin, ten feature spins, the payout, and the
base spins either side. Everything below is observed, not inferred.

### Field semantics

| Field | Confirmed meaning |
|---|---|
| `active` | true for the whole feature, including the triggering spin. Goes false on the payout spin. |
| `triggered` | true **only** on the spin that starts the feature. The intro cue. |
| `spinsRemaining` | Post-decrement, like `freeGameCount`. 3 on trigger, counts down, resets to 3 whenever an Orb lands. |
| `orbCount` | Orbs held in total. Always equals `heldPositions.Count`. |
| `newOrbCount` | Orbs added **this spin**, excluding the triggering batch (0 on the trigger spin). Can exceed 1. |
| `totalOrbPayout` | 0 for the whole round, then the final sum on the payout spin. Not a running total. |
| `heldPositions` | `"row:col"` strings, same key format as `cellMetadata` and `orbPrizeMap`. |
| `payload.orbPrizeMap` | `"row:col"` → prize, one entry per Orb on the board, refreshed every spin. |

### Observed lifecycle

| Spin | `active` | `triggered` | `spinsRemaining` | `orbCount` | `newOrbCount` | Balance |
|---|---|---|---|---|---|---|
| trigger | true | **true** | 3 | 6 | 0 | −5.00 (charged) |
| 1 | true | false | 2 | 6 | 0 | — |
| 2 | true | false | 1 | 6 | 0 | — |
| 3 — new Orb | true | false | **3** | 7 | 1 | — |
| 4 — two new | true | false | **3** | 9 | 2 | — |
| 5, 6 | true | false | 2, 1 | 9 | 0 | — |
| 7 — new Orb | true | false | **3** | 10 | 1 | — |
| 8, 9 | true | false | 2, 1 | 10 | 0 | — |
| 10 — end | **false** | false | 0 | 10 | 0 | **+11.10** |

### Confirmed facts

- **The triggering spin is charged; every feature spin is free.** Balance was flat for the whole
  round, then moved once on the payout.
- **The payout arrives through `currentWinning`**, equal to `totalOrbPayout` on the final spin
  (11.1 in the capture, exactly the sum of all ten `orbPrizeMap` values). The client needs no
  special handling — it already treats `currentWinning` as the authoritative total.
- **Orbs do NOT pay outside the feature.** Base spins with 4 and 5 Orbs both reported
  `currentWinning: 0`. Raised with the backend team; until they change it, the client simply shows
  whatever total they send.
- **The reset works exactly as specified** — `spinsRemaining` went 1 → 3 and 2 → 3, always on a
  spin where `newOrbCount > 0`.
- **Non-held cells cycle the full normal symbol set**, including Wilds and Scatters — not Orbs and
  blanks. This answers the open question in section 8 about what the 15 mini-reels show. The bonus
  strip is simply Orb-heavier (`holdReelsInstance: 8` vs `reelsInstance: 4`).
- **`scatterCount` is suppressed during the feature.** Several feature spins had visible Scatters
  and still reported 0 — consistent with paytable prizes not being awarded. Do not drive scatter
  anticipation off it while `active` is true.
- **`heldPositions` order is landing order, batched** — sorted row-major within each batch, with
  later batches appended. Useful directly for the section 7 "one Orb at a time" reveal sequence.
- **The triggering spin already reports `active: true` and `spinsRemaining: 3`.** The server does
  not wait for the player to press Start, exactly as Free Games already reports `freeGameCount: 6`
  on its own trigger spin. Intro pacing is entirely the client's; `triggered: true` is the cue.

### Known bug

**`orbCount` and `heldPositions` persist stale after a round ends.** On the base spin following the
capture, `orbPrizeMap` correctly described the 4 Orbs on that board while `orbCount` still read 10
and `heldPositions` still held the finished round's ten cells. `totalOrbPayout`, `active`,
`triggered` and `spinsRemaining` all reset properly. Self-corrects on the next trigger, so it only
leaks on non-trigger spins between rounds — but nothing outside the feature should read either
field until it is fixed.

Note the earlier suspicion that `orbCount: 0` outside the feature was a bug: **it was not.** The
whole block is simply dormant when no feature is running, and it populates correctly the moment a
trigger happens.

### Still unknown

**The all-15-filled end condition.** The captured round peaked at 10 Orbs and ended on
`spinsRemaining` reaching 0. Whether filling the board ends the round early, reports a non-zero
`spinsRemaining` when it does, or awards anything extra is all unobserved.

---

Everything in sections 3, 4, 6 and 7 that is pure presentation (the SlotShed/background swap, the
KingAnimation toggle, the fade sequencing) needs no wire data of its own — it is driven by
`active`/`triggered`/`spinsRemaining` the same way Free Games is driven by `isFreeGame` and
`freeGameCount`.
