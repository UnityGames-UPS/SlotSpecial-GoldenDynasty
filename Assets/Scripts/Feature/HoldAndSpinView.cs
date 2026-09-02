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

    [Tooltip("The fifteen cells, indexed row * 5 + col. Element 0 = row 0 / col 0, element 14 = row 2 / col 4 — the same flat index space WinLine.positions and mysteryPositions use.")]
    [SerializeField] private HoldAndSpinCell[] cells = new HoldAndSpinCell[15];

    [Header("Trigger Presentation")]
    [Tooltip("Full-screen animation played once the triggering Orbs have held. Deactivated again when it finishes.")]
    [SerializeField] private GameObject fullScreenIntro;

    [Tooltip("The \"PRESS START FEATURE BUTTON\" graphic. Its own object, NOT the one Free Games uses.")]
    [SerializeField] private GameObject pressStartFeature;
    [SerializeField] private CanvasGroup pressStartFeatureGroup;

    [Header("Counters")]
    [Tooltip("Parent holding both counters during the round. Hidden for the payout.")]
    [SerializeField] private GameObject counterPanel;
    [Tooltip("Orb count. Climbs by one the moment each Orb lands, not once per full stop.")]
    [SerializeField] private TMPro.TMP_Text orbCountText;
    [Tooltip("The respins-remaining graphic. A single static sprite — the number itself goes in spinsRemainingCount below.")]
    [SerializeField] private Image spinsRemainingImage;
    [Tooltip("Respins left in the round. Counts 3 down to 1 and snaps back to 3 the moment an Orb lands.")]
    [SerializeField] private TMPro.TMP_Text spinsRemainingCount;

    [Header("Payout")]
    [Tooltip("The \"Winner\" graphic shown once the round ends.")]
    [SerializeField] private GameObject winnerPanel;
    [SerializeField] private CanvasGroup winnerPanelGroup;
    [Tooltip("The total-win figure counted up inside the Winner panel.")]
    [SerializeField] private TMPro.TMP_Text winnerTotalText;

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
    [Tooltip("Dark overlay behind the Winner panel.")]
    [SerializeField] private CanvasGroup darkOverlayGroup;

    // Deliberately NOT [SerializeField], matching FreeGameView and SlotView: serialized values are
    // saved in the scene and silently override anything changed here, which makes retuning in code
    // look broken. Code is the single source of truth; the trade is a recompile.
    private const float triggerOrbHold = 1.0f;          // Orbs sit before anything else happens
    private const float promptPulseAlpha = 0.25f;
    private const float promptPulseDuration = 0.7f;
    private const float cellStopStagger = 0.08f;
    private const float overlayFadeDuration = 0.5f;
    private const float darkOverlayAlpha = 0.75f;
    private const float payoutHoldBeforeCountUp = 0.4f;
    private const float payoutCountUpDuration = 2.0f;

    private Coroutine activeSequence;
    private Tween promptPulseTween;
    private Action pendingTakeCallback;

    private Color slotShedOriginalColour;
    private Sprite backgroundOriginalSprite;
    private bool dressingCaptured;
    private bool missingRefsLogged;

    // Cells holding an Orb, as flat indices. This is the client's entire record of what is held —
    // it is diffed against orbPrizes each spin to find the new Orbs, which is why none of
    // orbCount, newOrbCount or heldPositions is ever read. See HoldAndSpin.md section 8.
    private readonly HashSet<int> heldCells = new HashSet<int>();

    private void Awake()
    {
        if (cells == null) return;

        foreach (var cell in cells)
        {
            if (cell != null) cell.SetupFromHierarchy();
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
        if (cells == null) return;

        List<int> fillerIds = slotView != null ? slotView.GetHoldAndSpinFillerIds() : null;

        foreach (var cell in cells)
        {
            if (cell == null || cell.IsLocked) continue;

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

        heldCells.Clear();

        if (cells != null)
        {
            foreach (var cell in cells)
            {
                if (cell != null) cell.ResetCell();
            }
        }

        if (cellLayerRoot != null) cellLayerRoot.SetActive(false);
        if (fullScreenIntro != null) fullScreenIntro.SetActive(false);
        if (pressStartFeature != null) pressStartFeature.SetActive(false);
        if (counterPanel != null) counterPanel.SetActive(false);
        if (winnerPanel != null) winnerPanel.SetActive(false);

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
        heldCells.Clear();
        if (orbPrizes != null)
        {
            foreach (int flatIndex in orbPrizes.Keys) heldCells.Add(flatIndex);
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

        // 5. Prompt up, payout values out.
        if (pressStartFeature != null) pressStartFeature.SetActive(true);
        StartPromptPulse();

        if (topGroup != null) topGroup.DOFade(0f, overlayFadeDuration);

        activeSequence = null;
        onComplete?.Invoke();
    }

    private void PrepareCellLayer()
    {
        if (slotView != null) slotView.SetColumnReelsVisible(false);
        if (cellLayerRoot != null) cellLayerRoot.SetActive(true);

        if (cells == null) return;

        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] == null) continue;

            cells[i].ResetCell();
            if (heldCells.Contains(i)) cells[i].Freeze();
        }
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
                HoldAndSpinCell cell = (cells != null && flatIndex < cells.Length) ? cells[flatIndex] : null;

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

        // 2. Dark overlay behind the Winner panel.
        if (darkOverlayGroup != null)
        {
            darkOverlayGroup.gameObject.SetActive(true);
            darkOverlayGroup.DOFade(darkOverlayAlpha, overlayFadeDuration);
        }

        if (winnerPanel != null) winnerPanel.SetActive(true);
        SetGroupAlpha(winnerPanelGroup, 1f, true);
        if (winnerTotalText != null) winnerTotalText.text = 0d.ToString(SpriteTextFormatter.MoneyFormat);

        yield return new WaitForSeconds(payoutHoldBeforeCountUp);

        // 3. Straight count-up to the round total.
        if (winnerTotalText != null && roundWin > 0)
        {
            double shown = 0;
            yield return DOTween.To(
                    () => (float)shown,
                    value =>
                    {
                        shown = value;
                        winnerTotalText.text = value.ToString(SpriteTextFormatter.MoneyFormat);
                    },
                    (float)roundWin,
                    payoutCountUpDuration)
                .SetEase(Ease.OutQuad)
                .WaitForCompletion();
        }

        if (winnerTotalText != null) winnerTotalText.text = roundWin.ToString(SpriteTextFormatter.MoneyFormat);

        // 4. Take becomes pressable. GameManager owns the button; OnTakePressed carries on.
        onCountUpComplete?.Invoke();

        activeSequence = null;
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
        if (orbCountText != null) orbCountText.text = heldCells.Count.ToString();

        // The graphic is a static sprite; only the number beside it changes.
        if (spinsRemainingImage != null) spinsRemainingImage.gameObject.SetActive(true);
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
        if (cells != null && cells.Length > 0 && slotView != null) return true;

        if (!missingRefsLogged)
        {
            missingRefsLogged = true;
            Debug.LogWarning("[HoldAndSpinView] Cell grid or SlotView is not wired — Hold & Spin will run without its presentation.");
        }
        return false;
    }

    #endregion
}
