using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Presentation for the Hold & Spin feature: the fifteen independent cell reels, the trigger
/// sequence, the counters, and the closing payout. See Assets/Scripts/MD/HoldAndSpin.md.
///
/// View layer only, same contract as FreeGameView. GameManager drives this and receives callbacks
/// when a sequence finishes; nothing here calls back into the game loop.
///
/// The feature takes the board over IN PLACE — same fifteen positions, same size, same SlotShed
/// object. Only the surroundings change. That is why the Orb layer is shared with the base game
/// rather than duplicated here: its slots already sit over these exact positions, so SlotView draws
/// every held Orb and this script never touches an Orb sprite or a prize value.
///
/// Attach to a GameObject that stays active for the whole session, not to the cell layer itself —
/// deactivating that would halt these coroutines mid-round.
/// </summary>
public class HoldAndSpinView : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SlotView slotView;

    [Header("Cell Grid")]
    [Tooltip("Root of the fifteen cell reels. Activated for the round; the column reels hide behind it.")]
    [SerializeField] private GameObject cellLayerRoot;

    [Tooltip("One entry per reel column, each holding the 3 row cells top to bottom. Same shape as the win, Mystery and Orb layers — wire it column by column, matching the hierarchy.")]
    [SerializeField] private List<HoldAndSpinCellColumn> cellColumns = new List<HoldAndSpinCellColumn>(5);

    [Header("Trigger Presentation")]
    [Tooltip("Full-screen animation played once the triggering Orbs have held. Deactivated again when it finishes.")]
    [SerializeField] private GameObject fullScreenIntro;

    [Tooltip("The \"PRESS START FEATURE BUTTON\" graphic. Its own object, NOT the one Free Games uses.")]
    [SerializeField] private GameObject pressStartFeature;
    [SerializeField] private CanvasGroup pressStartFeatureGroup;

    [Header("Counters")]
    [Tooltip("Parent holding the prompt AND the counters. Raised the moment the trigger sequence ends — the prompt lives inside it — and hidden for the payout.")]
    [SerializeField] private GameObject counterPanel;
    [Tooltip("Orb count. Climbs by one the moment each Orb lands, not once per full stop. Hidden until Start is pressed.")]
    [SerializeField] private TMPro.TMP_Text orbCountText;
    [Tooltip("The \"Total 15 Win\" graphic beside the counters. Raised with them when the round starts.")]
    [SerializeField] private GameObject total15WinGraphic;
    [Tooltip("The respins-remaining graphic. A single static sprite — the number itself goes in spinsRemainingCount below.")]
    [SerializeField] private Image spinsRemainingImage;
    [Tooltip("Respins left in the round. Counts 3 down to 1 and snaps back to 3 the moment an Orb lands.")]
    [SerializeField] private TMPro.TMP_Text spinsRemainingCount;

    [Header("Payout")]
    [Tooltip("Parent for the whole payout presentation — both holders and the Winner graphic.")]
    [SerializeField] private GameObject winnerPanel;
    [SerializeField] private CanvasGroup winnerPanelGroup;

    [Tooltip("The \"Winner\" graphic. Distinct from the \"Win\" label inside the red holder. Raised for the second count-up only.")]
    [SerializeField] private GameObject winnerGraphic;
    [SerializeField] private ImageAnimation winnerGraphicAnim;

    [Header("Payout — Winner Blue (running total)")]
    [Tooltip("Holder for the total that climbs one Orb at a time. Animates on the holder itself.")]
    [SerializeField] private GameObject winnerBlue;
    [SerializeField] private ImageAnimation winnerBlueAnim;
    [SerializeField] private TMPro.TMP_Text winnerBlueText;

    [Header("Payout — Winner Red (final count-up)")]
    [Tooltip("Holder for the count-up from zero. Unlike blue it carries no animation of its own — the movement is in the two groups below.")]
    [SerializeField] private GameObject winnerRed;
    [SerializeField] private TMPro.TMP_Text winnerRedText;

    [Tooltip("Parent of the animating side effects. Its ImageAnimations are found in its children, so however many there are they need no wiring.")]
    [SerializeField] private CanvasGroup winnerRedEffectsGroup;

    [Tooltip("Parent of the \"Win\" graphics. Pulses in OPPOSITE phase to the effects above — at 1 when they are at 0.")]
    [SerializeField] private CanvasGroup winnerRedWinGroup;

    [Header("Board Dressing")]
    [Tooltip("The SlotShed's Image. NOT sprite-swapped — the same object stays at the same size and only its colour changes.")]
    [SerializeField] private Image slotShedImage;
    [Tooltip("The background Image, which IS sprite-swapped for the round.")]
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Sprite featureBackgroundSprite;
    [Tooltip("Deactivated for the duration of the round and switched back on at the end.")]
    [SerializeField] private GameObject kingAnimation;

    [Header("Overlays")]
    [Tooltip("The 'top' parent holding the payout values. Faded out for the round, back in at the end.")]
    [SerializeField] private CanvasGroup topGroup;
    [Tooltip("Blackout used to hide the board changing back at the very end. NOT a backdrop for the payout — that plays over the live feature board. Goes to FULL alpha, so it needs to cover everything except the red holder.")]
    [SerializeField] private CanvasGroup darkOverlayGroup;

    // Deliberately NOT [SerializeField], matching FreeGameView and SlotView: serialized values are
    // saved in the scene and silently override anything changed here, which makes retuning in code
    // look broken. Code is the single source of truth; the trade is a recompile.
    private const float triggerOrbHold = 1.0f;          // Orbs sit before anything else happens
    private const float promptPulseAlpha = 0.25f;
    private const float promptPulseDuration = 0.7f;
    private const float cellStopStagger = 0.08f;
    private const float overlayFadeDuration = 0.5f;
    // The closing blackout, kept separate from overlayFadeDuration so it can be paced on its own.
    // Slower on purpose: it is the beat that ends the feature, and at half a second it read as a
    // flicker rather than a transition.
    private const float blackoutFadeDuration = 1.2f;
    private const float payoutHoldBeforeCountUp = 0.4f;
    private const float payoutCountUpDuration = 2.0f;
    private const float redPulseDuration = 0.6f;        // one half of the cross-fade, effects <-> Win

    private Coroutine activeSequence;
    private Tween promptPulseTween;
    private Tween redPulseTween;
    private Action pendingTakeCallback;

    // Cached on first use from winnerRedEffectsGroup's children, so however many side effects the
    // scene ends up with, none of them need wiring.
    private ImageAnimation[] redEffectAnims;

    private Color slotShedOriginalColour;
    private Sprite backgroundOriginalSprite;
    private bool dressingCaptured;
    private bool missingRefsLogged;

    // Cells holding an Orb, as flat indices. This is the client's entire record of what is held —
    // it is diffed against orbPrizes each spin to find the new Orbs, which is why none of
    // orbCount, newOrbCount or heldPositions is ever read. See HoldAndSpin.md section 8.
    private readonly HashSet<int> heldCells = new HashSet<int>();

    // The triggering spin's Orbs and their prizes. Kept for the end of the round: the column reels
    // are still showing that spin's board, so the Orb layer has to be restored to match it rather
    // than to the round's final held set — those extra Orbs landed on cell reels that are about to
    // disappear, and their prizes would be left floating over ordinary symbols.
    private readonly Dictionary<int, double> triggerOrbPrizes = new Dictionary<int, double>();

    private void Awake()
    {
        foreach (var cell in AllCells())
        {
            cell.SetupFromHierarchy();
        }
    }

    /// <summary>
    /// The cell at a flat index (row * reelCount + col) — the space orbPrizes, WinLine.positions
    /// and mysteryPositions all use. Converts into the column-major storage the Inspector holds.
    /// </summary>
    private HoldAndSpinCell GetCell(int flatIndex)
    {
        if (cellColumns == null || ReelCount <= 0) return null;

        int row = flatIndex / ReelCount;
        int col = flatIndex % ReelCount;

        if (col < 0 || col >= cellColumns.Count) return null;

        var column = cellColumns[col];
        if (column?.rows == null || row < 0 || row >= column.rows.Count) return null;

        return column.rows[row];
    }

    // Every wired cell, in no particular order — for the operations that touch all of them.
    private IEnumerable<HoldAndSpinCell> AllCells()
    {
        if (cellColumns == null) yield break;

        foreach (var column in cellColumns)
        {
            if (column?.rows == null) continue;

            foreach (var cell in column.rows)
            {
                if (cell != null) yield return cell;
            }
        }
    }

    #region Public API — called by GameManager

    /// <summary>
    /// The triggering spin landed. Holds the Orbs for a beat, plays the full-screen animation,
    /// changes the board dressing, and leaves the prompt pulsing. The Start button is UIManager's.
    ///
    /// The triggering Orbs are already drawn by SlotView's base-game Orb pass, so they are simply
    /// adopted here rather than redrawn — which is also what stops their animations restarting.
    /// </summary>
    internal void BeginTrigger(Dictionary<int, double> orbPrizes, Action onComplete)
    {
        StopActiveSequence();
        activeSequence = StartCoroutine(TriggerRoutine(orbPrizes, onComplete));
    }

    /// <summary>
    /// Start was pressed. The prompt gives way to the counters and the round can begin.
    /// </summary>
    internal void StartRound(int spinsRemaining, Action onReady)
    {
        StopPromptPulse();
        if (pressStartFeature != null) pressStartFeature.SetActive(false);

        if (counterPanel != null) counterPanel.SetActive(true);
        SetRoundCountersVisible(true);
        UpdateCounters(spinsRemaining);

        onReady?.Invoke();
    }

    /// <summary>
    /// Starts every unheld cell scrolling. Called when the spin request goes out, not when the
    /// response lands — the same split the base game uses, so the board is never sitting still
    /// while the server thinks. Held cells refuse the call.
    /// </summary>
    internal void StartCellSpin()
    {
        List<int> fillerIds = slotView != null ? slotView.GetHoldAndSpinFillerIds() : null;

        foreach (var cell in AllCells())
        {
            if (cell.IsLocked) continue;

            FillCellStrip(cell, fillerIds);
            cell.StartSpin();
        }
    }

    /// <summary>
    /// Lands the respin: stops the scrolling cells one at a time and holds whichever landed an Orb.
    /// The spinning half is <see cref="StartCellSpin"/>, already running by the time this is called.
    /// </summary>
    internal void RunSpin(List<List<int>> resultMatrix, Dictionary<int, double> orbPrizes, int spinsRemaining, Action onComplete)
    {
        StopActiveSequence();
        activeSequence = StartCoroutine(SpinRoutine(resultMatrix, orbPrizes, spinsRemaining, onComplete));
    }

    /// <summary>
    /// The round is over. Counters out, Winner panel in, total counts up, then Take.
    ///
    /// A straight count-up for now. Section 7's per-Orb walk — a copy flying from each Orb to the
    /// panel, dimming its cell, adding its prize to a running total — is deferred. When it is
    /// built it will need each held Orb's screen position, which means exposing the Orb layer's
    /// slot rects from SlotView; heldCells already carries the indices to ask for.
    /// </summary>
    internal void PlayOutro(double roundWin, Action onCountUpComplete, Action onComplete)
    {
        StopActiveSequence();
        pendingTakeCallback = onComplete;
        activeSequence = StartCoroutine(OutroRoutine(roundWin, onCountUpComplete));
    }

    /// <summary>Take was pressed. Fires whatever PlayOutro was given as its completion callback.</summary>
    internal void OnTakePressed()
    {
        var callback = pendingTakeCallback;
        pendingTakeCallback = null;
        callback?.Invoke();
    }

    /// <summary>Puts everything back the way the base game expects it. Safe to call at any point.</summary>
    internal void ResetToDefault()
    {
        StopActiveSequence();
        StopPromptPulse();
        StopRedPulse();
        StopPayoutAnimations();

        heldCells.Clear();

        foreach (var cell in AllCells())
        {
            cell.ResetCell();
        }

        if (cellLayerRoot != null) cellLayerRoot.SetActive(false);
        if (fullScreenIntro != null) fullScreenIntro.SetActive(false);
        if (pressStartFeature != null) pressStartFeature.SetActive(false);
        SetRoundCountersVisible(false);
        if (counterPanel != null) counterPanel.SetActive(false);
        if (winnerPanel != null) winnerPanel.SetActive(false);
        if (winnerBlue != null) winnerBlue.SetActive(false);
        if (winnerRed != null) winnerRed.SetActive(false);
        if (winnerGraphic != null) winnerGraphic.SetActive(false);

        SetGroupAlpha(darkOverlayGroup, 0f, false);
        SetGroupAlpha(topGroup, 1f, true);

        RestoreBoardDressing();

        if (slotView != null) slotView.SetColumnReelsVisible(true);

        pendingTakeCallback = null;
    }

    #endregion

    #region Trigger

    private IEnumerator TriggerRoutine(Dictionary<int, double> orbPrizes, Action onComplete)
    {
        // The Orbs that triggered are the round's starting held set. They are already on the Orb
        // layer from the base-game pass, so adopting them here means their animations carry
        // straight through the trigger without a restart.
        //
        // The prizes are kept as well, because the board the base game returns to at the end is
        // this one — SlotView's display matrix is never written during a round — so the Orb layer
        // has to be put back to match it. See RestoreBoardForBaseGame.
        heldCells.Clear();
        triggerOrbPrizes.Clear();

        if (orbPrizes != null)
        {
            foreach (var entry in orbPrizes)
            {
                heldCells.Add(entry.Key);
                triggerOrbPrizes[entry.Key] = entry.Value;
            }
        }

        // 1. Everything sits for a beat. This is the entire trigger cue — there is no anticipation
        //    build-up for Orbs, unlike the scatter tease in Free Games.
        yield return new WaitForSeconds(triggerOrbHold);

        // 2. Full-screen animation.
        if (fullScreenIntro != null)
        {
            fullScreenIntro.SetActive(true);
            yield return WaitForImageAnimation(fullScreenIntro);
            fullScreenIntro.SetActive(false);
        }

        // 3. The board changes around the symbols, which do not move.
        ApplyBoardDressing();

        // 4. The cell reels take over the column reels' positions, with the already-held Orbs
        //    frozen in place.
        PrepareCellLayer();

        // 5. Prompt up, payout values out. The panel comes up whole — the prompt is a descendant of
        //    it, so it cannot be shown without raising the panel first. Only the counters inside
        //    stay hidden until Start.
        ShowAwardPrompt();

        if (topGroup != null) topGroup.DOFade(0f, overlayFadeDuration);

        activeSequence = null;
        onComplete?.Invoke();
    }

    private void PrepareCellLayer()
    {
        if (slotView != null) slotView.SetColumnReelsVisible(false);
        if (cellLayerRoot != null) cellLayerRoot.SetActive(true);

        // The board the column reels are showing right now. The cell layer has to reproduce it
        // exactly, or the swap from one to the other is a visible jump — every non-Orb position
        // would flick to whatever filler its strip happened to be authored with.
        List<List<int>> landedBoard = slotView != null ? slotView.GetCurrentDisplayMatrix() : null;
        int reelCount = ReelCount;
        int cellCount = reelCount * RowCount;

        for (int i = 0; i < cellCount; i++)
        {
            HoldAndSpinCell cell = GetCell(i);
            if (cell == null) continue;

            cell.ResetCell();

            if (heldCells.Contains(i))
            {
                BlankHeldCell(cell);
                cell.Freeze();
                continue;
            }

            // Not held, so it will spin on the first respin — but until then it has to carry on
            // showing whatever the trigger spin landed there.
            int symbolId = ReadMatrix(landedBoard, i % reelCount, i / reelCount);
            if (symbolId >= 0 && slotView != null)
            {
                Image[] strip = cell.StripImages;
                if (strip != null && strip.Length > 0) slotView.WriteSymbol(strip[0], symbolId);
            }
        }
    }

    /// <summary>
    /// Clears a held cell's visible slot to the empty-cell sprite.
    ///
    /// The Orb layer draws the Orb over this cell, and its sprite does not cover the whole rect —
    /// an Orb is round, so its corners are transparent. Whatever sits underneath shows through
    /// them, for the entire round, so the cell is blanked rather than left holding a symbol.
    ///
    /// Applies to both routes into a hold. A triggering Orb never lands — it is frozen straight out
    /// of PrepareCellLayer — so it would otherwise keep whatever filler the strip was authored
    /// with. One landing mid-round would otherwise keep the Orb the matrix gave it, which reads
    /// better but leaves the two kinds of held cell looking different at the edges.
    /// </summary>
    private void BlankHeldCell(HoldAndSpinCell cell)
    {
        if (slotView == null || cell == null) return;

        Image[] strip = cell.StripImages;
        if (strip == null || strip.Length == 0 || strip[0] == null) return;

        slotView.WriteEmptySymbol(strip[0]);
    }

    #endregion

    #region Spin

    private IEnumerator SpinRoutine(List<List<int>> resultMatrix, Dictionary<int, double> orbPrizes, int spinsRemaining, Action onComplete)
    {
        if (!HasRequiredRefs())
        {
            activeSequence = null;
            onComplete?.Invoke();
            yield break;
        }

        // Stop column-major so the board settles left to right, the way the base reels do.
        int reelCount = ReelCount;
        int rowCount = RowCount;

        for (int col = 0; col < reelCount; col++)
        {
            for (int row = 0; row < rowCount; row++)
            {
                int flatIndex = row * reelCount + col;
                HoldAndSpinCell cell = GetCell(flatIndex);

                if (cell == null || cell.IsLocked) continue;

                // Write the landed symbol first, then snap — the same frame, so the write is never
                // seen mid-scroll.
                int symbolId = ReadMatrix(resultMatrix, col, row);
                if (symbolId >= 0 && slotView != null)
                {
                    Image[] strip = cell.StripImages;
                    if (strip != null && strip.Length > 0) slotView.WriteSymbol(strip[0], symbolId);
                }

                cell.Stop();

                // An Orb landing holds its cell and counts up immediately — per Orb, mid-spin,
                // rather than once when the whole board is down.
                if (orbPrizes != null && orbPrizes.TryGetValue(flatIndex, out double prize) && !heldCells.Contains(flatIndex))
                {
                    heldCells.Add(flatIndex);
                    cell.Freeze();

                    // Same frame as HoldOrb below, so the Orb layer is up before the cell empties
                    // — the player never sees the gap between the two.
                    BlankHeldCell(cell);

                    if (slotView != null) slotView.HoldOrb(flatIndex, prize);
                    UpdateCounters(spinsRemaining);
                }

                yield return new WaitForSeconds(cellStopStagger);
            }
        }

        UpdateCounters(spinsRemaining);

        activeSequence = null;
        onComplete?.Invoke();
    }

    private void FillCellStrip(HoldAndSpinCell cell, List<int> fillerIds)
    {
        Image[] strip = cell.StripImages;
        if (strip == null || slotView == null) return;

        for (int i = 0; i < strip.Length; i++)
        {
            if (strip[i] == null) continue;

            int symbolId = (fillerIds != null && fillerIds.Count > 0)
                ? fillerIds[UnityEngine.Random.Range(0, fillerIds.Count)]
                : 0;

            slotView.WriteSymbol(strip[i], symbolId);
        }
    }

    // resultMatrix is column-major ([reel][row]), the transpose of the server's rows.
    private static int ReadMatrix(List<List<int>> resultMatrix, int col, int row)
    {
        if (resultMatrix == null || col < 0 || col >= resultMatrix.Count) return -1;

        var column = resultMatrix[col];
        if (column == null || row < 0 || row >= column.Count) return -1;

        return column[row];
    }

    #endregion

    #region Outro

    private IEnumerator OutroRoutine(double roundWin, Action onCountUpComplete)
    {
        // 1. The counters go; the payout owns the screen from here.
        if (counterPanel != null) counterPanel.SetActive(false);
        StopPromptPulse();
        if (pressStartFeature != null) pressStartFeature.SetActive(false);

        // 2. The payout panel comes up over the live feature board — no overlay behind it.
        if (winnerPanel != null) winnerPanel.SetActive(true);
        SetGroupAlpha(winnerPanelGroup, 1f, true);

        // 3. Blue: the running total. Its per-Orb walk is not built yet, so the figure is written
        //    straight to the finished total — which is exactly where the walk ends up, so adding it
        //    later replaces one assignment and disturbs nothing else here.
        ShowWinnerBlue(roundWin);

        yield return new WaitForSeconds(payoutHoldBeforeCountUp);

        // 4. Red takes over, and only now does the Winner graphic come up. Blue's holder goes with
        //    it — the two are never on screen together.
        ShowWinnerRed();

        // 5. Count up from zero to the same total. The second climb is intentional: the totals
        //    match, but they are different objects in different holders, so nothing visibly drops
        //    back to zero.
        if (winnerRedText != null && roundWin > 0)
        {
            double shown = 0;
            yield return DOTween.To(
                    () => (float)shown,
                    value =>
                    {
                        shown = value;
                        winnerRedText.text = value.ToString(SpriteTextFormatter.MoneyFormat);
                    },
                    (float)roundWin,
                    payoutCountUpDuration)
                .SetEase(Ease.OutQuad)
                .WaitForCompletion();
        }

        if (winnerRedText != null) winnerRedText.text = roundWin.ToString(SpriteTextFormatter.MoneyFormat);

        // 6. Only now does the screen go dark. The overlay is a cover for putting the board back,
        //    not a backdrop for the payout — it plays over the live feature board. The payout
        //    values fade back in at the same time.
        yield return RaiseBlackout();

        // 7. Everything except the red holder reverts while nothing can be seen: the cell layer,
        //    the Orb layer, the board dressing, the column reels.
        RestoreBoardForBaseGame();

        // 8. Back out, leaving the red holder alone on a base-game board — still counting its
        //    animations, still showing the total.
        yield return LowerBlackout();

        // 9. Take becomes pressable, and it now has only the red holder left to clear. GameManager
        //    owns the button; OnTakePressed carries on.
        onCountUpComplete?.Invoke();

        activeSequence = null;
    }

    // Full alpha, not a tint: this has to hide the board completely while it changes back.
    private IEnumerator RaiseBlackout()
    {
        // Same duration as the blackout, so the two genuinely move together rather than one
        // trailing the other.
        if (topGroup != null)
        {
            topGroup.gameObject.SetActive(true);
            topGroup.DOFade(1f, blackoutFadeDuration);
        }

        if (darkOverlayGroup == null)
        {
            // Unwired: the reverts still have to happen, they are just not hidden.
            yield return new WaitForSeconds(blackoutFadeDuration);
            yield break;
        }

        darkOverlayGroup.gameObject.SetActive(true);
        yield return darkOverlayGroup.DOFade(1f, blackoutFadeDuration).WaitForCompletion();
    }

    private IEnumerator LowerBlackout()
    {
        if (darkOverlayGroup == null) yield break;

        yield return darkOverlayGroup.DOFade(0f, blackoutFadeDuration).WaitForCompletion();
        darkOverlayGroup.gameObject.SetActive(false);
    }

    /// <summary>
    /// Puts the board back to its base-game state, leaving the payout alone.
    ///
    /// Deliberately narrower than ResetToDefault: this runs behind the blackout, BEFORE the player
    /// has taken the win, so the red holder and its animations have to survive it. ResetToDefault
    /// then clears those on the Take press.
    /// </summary>
    private void RestoreBoardForBaseGame()
    {
        foreach (var cell in AllCells())
        {
            cell.ResetCell();
        }

        heldCells.Clear();

        if (cellLayerRoot != null) cellLayerRoot.SetActive(false);
        if (fullScreenIntro != null) fullScreenIntro.SetActive(false);

        RestoreBoardDressing();

        if (slotView != null)
        {
            slotView.SetColumnReelsVisible(true);

            // Rebuilt to match the board coming back, NOT cleared. Those reels still hold the
            // triggering spin's Orbs, and an Orb without its prize on it is not something this
            // game ever shows.
            slotView.ApplyOrbLayer(triggerOrbPrizes);
        }
    }

    private void ShowWinnerBlue(double roundWin)
    {
        if (winnerRed != null) winnerRed.SetActive(false);
        if (winnerGraphic != null) winnerGraphic.SetActive(false);

        if (winnerBlue != null) winnerBlue.SetActive(true);
        if (winnerBlueText != null) winnerBlueText.text = roundWin.ToString(SpriteTextFormatter.MoneyFormat);

        if (winnerBlueAnim != null)
        {
            winnerBlueAnim.doLoopAnimation = true;
            winnerBlueAnim.onLoopComplete = null;
            winnerBlueAnim.StartAnimation();
        }
    }

    private void ShowWinnerRed()
    {
        if (winnerBlueAnim != null) winnerBlueAnim.StopAnimation();
        if (winnerBlue != null) winnerBlue.SetActive(false);

        if (winnerRed != null) winnerRed.SetActive(true);
        if (winnerRedText != null) winnerRedText.text = 0d.ToString(SpriteTextFormatter.MoneyFormat);

        if (winnerGraphic != null) winnerGraphic.SetActive(true);
        if (winnerGraphicAnim != null)
        {
            winnerGraphicAnim.doLoopAnimation = true;
            winnerGraphicAnim.onLoopComplete = null;
            winnerGraphicAnim.StartAnimation();
        }

        StartRedEffectAnimations();
        StartRedPulse();
    }

    // The side effects run their own clips. Found in the group's children rather than wired, so the
    // number of them is a scene decision rather than something the code has to be told.
    private void StartRedEffectAnimations()
    {
        if (winnerRedEffectsGroup == null) return;

        if (redEffectAnims == null)
        {
            redEffectAnims = winnerRedEffectsGroup.GetComponentsInChildren<ImageAnimation>(true);
        }

        foreach (var anim in redEffectAnims)
        {
            if (anim == null) continue;
            anim.doLoopAnimation = true;
            anim.onLoopComplete = null;
            anim.StartAnimation();
        }
    }

    /// <summary>
    /// Cross-fades the side effects against the "Win" graphics — when one is at full alpha the
    /// other is at zero.
    ///
    /// Driven by a SINGLE tween writing both alphas from one value, rather than two yoyo tweens
    /// started opposite. Two tweens look correct for a few seconds and then drift apart, and the
    /// drift is gradual enough to be miserable to diagnose. One driver cannot go out of phase.
    /// </summary>
    private void StartRedPulse()
    {
        StopRedPulse();
        if (winnerRedEffectsGroup == null && winnerRedWinGroup == null) return;

        if (winnerRedEffectsGroup != null) winnerRedEffectsGroup.alpha = 1f;
        if (winnerRedWinGroup != null) winnerRedWinGroup.alpha = 0f;

        float driver = 1f;
        redPulseTween = DOTween.To(
                () => driver,
                value =>
                {
                    driver = value;
                    if (winnerRedEffectsGroup != null) winnerRedEffectsGroup.alpha = value;
                    if (winnerRedWinGroup != null) winnerRedWinGroup.alpha = 1f - value;
                },
                0f,
                redPulseDuration)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo);
    }

    private void StopRedPulse()
    {
        if (redPulseTween != null)
        {
            redPulseTween.Kill();
            redPulseTween = null;
        }
    }

    // Both holders' clips loop indefinitely once started, so they have to be stopped explicitly —
    // nothing else ends them, and a live clip would keep ticking over a hidden object.
    private void StopPayoutAnimations()
    {
        if (winnerBlueAnim != null) winnerBlueAnim.StopAnimation();
        if (winnerGraphicAnim != null) winnerGraphicAnim.StopAnimation();

        if (redEffectAnims == null) return;
        foreach (var anim in redEffectAnims)
        {
            if (anim != null) anim.StopAnimation();
        }
    }

    #endregion

    #region Board dressing

    // The SlotShed is deliberately not sprite-swapped: the same object stays, at the same size and
    // position, and only its Image colour changes. The background IS swapped.
    private void ApplyBoardDressing()
    {
        CaptureBoardDressing();

        if (slotShedImage != null) slotShedImage.color = featureSlotShedColour;
        if (backgroundImage != null && featureBackgroundSprite != null) backgroundImage.sprite = featureBackgroundSprite;
        if (kingAnimation != null) kingAnimation.SetActive(false);
    }

    private void RestoreBoardDressing()
    {
        if (dressingCaptured)
        {
            if (slotShedImage != null) slotShedImage.color = slotShedOriginalColour;
            if (backgroundImage != null) backgroundImage.sprite = backgroundOriginalSprite;
        }

        if (kingAnimation != null) kingAnimation.SetActive(true);
    }

    // Captured on first use rather than in Awake, so it records what the scene actually looks like
    // at the moment the feature starts rather than whatever it happened to be at load.
    private void CaptureBoardDressing()
    {
        if (dressingCaptured) return;

        if (slotShedImage != null) slotShedOriginalColour = slotShedImage.color;
        if (backgroundImage != null) backgroundOriginalSprite = backgroundImage.sprite;
        dressingCaptured = true;
    }

    // Desaturated and dimmed. A colour rather than a sprite so the shed keeps its exact geometry.
    private static readonly Color featureSlotShedColour = new Color(0.55f, 0.55f, 0.6f, 1f);

    #endregion

    #region Prompt pulse

    /// <summary>
    /// The state between the trigger sequence ending and Start being pressed: the counter panel is
    /// up, but showing only the pulsing prompt.
    ///
    /// The panel has to be raised here rather than at Start, because the prompt sits inside it —
    /// leaving the panel off would leave the prompt off with it, however active the prompt's own
    /// object is.
    /// </summary>
    private void ShowAwardPrompt()
    {
        if (counterPanel != null) counterPanel.SetActive(true);

        SetRoundCountersVisible(false);

        if (pressStartFeature != null) pressStartFeature.SetActive(true);
        StartPromptPulse();
    }

    // Everything that belongs to a running round rather than the prompt. Off while the player is
    // being asked to press Start, on for the rest of the round.
    private void SetRoundCountersVisible(bool visible)
    {
        if (orbCountText != null) orbCountText.gameObject.SetActive(visible);
        if (total15WinGraphic != null) total15WinGraphic.SetActive(visible);
        if (spinsRemainingImage != null) spinsRemainingImage.gameObject.SetActive(visible);
        if (spinsRemainingCount != null) spinsRemainingCount.gameObject.SetActive(visible);
    }

    private void StartPromptPulse()
    {
        if (pressStartFeatureGroup == null) return;

        StopPromptPulse();
        pressStartFeatureGroup.alpha = 1f;
        promptPulseTween = pressStartFeatureGroup
            .DOFade(promptPulseAlpha, promptPulseDuration)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo);
    }

    private void StopPromptPulse()
    {
        if (promptPulseTween != null)
        {
            promptPulseTween.Kill();
            promptPulseTween = null;
        }

        if (pressStartFeatureGroup != null) pressStartFeatureGroup.alpha = 1f;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Writes both counters.
    ///
    /// Call this ONLY at landing moments — the round's start, an Orb landing, and the end of a
    /// spin. It must never be driven off the response arriving, which happens while the cells are
    /// still scrolling: `spinsRemaining` is already reset to 3 by then on any spin that lands an
    /// Orb, so an early write would flip the counter to 3 before the Orb appeared and telegraph it
    /// every single time. Updating on landings instead puts the reset on the same frame as the Orb
    /// that caused it.
    /// </summary>
    private void UpdateCounters(int spinsRemaining)
    {
        // Values only — what is visible is owned by SetRoundCountersVisible, so this cannot
        // accidentally raise a counter during the award prompt.
        if (orbCountText != null) orbCountText.text = heldCells.Count.ToString();
        if (spinsRemainingCount != null) spinsRemainingCount.text = Mathf.Max(0, spinsRemaining).ToString();
    }

    // Plays a one-shot ImageAnimation and waits it out. Falls back to a fixed beat when the object
    // carries no animation, so an unwired intro still leaves a gap rather than snapping through.
    private IEnumerator WaitForImageAnimation(GameObject target)
    {
        ImageAnimation anim = target.GetComponent<ImageAnimation>();
        if (anim == null || anim.textureArray == null || anim.textureArray.Count == 0)
        {
            yield return new WaitForSeconds(overlayFadeDuration);
            yield break;
        }

        bool done = false;
        anim.doLoopAnimation = false;
        anim.onLoopComplete = _ => done = true;
        anim.StartAnimation();

        float timeout = Time.time + 5f;
        while (!done && Time.time < timeout) yield return null;

        anim.onLoopComplete = null;
        anim.StopAnimation();
    }

    private int ReelCount => slotView != null ? slotView.ReelCount : 5;
    private int RowCount => slotView != null ? slotView.RowCount : 3;

    private void SetGroupAlpha(CanvasGroup group, float alpha, bool active)
    {
        if (group == null) return;

        group.DOKill();
        group.alpha = alpha;
        group.gameObject.SetActive(active);
    }

    private void StopActiveSequence()
    {
        if (activeSequence != null)
        {
            StopCoroutine(activeSequence);
            activeSequence = null;
        }
    }

    // The scene is built after this script, so missing references are expected for a while. Warn
    // once, then let the round run without its presentation rather than stalling the game loop.
    private bool HasRequiredRefs()
    {
        if (cellColumns != null && cellColumns.Count > 0 && slotView != null) return true;

        if (!missingRefsLogged)
        {
            missingRefsLogged = true;
            Debug.LogWarning("[HoldAndSpinView] Cell grid or SlotView is not wired — Hold & Spin will run without its presentation.");
        }
        return false;
    }

    #endregion
}
