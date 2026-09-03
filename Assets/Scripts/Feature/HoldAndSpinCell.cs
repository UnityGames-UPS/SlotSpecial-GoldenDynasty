using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// One of the fifteen independent reels that make up the Hold & Spin board.
///
/// This owns MOTION AND LOCK STATE ONLY. It has no idea what an Orb is, what a prize is worth, or
/// which cells the server held — HoldAndSpinView writes content through <see cref="StripImages"/>
/// and this scrolls it. Keeping the split that way is what stops fifteen copies of the feature's
/// rules existing.
///
/// Deliberately NOT modelled on SlotView's column reel. That one has a 16-icon scroll buffer behind
/// a display block, an overshoot and a settle tween, because a column has to land three rows at
/// once and sell the weight of it. A cell lands one symbol: the strip loops down exactly one cell
/// height and stopping is a kill and a snap. Fifteen of these are cheap precisely because none of
/// that machinery is here.
///
/// Every method early-returns while locked, so a held cell refuses the call rather than relying on
/// the caller to remember to skip it.
/// </summary>
// One reel column's worth of Hold & Spin cells — 3 rows, top first.
//
// Column-major on purpose, matching AnimSlotColumn, MysterySlotColumn and OrbSlotColumn. The cell
// hierarchy is built as five column objects of three children each, so this is the order things
// get dragged in. A flat row-major array read the natural drag order as the wrong cells and swapped
// held Orbs with spinning ones, which is invisible until a round actually runs.
[System.Serializable]
public class HoldAndSpinCellColumn
{
    public List<HoldAndSpinCell> rows = new List<HoldAndSpinCell>(3);   // index 0 = top row
}

public class HoldAndSpinCell : MonoBehaviour
{
    [Header("Strip")]
    [Tooltip("The strip that scrolls. Tweened down one cellHeight on a loop; snapped back to zero when the cell stops.")]
    [SerializeField] private RectTransform spinStrip;

    [Tooltip("Symbols on the strip, top first. Index 0 is the cell that shows once the strip snaps back, so that is where the landed symbol is written.")]
    [SerializeField] private Image[] stripSymbols;

    // Un-serialized on purpose, matching SlotView's sizing and FreeGameView's timings: the scene
    // would otherwise hold the authority on these and silently override any change made here.

    // MUST equal the vertical spacing between the strip's symbols in the scene. The strip travels
    // exactly this far and then restarts, so if the two disagree no symbol ever lands centred in
    // the window and the scroll judders.
    //
    // Wider than SlotView's 175 pitch on purpose: at 175 the oversized symbols (Warriors and Drum
    // at 262.5) bleed into their neighbours' slots, which the base game hides behind a board-wide
    // mask but a single-cell window does not.
    private const float CellHeight = 200f;

    // Pixels per second, NOT a duration. The strip's travel depends on how many symbols are on it,
    // so a fixed duration would make a longer strip spin proportionally faster. Expressed as a rate,
    // adding symbols in the scene increases the variety without touching the speed.
    //
    // Matched to SlotView's spinSpeed on purpose. The loop restarts by snapping the strip back to
    // zero, which cuts from the last symbol to the first — the base reels do exactly the same and
    // it is invisible there only because 6000 px/s puts a symbol on screen for 0.03s, far below
    // what the eye resolves. At a third of that speed every symbol reads individually and the cut
    // reads with them.
    private const float ScrollSpeed = 6000f;

    // How long the landed symbol takes to slide into place. Deliberately no ease: the base reels
    // settle with an OutBack overshoot, this one just arrives.
    private const float LandDuration = 0.15f;

    private bool isLocked;
    private Tween spinTween;

    /// <summary>Held cells are locked. Nothing moves them until the round is reset.</summary>
    internal bool IsLocked => isLocked;

    /// <summary>
    /// The strip's images, for the view to write symbols into. Index 0 is the landing cell.
    /// </summary>
    internal Image[] StripImages => stripSymbols;

    /// <summary>
    /// Finds this cell's own children so fifteen cells do not have to be wired by hand — that would
    /// be roughly ninety Inspector drags. Anything already assigned in the Inspector wins, so a
    /// hand-authored cell that does not match the expected hierarchy still works.
    /// </summary>
    internal void SetupFromHierarchy()
    {
        if (spinStrip == null)
        {
            // The strip is the child that holds the symbols, not this cell's own rect — masking
            // happens on the cell, movement happens on the strip inside it.
            if (transform.childCount > 0) spinStrip = transform.GetChild(0) as RectTransform;
        }

        if (stripSymbols == null || stripSymbols.Length == 0)
        {
            if (spinStrip != null)
            {
                var found = new List<Image>();
                for (int i = 0; i < spinStrip.childCount; i++)
                {
                    Image image = spinStrip.GetChild(i).GetComponent<Image>();
                    if (image != null) found.Add(image);
                }
                stripSymbols = found.ToArray();
            }
        }
    }

    /// <summary>
    /// Starts scrolling. One cell height, linear, looping forever — the strip's contents are what
    /// make it read as a reel, so the motion itself never needs to vary.
    /// </summary>
    internal void StartSpin()
    {
        if (isLocked || spinStrip == null) return;

        spinTween?.Kill();
        spinStrip.anchoredPosition = Vector2.zero;

        // Travel the WHOLE strip, not one cell. Moving a single CellHeight only ever brought the
        // second symbol into the window — everything past it sat off-screen for the entire spin,
        // so a four-symbol strip read as two images flickering.
        //
        // The last symbol is the far end of the travel rather than one step beyond it, hence the
        // -1: with symbols at 0, 200, 400, 600 the window has to reach 600, not 800.
        float travel = StripTravel();
        if (travel <= 0f) return;

        spinTween = spinStrip
            .DOAnchorPosY(-travel, travel / ScrollSpeed)
            .SetEase(Ease.Linear)
            .SetLoops(-1, LoopType.Restart);
    }

    private float StripTravel()
    {
        int count = stripSymbols != null ? stripSymbols.Length : 0;
        return count > 1 ? (count - 1) * CellHeight : 0f;
    }

    /// <summary>
    /// Stops dead on whatever is currently in <see cref="StripImages"/>[0].
    ///
    /// The view writes the landed symbol immediately before calling this, in the same frame as the
    /// snap, so the write is never on screen mid-scroll — the same trick StopSingleReel uses on the
    /// column reels.
    /// </summary>
    internal void Stop()
    {
        if (isLocked) return;

        spinTween?.Kill();
        spinTween = null;
        if (spinStrip == null) return;

        // Drop in from one cell above rather than cutting. The caller has already written the
        // landed symbol into StripImages[0], so putting the strip one pitch high puts that symbol
        // just out of frame and the slide brings it down into the window.
        //
        // Snapping straight to zero made the symbol appear rather than arrive, which read as the
        // image changing at the moment of landing.
        spinStrip.anchoredPosition = new Vector2(spinStrip.anchoredPosition.x, CellHeight);

        spinTween = spinStrip
            .DOAnchorPosY(0f, LandDuration)
            .SetEase(Ease.Linear)
            .OnComplete(() => spinTween = null);
    }

    /// <summary>
    /// Holds this cell for the rest of the round. The Orb itself is drawn by SlotView's Orb layer
    /// above, which is why nothing here touches sprites — this cell simply stops existing as a reel
    /// until the round is over.
    /// </summary>
    internal void Freeze()
    {
        isLocked = true;
        spinTween?.Kill();
        spinTween = null;
        if (spinStrip != null) spinStrip.anchoredPosition = Vector2.zero;
    }

    /// <summary>Full teardown back to an unlocked, motionless cell. Safe to call at any time.</summary>
    internal void ResetCell()
    {
        isLocked = false;
        spinTween?.Kill();
        spinTween = null;
        if (spinStrip != null) spinStrip.anchoredPosition = Vector2.zero;
    }

    private void OnDestroy()
    {
        spinTween?.Kill();
    }
}
