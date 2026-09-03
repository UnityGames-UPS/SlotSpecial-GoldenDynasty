using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

public class SlotView : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameManager gameManager;

    // Number of distinct symbols the backend can send (ids 0..SymbolCount-1). Every array indexed
    // by symbol id is sized from this, so the count lives in exactly one place.
    private const int SymbolCount = 13;

    [Header("Symbol Sprites - Assign by Name")]
    // Field names match the backend's symbol "name" exactly, so the two can be checked against
    // each other at a glance. Where the backend also sends a friendlier "displayName", it's noted.
    [SerializeField] private Sprite spriteWild;               // ID: 0  (wild)
    [SerializeField] private Sprite spriteScatter;            // ID: 1  (scatter — triggers Free Games)
    [SerializeField] private Sprite spriteOrb;                // ID: 2  (triggers Hold & Spin)
    [SerializeField] private Sprite spriteMystery;            // ID: 3  (Free Games only)
    [SerializeField] private Sprite spriteWarriors;           // ID: 4  (high — top paytable)
    [SerializeField] private Sprite spriteLady;               // ID: 5  (high)
    [SerializeField] private Sprite spriteBook;               // ID: 6  (high)
    [SerializeField] private Sprite spriteDrum;               // ID: 7  (mid)
    [SerializeField] private Sprite spriteA;                  // ID: 8  (low — "Ace")
    [SerializeField] private Sprite spriteK;                  // ID: 9  (low — "King")
    [SerializeField] private Sprite spriteQ;                  // ID: 10 (low — "Queen")
    [SerializeField] private Sprite spriteJ;                  // ID: 11 (low — "Jack")
    [SerializeField] private Sprite sprite10;                 // ID: 12 (low — "Ten")

    // Deliberately NOT part of the id-keyed table above and NOT in BuildSymbolSpriteArray: this has
    // no symbol id, the server can never send it, and it is never a spin result. It is the empty
    // cell backing, used only where something has to occupy a slot without being a symbol — today
    // that is Hold & Spin's held cells, which sit behind the Orb layer and must not show a symbol
    // of their own through its transparent corners.
    [Tooltip("The \"Empty\" sprite — an empty cell, not a symbol. Drawn behind held Orbs during Hold & Spin.")]
    [SerializeField] private Sprite spriteEmpty;

    // Rect size for symbols whose art is drawn at 1.5x against the 175 pitch.
    private static readonly Vector2 LargeSymbolSize = new Vector2(262.5f, 262.5f);

    // Symbols that need a rect size other than normalSymbolSize, each with its own. Was a flat set
    // of "large" ids against a single size, until Wild and Lady each turned out to want something
    // between 175 and 262.5 — sizing is per-symbol art, not a two-tier property.
    //
    // Kept next to the sprite fields on purpose: both are id-keyed maps of the same symbol table,
    // so if the backend ever reorders it again they have to be corrected together — and the sprite
    // mapping fails loudly (every symbol showing the wrong art) the moment that happens.
    private static readonly Dictionary<int, Vector2> SymbolSizeOverrides = new Dictionary<int, Vector2>
    {
        { 0, new Vector2(200f, 200f) },  // Wild
        { 4, LargeSymbolSize },          // Warriors
        { 5, new Vector2(210f, 210f) },  // Lady
        { 7, LargeSymbolSize }           // Drum
    };

    // Playback speed per symbol, applied wherever that symbol's clip is assigned.
    //
    // NOT frames per second. ImageAnimation derives its frame delay as
    // (1/24) * frameCount / AnimationSpeed, so the same value plays a long clip more slowly than a
    // short one — which is why each symbol needs its own, tuned by eye against its own art.
    //
    // Every symbol gets a value on every write. The win-layer components are reused from spin to
    // spin, so leaving one untouched would silently inherit whatever the previous symbol had set
    // on that slot.
    //
    // All 13 are listed explicitly, so the fallback below is only reached if the backend ever sends
    // an id this table doesn't know about.
    private const float DefaultSymbolAnimationSpeed = 20f;
    //Animation Speeds
    private static readonly Dictionary<int, float> SymbolAnimationSpeeds = new Dictionary<int, float>
    {
        { 0,  25f },  // Wild
        { 1,  20f },  // Scatter
        { 2,  20f },  // Orb
        { 3,  20f },  // Mystery
        { 4,  25f },  // Warriors
        { 5,  25f },  // Lady
        { 6,  20f },  // Book
        { 7,  50f },  // Drum
        { 8,  15f },  // A
        { 9,  15f },  // K
        { 10, 15f },  // Q
        { 11, 15f },  // J
        { 12, 15f }   // 10
    };

    // Internal array built from named sprites
    private Sprite[] symbolSprites;

    [Header("Win Animation Sprite Arrays")]
    [Tooltip("Optional per-symbol win-animation frame sequences. Leave any empty until real art exists — animation playback already no-ops safely on an empty list.")]
    [SerializeField] private List<Sprite> animSpritesWild;           // ID: 0
    [SerializeField] private List<Sprite> animSpritesScatter;        // ID: 1
    [SerializeField] private List<Sprite> animSpritesOrb;            // ID: 2
    [SerializeField] private List<Sprite> animSpritesMystery;        // ID: 3
    [SerializeField] private List<Sprite> animSpritesWarriors;       // ID: 4
    [SerializeField] private List<Sprite> animSpritesLady;           // ID: 5
    [SerializeField] private List<Sprite> animSpritesBook;           // ID: 6
    [SerializeField] private List<Sprite> animSpritesDrum;           // ID: 7
    [SerializeField] private List<Sprite> animSpritesA;              // ID: 8
    [SerializeField] private List<Sprite> animSpritesK;              // ID: 9
    [SerializeField] private List<Sprite> animSpritesQ;              // ID: 10
    [SerializeField] private List<Sprite> animSpritesJ;              // ID: 11
    [SerializeField] private List<Sprite> animSprites10;             // ID: 12

    // Internal array of animation sprite lists
    private List<Sprite>[] animationSpriteArrays;

    [Header("Reel Containers")]
    [SerializeField] private Transform[] reelTransforms;

    [Header("Reel Images")]
    [SerializeField] private List<ReelImages> reelImagesList;

    // ── Symbol sizing / reel pitch ──────────────────────────────────────────────────────────────
    // Deliberately NOT [SerializeField], for the same reason FreeGameView's timing constants are
    // not: while these were serialized, the scene's saved values silently won over anything changed
    // here, so retuning in code appeared to do nothing. Code is the single source of truth now. The
    // trade is that they can no longer be nudged in Play mode — each change is a recompile.

    // Rect size used by every symbol not listed in SymbolSizeOverrides.
    private Vector2 normalSymbolSize = new Vector2(175f, 175f);

    // Must match the actual icon pitch in the scene. Drives the spin loop's travel distance, which
    // has to be a whole number of pitches or the loop's wrap-around is visible.
    private float symbolHeight = 175f;

    [Header("Spin Settings")]
    [SerializeField] private float spinSpeed = 6000f;
    [SerializeField] private float reelStartStagger = 0.08f;
    [SerializeField] private float reelStopStagger = 0.12f;

    [Header("Animation Settings - Casino Style")]
    [SerializeField] private float anticipationUpDistance = 20f;
    [SerializeField] private float anticipationUpDuration = 0.12f;

    [Header("Win Animation Settings")]
    [SerializeField] private float winPopDuration = 0.4f;
    [SerializeField] private int winPopRepeat = 3;


    [Header("Stop Animation Settings")]
    // Ported from PinballDoubleGold's SlotBehaviour.StopReelSpin: one continuous tween using
    // DOTween's built-in overshoot-and-settle curve, instead of two separate tweens manually
    // faking the same effect (see git history for the old stopOvershootDistance/
    // stopOvershootDuration/stopSettleDuration fields this replaced).
    [SerializeField] private Ease stopEase = Ease.OutBack;
    [Tooltip("Overshoot strength for stopEase, same role as Pinball's landOvershoot (0.9 there). Sizzling7's icon spacing differs, so this needs its own tuning pass.")]
    [SerializeField] private float stopEaseOvershoot = 0.9f;
    [Tooltip("Fixed duration for the landing tween. Pinball derives its landing duration from distance/reelSpeed instead, but Sizzling7's symbolHeight field doesn't reliably match the real icon spacing (275, hand-placed) right now, so an authored duration is used instead of deriving one — matches how every other stop-timing field in this file already works.")]
    [SerializeField] private float stopDuration = 0.5f;

    [Header("Quick Spin Settings")]
    [SerializeField] private float quickStopStagger = 0.06f;
    [SerializeField] private float quickStopOvershoot = 20f;
    [SerializeField] private float quickStopDuration = 0.2f;

    [Header("Scatter Anticipation")]
    [Tooltip("Effects shown around a reel that could still complete a scatter trigger. Index 0 = reel 2, index 1 = reel 3, index 2 = reel 4, index 3 = reel 5. Reel 1 can never anticipate, since two scatters must already have landed.")]
    [SerializeField] private GameObject[] anticipationEffects = new GameObject[4];
    [Tooltip("Extra time each held reel keeps spinning. Applied per held reel and cumulative, so several holds in one spin add up.")]
    [SerializeField] private float anticipationExtraTime = 2f;
    [Tooltip("Shorter hold used when the player is on Turbo.")]
    [SerializeField] private float anticipationExtraTimeTurbo = 1f;

    [Header("Continuous Spin (Tween) Settings")]
    [Tooltip("Filler image slots prepended above the visible window, giving the continuous spin loop room to travel before it has to wrap.")]
    [SerializeField] private int bufferRowsAbove = 16;
    [SerializeField] private Ease spinLoopEase = Ease.Linear;


    [Header("Win Animation Settings")]
    [SerializeField] private float winAnimationDuration = 3.0f; // Total duration each win symbol animation plays
    [SerializeField] private float winSymbolLoopDuration = 1.5f;
    [SerializeField] private int winSymbolLoopCount = 3;
    [Tooltip("Delay between enabling winBox overlay and starting the ImageAnimation - for sync timing")]
    [SerializeField] private float winLineBoxToAnimationDelay = 0.05f;

    [Header("Win Presentation Layer")]
    [Tooltip("Dark sheet covering the reel area during a win. Snaps on/off, no fade.")]
    [SerializeField] private GameObject winDimOverlay;
    [Tooltip("Root of the layer holding the bright winning symbols, drawn above the dim.")]
    [SerializeField] private GameObject winAnimationLayer;
    [Tooltip("One entry per reel column, each holding the 3 active-row slots top to bottom.")]
    [SerializeField] private List<AnimSlotColumn> animSlotColumns = new List<AnimSlotColumn>(3);
    [Tooltip("The 27 paylines, indexed directly by the server's lineIndex — element 0 is line 0. Shown one at a time during the Phase 2 cycle. Leave a field empty if its art doesn't exist yet; it's skipped with a warning naming the index.")]
    [SerializeField] private WinLineVisual[] winLineVisuals = new WinLineVisual[27];

    [Header("Mystery Reveal Layer")]
    [Tooltip("Root of the layer holding the Mystery symbols during their reveal. Sits ABOVE the win animation layer.")]
    [SerializeField] private GameObject mysteryLayerRoot;
    [Tooltip("One entry per reel column, each holding the 3 row slots top to bottom. Same shape as the win layer — 5 columns of 3.")]
    [SerializeField] private List<AnimSlotColumn> mysterySlotColumns = new List<AnimSlotColumn>(5);
    [Tooltip("Beat between the Mystery layer coming down and the win animations starting. Only spins that had a Mystery pay this. Distinct from winLineBoxToAnimationDelay, which sits INSIDE the win presentation, between raising its layer and starting its clips.")]
    [SerializeField] private float mysteryToWinAnimationDelay = 0.1f;

    [Header("Orb Layer")]
    [Tooltip("Root of the layer that draws Orbs with their prize values. Sits ABOVE the win dim (Orbs stay bright) and BELOW the Mystery layer (a closed door hides the Orb until it opens). Used by the base game and by Hold & Spin — the geometry is identical, so one layer serves both.")]
    [SerializeField] private GameObject orbLayerRoot;
    [Tooltip("One entry per reel column, each holding the 3 row slots top to bottom. Same shape as the win and Mystery layers.")]
    [SerializeField] private List<OrbSlotColumn> orbSlotColumns = new List<OrbSlotColumn>(5);

    [Header("Phase 1 Total Win Presentation")]
    [SerializeField] private TMPro.TMP_Text phase1TotalWinText;

    [Header("Symbol Info Card")]
    [SerializeField] private SymbolInfoCard symbolInfoCard;


    private float middlePosition = 0f;


    private List<Tween> spinTweens = new List<Tween>();
    private List<Tween> winTweens = new List<Tween>();
    private Coroutine winAnimationCoroutine;

    // The lines from the spin that just landed, kept so the controller can start the Phase 2 cycle
    // after the fact — autoplay and free spins skip it while they run, and only the controller knows
    // when the round is actually over.
    private List<WinLine> lastWinLines;

    // Which reels are being held back to tease a scatter trigger this spin. Filled before the reels
    // start stopping; StopSingleReel raises and clears each effect off its own landing events.
    // More than one reel can be held in a single spin — see ComputeAnticipatedReels.
    private readonly HashSet<int> anticipatedReels = new HashSet<int>();

    // True while the win dim is being held up by the Mystery reveal, so the win presentation that
    // follows inherits it instead of dropping and re-raising it (which would flicker).
    private bool dimHeld;

    // Cells that landed as a Mystery this spin, as flat indices. Captured when the reels are told
    // to stop, so the landing write knows to draw a Mystery there instead of the revealed symbol.
    private readonly HashSet<int> mysteryCells = new HashSet<int>();

    // Ids the scroll buffer is allowed to pick from — every symbol except Orb and Mystery. Neither
    // should ever appear unless the backend actually placed it there: an Orb always needs a real
    // prize value attached, and Mystery has no meaning outside a reveal, so seeing either as random
    // filler would be showing something the server never sent. Built once and cached, since
    // gameConfig doesn't change after init and this is read on every buffer icon of every spin.
    private List<int> fillerSymbolIds;


    internal List<List<int>> currentDisplayMatrix;

    private bool isSpinning;

    // Config-driven, not Inspector-array-length-driven: reelTransforms/reelImagesList may still
    // have leftover unused slots from a previous reel count (e.g. CNY's 5 reels), so this must
    // reflect the real backend's reel count, not the serialized array size.
    internal int ReelCount => (gameManager != null && gameManager.gameConfig != null)
        ? gameManager.gameConfig.reelCount
        : (reelTransforms != null ? reelTransforms.Length : 3);

    // Row count (3). Every row the server sends is live and pays — there is no decorative padding
    // in this game, so a row index means the same thing in the server payload, in
    // currentDisplayMatrix, and in each reel's displayImages list. The Sizzling-era
    // totalResponseRowCount / ActiveRowStart pair that translated between those spaces is gone.
    internal int RowCount => (gameManager != null && gameManager.gameConfig != null) ? gameManager.gameConfig.rowCount : 3;

    // The Orb's symbol id, or -1 before init. Read wherever an Orb has to be drawn with no matrix
    // entry to take it from — the Orb layer, the feature's filler pool, and Hold & Spin's held
    // cells, which are frozen rather than landed and so never receive a symbol from a spin.
    internal int OrbSymbolId => (gameManager != null && gameManager.gameConfig != null)
        ? gameManager.gameConfig.orbSymbolId
        : -1;

    #region Initialization

    
    private void Awake()
    {
        BuildSymbolSpriteArray();
        InitializeReels();
    }
    private void Start()
    {
        if (symbolSprites == null || symbolSprites.Length == 0)
        {
            BuildSymbolSpriteArray();
        }
        DisableAllOverlays();
        SetupSymbolButtons();
    }

    private void DisableAllOverlays()
    {
        HidePhase1TotalWinText();
        HideAnticipationEffects();
        HideWinSlots();
        HideMysterySlots();
        HideAllWinLines();
        // Safe to clear here despite Hold & Spin's Orbs needing to survive a whole round: the only
        // callers are Start and StartSpin, and StartSpin drives the column reels, which do not run
        // during a round. The cells spin instead and the Orb layer is left standing.
        ClearOrbLayer();
        // Release rather than Hide: this is the full teardown, so a Mystery reveal's claim on the
        // dim must not survive it.
        ReleaseHeldDim();
        HideWinDim();
        if (symbolInfoCard) symbolInfoCard.HideCard();
    }

    private void HideAnticipationEffects()
    {
        anticipatedReels.Clear();
        if (anticipationEffects == null) return;
        foreach (var effect in anticipationEffects)
        {
            if (effect != null) effect.SetActive(false);
        }
    }

    private void SetupSymbolButtons()
    {
        if (reelImagesList == null) return;
        for (int col = 0; col < reelImagesList.Count; col++)
        {
            var reel = reelImagesList[col];
            if (reel == null || reel.displayImages == null) continue;
            int rowCount = RowCount;
            for (int row = 0; row < rowCount; row++)
            {
                if (row < reel.displayImages.Count && reel.displayImages[row] != null)
                {
                    Image img = reel.displayImages[row];
                    SymbolButtonHandler btnHandler = img.GetComponent<SymbolButtonHandler>();
                    if (btnHandler == null)
                    {
                        btnHandler = img.gameObject.AddComponent<SymbolButtonHandler>();
                    }
                    btnHandler.Init(col, row, this);
                }
            }
        }
    }

    internal void HideSymbolInfoCard()
    {
        if (symbolInfoCard != null) symbolInfoCard.HideCard();
    }

    internal void OnBetChanged()
    {
        if (symbolInfoCard != null && symbolInfoCard.gameObject.activeSelf)
        {
            symbolInfoCard.RefreshCard(gameManager);
        }
    }

    internal void OnSymbolClicked(int col, int row, RectTransform symbolRect)
    {
        if (isSpinning)
        {
            if (symbolInfoCard != null) symbolInfoCard.HideCard();
            return;
        }

        int matrixRow = row;
        if (currentDisplayMatrix == null || col >= currentDisplayMatrix.Count || matrixRow < 0 || matrixRow >= currentDisplayMatrix[col].Count)
        {
            return;
        }

        int symbolId = currentDisplayMatrix[col][matrixRow];

        if (symbolInfoCard != null)
        {
            symbolInfoCard.ShowCard(symbolId, col, row, symbolRect, gameManager);
        }
    }

    private void BuildSymbolSpriteArray()
    {
        // Build the symbol sprite array from named sprite fields
        symbolSprites = new Sprite[SymbolCount];
        symbolSprites[0] = spriteWild;
        symbolSprites[1] = spriteScatter;
        symbolSprites[2] = spriteOrb;
        symbolSprites[3] = spriteMystery;
        symbolSprites[4] = spriteWarriors;
        symbolSprites[5] = spriteLady;
        symbolSprites[6] = spriteBook;
        symbolSprites[7] = spriteDrum;
        symbolSprites[8] = spriteA;
        symbolSprites[9] = spriteK;
        symbolSprites[10] = spriteQ;
        symbolSprites[11] = spriteJ;
        symbolSprites[12] = sprite10;

        // Validate
        for (int i = 0; i < symbolSprites.Length; i++)
        {
            if (symbolSprites[i] == null)
            {
                Debug.LogError($"[SlotView] Symbol sprite at index {i} is not assigned in inspector!");
            }
        }

        // Build the animation sprite arrays (any entry left empty simply won't animate)
        animationSpriteArrays = new List<Sprite>[SymbolCount];
        animationSpriteArrays[0] = animSpritesWild;
        animationSpriteArrays[1] = animSpritesScatter;
        animationSpriteArrays[2] = animSpritesOrb;
        animationSpriteArrays[3] = animSpritesMystery;
        animationSpriteArrays[4] = animSpritesWarriors;
        animationSpriteArrays[5] = animSpritesLady;
        animationSpriteArrays[6] = animSpritesBook;
        animationSpriteArrays[7] = animSpritesDrum;
        animationSpriteArrays[8] = animSpritesA;
        animationSpriteArrays[9] = animSpritesK;
        animationSpriteArrays[10] = animSpritesQ;
        animationSpriteArrays[11] = animSpritesJ;
        animationSpriteArrays[12] = animSprites10;
    }

    private void InitializeReels()
    {
        middlePosition = -67.4f;

        int rowCount = RowCount;

        currentDisplayMatrix = new List<List<int>>();
        for (int col = 0; col < ReelCount; col++)
        {
            var defaultCol = new List<int>();
            for (int r = 0; r < rowCount; r++)
            {
                defaultCol.Add(0);
            }
            currentDisplayMatrix.Add(defaultCol);
        }
    }

    internal void SetInitialMatrix(List<List<int>> matrix)
    {
        if (matrix == null || matrix.Count != ReelCount) return;

        int rowCount = RowCount;

        for (int col = 0; col < ReelCount; col++)
        {
            if (matrix[col].Count != rowCount) return;
        }

        currentDisplayMatrix = matrix;

        for (int col = 0; col < ReelCount; col++)
        {
            SetReelSymbols(col, matrix[col], true);
        }
    }

    #endregion

    #region Symbol Display

    private void SetReelSymbols(int columnIndex, List<int> visibleSymbolIds, bool isInitial = false)
    {
        if (columnIndex >= reelImagesList.Count)
        {
            Debug.LogError($"SetReelSymbols: Invalid column index {columnIndex}, max is {reelImagesList.Count - 1}");
            return;
        }

        int rowCount = RowCount;

        if (visibleSymbolIds == null || visibleSymbolIds.Count != rowCount)
        {
            Debug.LogError($"SetReelSymbols: Invalid visibleSymbolIds count {visibleSymbolIds?.Count}, expected {rowCount}");
            return;
        }

        var reel = reelImagesList[columnIndex];

        if (reel.images == null)
        {
            Debug.LogError($"SetReelSymbols: Reel {columnIndex} has no images assigned");
            return;
        }

        WriteDisplayBlockSprites(columnIndex, visibleSymbolIds);
        RandomizeBufferSprites(columnIndex);

        if (isInitial && reelTransforms[columnIndex] != null)
        {
            reelTransforms[columnIndex].localPosition = new Vector3(
                reelTransforms[columnIndex].localPosition.x,
                middlePosition,
                0
            );
        }
    }

    // Writes only the display-block sprites (no buffer reshuffle, no position touch) — used by
    // SetReelSymbols above and, standalone, by the early result-preload path, which deliberately
    // must not trigger a buffer reshuffle mid-spin.
    private void WriteDisplayBlockSprites(int columnIndex, List<int> visibleSymbolIds)
    {
        if (columnIndex >= reelImagesList.Count) return;

        int rowCount = RowCount;
        if (visibleSymbolIds == null || visibleSymbolIds.Count != rowCount) return;

        var reel = reelImagesList[columnIndex];
        if (reel.displayImages == null) return;

        int mysteryId = (gameManager != null && gameManager.gameConfig != null)
            ? gameManager.gameConfig.mysterySymbolId
            : -1;

        for (int row = 0; row < rowCount; row++)
        {
            if (row < reel.displayImages.Count && reel.displayImages[row] != null)
            {
                int symbolId = visibleSymbolIds[row];

                // A cell that landed as a Mystery shows the Mystery, not what it revealed into.
                // The server's matrix is post-reveal, so without this override the player would see
                // the answer the instant the reel stopped and only then watch it be "revealed".
                // MysteryRevealRoutine writes the real symbol back once the door is covering it.
                if (mysteryId >= 0 && mysteryCells.Contains(row * ReelCount + columnIndex))
                {
                    symbolId = mysteryId;
                }

                ApplySymbol(reel.displayImages[row], symbolId, manageRaycast: true);
            }
        }
    }

    // Falls back rather than throwing on an unlisted id: only symbols being retuned need an entry,
    // and an id with no entry is the normal case, not an error.
    private static float GetSymbolAnimationSpeed(int symbolId)
    {
        return SymbolAnimationSpeeds.TryGetValue(symbolId, out float speed)
            ? speed
            : DefaultSymbolAnimationSpeed;
    }

    private Sprite GetSymbolSprite(int symbolId)
    {
        // Validate symbolId range (0..SymbolCount-1)
        if (symbolId < 0 || symbolId >= symbolSprites.Length)
        {
            Debug.LogWarning($"[SlotView] Invalid symbolId {symbolId}, using default sprite 0. Total sprites: {symbolSprites.Length}");
            return symbolSprites[0];
        }

        if (symbolSprites[symbolId] == null)
        {
            Debug.LogError($"[SlotView] Symbol sprite for ID {symbolId} is null!");
            return symbolSprites[0];
        }

        return symbolSprites[symbolId];
    }

    // Single place that puts a symbol onto an icon. Sprite and size are set together on purpose:
    // the Bonus symbol's art is drawn at a different scale to the rest, so it needs a larger rect.
    // Because every write goes through here and always sets one size or the other, an icon that
    // showed a Bonus is snapped back to normal as soon as it's given any other symbol — no reset
    // pass to maintain and no way for an icon to get stuck oversized.
    private void ApplySymbol(Image image, int symbolId, bool manageRaycast = false)
    {
        if (image == null) return;

        image.sprite = GetSymbolSprite(symbolId);

        // Sizing is art-driven, not role-driven: a few symbols are drawn larger than the pitch and
        // the rest are not, which is why this reads an id-keyed map rather than keying off
        // scatterSymbolId the way it did when exactly one symbol needed its own size.
        //
        // Anything above 175 overlaps its vertical neighbours — 262.5 stands ~44px into each — which
        // is intentional art bleed, but also means those symbols swallow clicks aimed at the ones
        // above and below them.
        image.rectTransform.sizeDelta = SymbolSizeOverrides.TryGetValue(symbolId, out Vector2 size)
            ? size
            : normalSymbolSize;

        // Display icons must catch clicks so the symbol info card can open on them. This is set
        // here rather than left to the scene so it can't be lost by an icon being re-authored.
        //
        // Opt-in rather than unconditional, because the other two callers must not get it: the
        // win-animation layer's slots are authored raycast-off and have to stay that way (they sit
        // above the reels during a win), and the scroll buffer has no info card to open.
        //
        // This used to switch on the blank symbol, which no longer exists — Sizzling 7s spaced its
        // symbols with blanks whose oversized rects straddled two neighbours and swallowed their
        // clicks. Golden Dynasty has no blanks, but note the four 262.5-tall symbols overlap their
        // neighbours the same way, so the same click-stealing is possible from those.
        if (manageRaycast)
        {
            image.raycastTarget = true;
        }
    }

    // Randomizes the pure spin-loop scroll buffer. images now holds only buffer icons (the 5
    // real display-block icons live in displayImages instead), so no start/end boundary math
    // is needed — every entry here is fair game for random filler.
    private void RandomizeBufferSprites(int columnIndex)
    {
        if (columnIndex >= reelImagesList.Count) return;
        var reel = reelImagesList[columnIndex];
        if (reel.images == null) return;

        EnsureFillerSymbolIds();

        for (int i = 0; i < reel.images.Count; i++)
        {
            // Held in a variable so ApplySymbol can size it — the pool spans every non-excluded
            // symbol id, so a special that needs its own rect size resizes as it scrolls past just
            // like a landed one.
            int symbolId = fillerSymbolIds.Count > 0
                ? fillerSymbolIds[Random.Range(0, fillerSymbolIds.Count)]
                : 0;
            ApplySymbol(reel.images[i], symbolId);
        }
    }

    // Builds the filler pool once and reuses it — gameConfig is fixed for the session, and this is
    // read on every buffer icon of every spin.
    private void EnsureFillerSymbolIds()
    {
        if (fillerSymbolIds != null) return;

        int orbSymbolId = OrbSymbolId;
        int mysterySymbolId = (gameManager != null && gameManager.gameConfig != null) ? gameManager.gameConfig.mysterySymbolId : -1;

        fillerSymbolIds = new List<int>(SymbolCount);
        for (int id = 0; id < SymbolCount; id++)
        {
            if (id == orbSymbolId || id == mysterySymbolId) continue;
            fillerSymbolIds.Add(id);
        }
    }

    #endregion

    #region Spin Animation

    internal void StartSpin()
    {
        if (isSpinning) return;

        if (symbolInfoCard != null) symbolInfoCard.HideCard();

        isSpinning = true;
        KillAllTweens();

        DisableAllOverlays();

        for (int col = 0; col < ReelCount; col++)
        {
            RandomizeBufferSprites(col);
            StartReelCycleWithDelay(col, col * reelStartStagger);
        }
    }

    private void StartReelCycleWithDelay(int columnIndex, float delay)
    {
        if (columnIndex >= reelTransforms.Length) return;

        Transform slotTransform = reelTransforms[columnIndex];

        Sequence startSequence = DOTween.Sequence();

        if (delay > 0)
        {
            startSequence.AppendInterval(delay);
        }

        startSequence.Append(
            slotTransform.DOLocalMoveY(middlePosition + anticipationUpDistance, anticipationUpDuration)
                .SetEase(Ease.OutQuad)
        );

        startSequence.Append(
            slotTransform.DOLocalMoveY(middlePosition, anticipationUpDuration * 0.5f)
                .SetEase(Ease.InQuad)
        );

        startSequence.OnComplete(() => {
            if (isSpinning)
            {
                StartContinuousLoop(columnIndex);
            }
        });

        startSequence.Play();

        if (spinTweens.Count <= columnIndex)
            spinTweens.Add(startSequence);
        else
            spinTweens[columnIndex] = startSequence;
    }

    // One continuous loop tween per column, replacing the old "shift one row then snap"
    // illusion. The strip's sprite content is set once at StartSpin() and stays static for the
    // rest of the spin — reshuffling it on every loop wrap was visible as symbols popping/
    // changing mid-scroll, so the buffer is deliberately left untouched here.
    private void StartContinuousLoop(int columnIndex)
    {
        if (columnIndex >= reelTransforms.Length) return;
        if (!isSpinning) return;

        Transform slotTransform = reelTransforms[columnIndex];

        slotTransform.localPosition = new Vector3(slotTransform.localPosition.x, middlePosition, 0);

        float loopDistance = bufferRowsAbove * symbolHeight;
        float loopDuration = loopDistance / spinSpeed;

        Tween loopTween = slotTransform.DOLocalMoveY(middlePosition - loopDistance, loopDuration)
            .SetEase(spinLoopEase)
            .SetLoops(-1, LoopType.Restart);

        loopTween.Play();

        if (spinTweens.Count <= columnIndex)
            spinTweens.Add(loopTween);
        else
            spinTweens[columnIndex] = loopTween;
    }

    #endregion

    #region Stop Spin

    internal void StopSpin(List<List<int>> resultMatrix, System.Action onComplete)
    {
        if (!isSpinning)
        {
            currentDisplayMatrix = resultMatrix;
            // No reveal runs on this path, so no cell should be held back as a Mystery — and a
            // stale set from a previous spin would draw one over an unrelated symbol.
            mysteryCells.Clear();
            for (int col = 0; col < ReelCount; col++)
            {
                SetReelSymbols(col, resultMatrix[col], false);
            }
            // No reel landed, so the per-column draw in StopSingleReel never ran.
            ApplyOrbLayer(gameManager?.lastResult?.holdAndSpin?.orbPrizes);
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(StopSpinSequence(resultMatrix, onComplete, false));
    }

    private IEnumerator StopSpinSequence(List<List<int>> resultMatrix, System.Action onComplete, bool isQuickStop)
    {
        currentDisplayMatrix = resultMatrix;

        // Captured before any reel lands, because the landing write needs it: these cells draw a
        // Mystery rather than the symbol the matrix holds for them.
        mysteryCells.Clear();
        var landedMysteries = gameManager != null && gameManager.lastResult != null
            ? gameManager.lastResult.mysteryPositions
            : null;
        if (landedMysteries != null)
        {
            foreach (int flatIndex in landedMysteries) mysteryCells.Add(flatIndex);
        }

        // GameManager.GetSpinDuration() already enforces the minimum spin time before this is
        // ever called, so there's no need for a separate discrete-cycle-count gate here.
        float stagger = isQuickStop ? quickStopStagger : reelStopStagger;

        // Skipped entirely on a quick stop — that path covers both QuickSpin mode and the player
        // hitting Stop, and neither should sit through the hold. StopSingleReel reads this set to
        // know when to raise and clear each effect.
        anticipatedReels.Clear();
        if (!isQuickStop) ComputeAnticipatedReels(resultMatrix, anticipatedReels);
        float anticipationHold = GetAnticipationHold();

        // Each held reel spins on for anticipationHold, which pushes itself and everything after
        // it back by that much — so the delays are cumulative and the reels still land left to
        // right. Four held reels really do add four holds; that is the intended drama.
        int holdsSoFar = 0;
        for (int col = 0; col < ReelCount; col++)
        {
            if (anticipatedReels.Contains(col)) holdsSoFar++;

            float delay = col * stagger + anticipationHold * holdsSoFar;
            StartCoroutine(StopSingleReel(col, resultMatrix[col], delay, isQuickStop));
        }

        float lastColumnDelay = (ReelCount - 1) * stagger;
        float longestStopTime;
        if (isQuickStop)
        {
            longestStopTime = lastColumnDelay + quickStopDuration;
        }
        else
        {
            longestStopTime = lastColumnDelay + stopDuration;
        }

        // The whole hand-off waits for every hold too, so the win presentation can't start while a
        // reel is still spinning on.
        longestStopTime += anticipationHold * anticipatedReels.Count;

        yield return new WaitForSeconds(longestStopTime);

        isSpinning = false;

        // Cut the spin loop here rather than in the controller's OnReelsStoppedComplete: this is the
        // real moment the last reel lands, and on a quick stop the controller waits another 0.5s for
        // the snap to settle before it runs. Anticipation is already accounted for, since the hold is
        // folded into longestStopTime above — a teased reel keeps the loop running while it spins on.
        AudioManager.Instance?.StopSpinLoop();

        onComplete?.Invoke();
    }

    /// <summary>
    /// Which reels are held back to tease a scatter trigger. Any number of reels can be held in one
    /// spin, and the hold starts as soon as a 2nd scatter is on the board — whether or not a 3rd
    /// ever turns up.
    ///
    /// The whole rule reduces to one condition: <b>a reel is held iff exactly 2 scatters have
    /// landed in the reels before it.</b> Once a 3rd lands the running count passes 2 and the holds
    /// stop by themselves; while only 1 has landed it never starts. That single test also produces
    /// every "no anticipation" case without special-casing any of them:
    /// <list type="bullet">
    /// <item>3 scatters on reel 1 — the count is already 3 by reel 2, so nothing is held.</item>
    /// <item>2nd scatter on the last reel — no reels follow it to hold.</item>
    /// <item>All 3 on the last reel — the count is 0 everywhere before it.</item>
    /// </list>
    /// One known gap, deliberately left (see TODO.md): a single early scatter followed by two on
    /// the last reel gives no build-up, because the count before that reel is only 1.
    /// </summary>
    private void ComputeAnticipatedReels(List<List<int>> resultMatrix, HashSet<int> results)
    {
        if (resultMatrix == null || ReelCount < 2) return;

        int scattersBefore = 0;
        for (int col = 0; col < ReelCount; col++)
        {
            if (scattersBefore == 2) results.Add(col);
            scattersBefore += CountScattersInColumn(resultMatrix, col);
        }
    }

    // Counts scatters in one reel column, bounded to the rows the grid actually shows.
    private int CountScattersInColumn(List<List<int>> matrix, int col)
    {
        if (matrix == null || col < 0 || col >= matrix.Count || matrix[col] == null) return 0;

        int bonusId = gameManager?.gameConfig != null ? gameManager.gameConfig.scatterSymbolId : -1;
        if (bonusId < 0) return 0;

        int rowEnd = Mathf.Min(RowCount, matrix[col].Count);

        int count = 0;
        for (int row = 0; row < rowEnd; row++)
        {
            if (matrix[col][row] == bonusId) count++;
        }
        return count;
    }

    private float GetAnticipationHold()
    {
        bool isTurbo = gameManager != null && gameManager.currentSpinSpeed == SpinSpeed.Turbo;
        return isTurbo ? anticipationExtraTimeTurbo : anticipationExtraTime;
    }

    // effects[0] belongs to reel index 1, effects[1] to reel index 2, and so on — reel 0 can never
    // anticipate, since two scatters have to have landed before it.
    private void SetAnticipationEffect(int reelIndex, bool visible)
    {
        int effectIndex = reelIndex - 1;
        if (anticipationEffects == null || effectIndex < 0 || effectIndex >= anticipationEffects.Length) return;

        GameObject effect = anticipationEffects[effectIndex];
        if (effect != null) effect.SetActive(visible);
    }

    private IEnumerator StopSingleReel(int columnIndex, List<int> targetSymbols, float delay, bool isQuickStop)
    {
        if (delay > 0)
        {
            yield return new WaitForSeconds(delay);
        }

        if (columnIndex < spinTweens.Count && spinTweens[columnIndex] != null)
        {
            spinTweens[columnIndex].Kill();
        }

        Transform slotTransform = reelTransforms[columnIndex];

        SetReelSymbols(columnIndex, targetSymbols, false);

        // Orbs on this reel light up the moment it lands, without waiting for the reels to its
        // right — an Orb always carries a prize and always animates immediately, in the base game
        // as much as in the feature. Drawn per column here for exactly that reason; doing it once
        // at the end of the stop sequence would make every Orb wait for the slowest reel.
        DrawOrbsForColumn(columnIndex);

        // Snap to a fixed pre-land reference point so the overshoot/settle distance below is
        // consistent regardless of where in its continuous loop the reel was stopped.
        slotTransform.localPosition = new Vector3(
            slotTransform.localPosition.x,
            middlePosition + symbolHeight,
            0
        );

        // ── Play reel-stop sound immediately when symbols lock in ──────────
        AudioManager.Instance?.PlayReelStop();

        // Special-symbol landing cues for this column. Both fire at most once per reel, not once
        // per symbol.
        if (currentDisplayMatrix != null && columnIndex < currentDisplayMatrix.Count)
        {
            bool hasWild = false;
            bool hasBonus = false;
            int wildId = gameManager?.gameConfig != null ? gameManager.gameConfig.wildSymbolId : 1;
            int bonusId = gameManager?.gameConfig != null ? gameManager.gameConfig.scatterSymbolId : 0;
            var column = currentDisplayMatrix[columnIndex];
            int rowEnd = Mathf.Min(RowCount, column.Count);

            for (int r = 0; r < rowEnd; r++)
            {
                if (column[r] == wildId) hasWild = true;
                else if (column[r] == bonusId) hasBonus = true;

                if (hasWild && hasBonus) break;
            }

            if (hasWild) AudioManager.Instance?.PlayWildLand();
            if (hasBonus) AudioManager.Instance?.PlayBonusLand();
        }
        // ──────────────────────────────────────────────────────────────────

        // If the next reel is being held, its effect comes in on this reel's landing slam. Driven
        // off the actual event rather than a computed timestamp so it can't drift out of sync with
        // the staggers or the holds — which matters more now that several reels can be held and
        // the delays accumulate.
        if (anticipatedReels.Contains(columnIndex + 1))
        {
            SetAnticipationEffect(columnIndex + 1, true);
        }

        if (isQuickStop)
        {
            Sequence quickStopSequence = DOTween.Sequence();

            quickStopSequence.Append(
                slotTransform.DOLocalMoveY(middlePosition - quickStopOvershoot, quickStopDuration * 0.3f)
                    .SetEase(Ease.OutQuad)
            );

            quickStopSequence.Append(
                slotTransform.DOLocalMoveY(middlePosition, quickStopDuration * 0.7f)
                    .SetEase(Ease.InOutQuad)
            );

            spinTweens[columnIndex] = quickStopSequence;
        }
        else
        {
            // Single continuous tween — ported from Pinball's StopReelSpin, which uses
            // Ease.OutBack's built-in overshoot-and-settle curve instead of two separate tweens.
            Tween stopTween = slotTransform.DOLocalMoveY(middlePosition, stopDuration)
                .SetEase(stopEase, stopEaseOvershoot)
                .OnComplete(() =>
                {
                    // This reel was being teased and has now landed — clear its effect whether or
                    // not the scatter actually turned up. Other reels keep their own holds.
                    if (anticipatedReels.Remove(columnIndex))
                    {
                        SetAnticipationEffect(columnIndex, false);
                    }
                });

            spinTweens[columnIndex] = stopTween;
        }
    }

    #endregion

    #region Quick Spin

    internal void QuickStop(List<List<int>> resultMatrix, System.Action onComplete = null)
    {
        if (!isSpinning)
        {
            currentDisplayMatrix = resultMatrix;
            // Same reasoning as StopSpin's early-out: no reveal on this path, so no Mystery override.
            mysteryCells.Clear();
            for (int col = 0; col < ReelCount; col++)
            {
                if (col < reelTransforms.Length)
                {
                    SetReelSymbols(col, resultMatrix[col], false);
                    reelTransforms[col].localPosition = new Vector3(
                        reelTransforms[col].localPosition.x,
                        middlePosition,
                        0
                    );
                }
            }

            // No reel landed, so the per-column draw in StopSingleReel never ran.
            ApplyOrbLayer(gameManager?.lastResult?.holdAndSpin?.orbPrizes);

            onComplete?.Invoke();
            return;
        }

        StartCoroutine(StopSpinSequence(resultMatrix, onComplete, true));
    }

    #endregion

    #region Stop Symbol Animations

    // loopCount <= 0 means "animate indefinitely" — used by the free-games trigger so the scatters
    // keep playing through the whole intro/pick sequence. They're stopped by the first free spin's
    // StartSpin -> KillAllTweens -> KillWinTweens.
    internal void AnimateAllScatters(int loopCount)
    {
        if (currentDisplayMatrix == null) return;

        // Clear any individual hit animations before starting the collective one
        KillWinTweens();

        int actualScatterId = gameManager?.gameConfig != null ? gameManager.gameConfig.scatterSymbolId : -1;
        if (actualScatterId < 0) return;

        int rowCount = RowCount;

        for (int col = 0; col < ReelCount; col++)
        {
            if (col >= currentDisplayMatrix.Count) continue;
            for (int localRow = 0; localRow < rowCount; localRow++)
            {
                if (localRow >= currentDisplayMatrix[col].Count) continue;

                if (currentDisplayMatrix[col][localRow] == actualScatterId)
                {
                    AnimateSymbolSingleLoop(col, localRow, loopCount);
                }
            }
        }
    }

    private void AnimateSymbolSingleLoop(int column, int row, int loopCount = 1)
    {
        if (column >= reelImagesList.Count) return;

        var reel = reelImagesList[column];
        if (reel.displayImages == null) return;

        int displayIndex = row;
        if (displayIndex >= reel.displayImages.Count) return;

        Image symbolImage = reel.displayImages[displayIndex];
        if (symbolImage == null) return;

        ImageAnimation imageAnim = symbolImage.GetComponent<ImageAnimation>();
        if (imageAnim == null) return;

        int symbolId = currentDisplayMatrix[column][row];
        if (symbolId < 0 || symbolId >= animationSpriteArrays.Length) return;

        List<Sprite> animSprites = animationSpriteArrays[symbolId];
        if (animSprites == null || animSprites.Count == 0) return;

        imageAnim.textureArray = animSprites;

        Sequence seq = DOTween.Sequence();

        seq.AppendCallback(() => {
            // ImageAnimation now lives directly on the SlotIcon root, sharing symbolImage —
            // no separate overlay to activate/fade; just ensure full opacity before playing.
            symbolImage.DOKill();
            Color c = symbolImage.color;
            symbolImage.color = new Color(c.r, c.g, c.b, 1f);

            imageAnim.StartAnimation();
        });

        // loopCount <= 0 means run indefinitely — skip scheduling the stop entirely and let
        // whatever kills winTweens end it. The only live caller (AnimateAllScatters, via the
        // free-games trigger) passes 0, so the timed branch below is currently unexercised; it
        // stays for the method's default of 1 and for any future caller that wants a bounded run.
        if (loopCount > 0)
        {
            seq.AppendInterval(winSymbolLoopDuration * loopCount);

            seq.AppendCallback(() => {
                if (imageAnim != null) imageAnim.StopAnimation(); // reverts to textureArray[0], which equals the resting sprite
            });
        }

        winTweens.Add(seq);
    }

    #endregion

    #region Mystery Reveal

    /// <summary>
    /// Plays the Mystery door-opening reveal, then hands back to the caller.
    ///
    /// The reveal is subtractive rather than additive: the reel icon underneath ALREADY holds the
    /// symbol the Mystery turned into, because the server's matrix is post-reveal. So this draws a
    /// Mystery on the layer above, plays its clip, and then hides that layer — uncovering the real
    /// symbol. No crossfade, and no need to resolve the revealed symbol's name to an id.
    ///
    /// Raises the win dim and holds it up, so the win presentation that follows inherits it rather
    /// than dropping and re-raising it.
    /// </summary>
    /// <param name="positions">Flat cell indices (row * reelCount + col) that landed as Mystery.</param>
    internal void PlayMysteryReveal(List<int> positions, System.Action onComplete)
    {
        if (positions == null || positions.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(MysteryRevealRoutine(positions, onComplete));
    }

    private IEnumerator MysteryRevealRoutine(List<int> positions, System.Action onComplete)
    {
        int mysteryId = (gameManager != null && gameManager.gameConfig != null)
            ? gameManager.gameConfig.mysterySymbolId
            : -1;

        List<Sprite> revealFrames = (mysteryId >= 0 && mysteryId < animationSpriteArrays.Length)
            ? animationSpriteArrays[mysteryId]
            : null;

        var activeAnims = new List<ImageAnimation>();
        int completedCount = 0;
        bool isCompleted = false;
        bool anyShown = false;

        int rowCount = RowCount;

        foreach (int flatIndex in positions)
        {
            int row = flatIndex / ReelCount;
            int col = flatIndex % ReelCount;

            if (col < 0 || col >= ReelCount || row < 0 || row >= rowCount) continue;
            if (mysterySlotColumns == null || col >= mysterySlotColumns.Count) continue;

            var column = mysterySlotColumns[col];
            if (column == null || column.rows == null || row >= column.rows.Count) continue;

            AnimSlot slot = column.rows[row];
            if (slot == null || slot.image == null) continue;

            // Show the Mystery symbol unconditionally, even with no frames to play — otherwise a
            // missing clip would leave the cell already revealed with no reveal beat at all.
            Image slotImage = slot.image;
            slotImage.DOKill();
            ApplySymbol(slotImage, mysteryId);
            slotImage.transform.localScale = Vector3.one;
            Color c = slotImage.color;
            slotImage.color = new Color(c.r, c.g, c.b, 1f);
            slotImage.gameObject.SetActive(true);
            anyShown = true;

            if (revealFrames == null || revealFrames.Count == 0) continue;

            ImageAnimation imageAnim = slot.animation;
            if (imageAnim == null) continue;

            imageAnim.textureArray = revealFrames;
            imageAnim.doLoopAnimation = true;
            imageAnim.AnimationSpeed = GetSymbolAnimationSpeed(mysteryId);

            activeAnims.Add(imageAnim);

            imageAnim.onLoopComplete = (currentLoop) =>
            {
                // One pass only — a door opens once, it doesn't loop.
                if (currentLoop >= 1)
                {
                    imageAnim.onLoopComplete = null;
                    imageAnim.StopAnimation();

                    completedCount++;
                    if (completedCount >= activeAnims.Count)
                    {
                        isCompleted = true;
                    }
                }
            };
        }

        if (!anyShown)
        {
            onComplete?.Invoke();
            yield break;
        }

        if (winDimOverlay != null) winDimOverlay.SetActive(true);
        if (mysteryLayerRoot != null) mysteryLayerRoot.SetActive(true);
        // Held so the win presentation's teardown can't drop the dim between the two beats.
        dimHeld = true;

        // Order inside this frame matters. The layer is now up and every slot is sitting on frame 0
        // — a closed door — so the reel icons underneath can be swapped from Mystery to what they
        // actually revealed without any of it being seen. Do this before StartAnimation: the door
        // opens ONTO the base layer, so the real symbol has to already be there when it does.
        // Nothing renders until the end of the frame, so all of this lands at once.
        WriteRevealedSymbolsUnderMystery(positions);

        foreach (var imageAnim in activeAnims)
        {
            imageAnim.StartAnimation();
        }

        if (activeAnims.Count > 0)
        {
            yield return new WaitUntil(() => isCompleted);
        }
        else
        {
            yield return new WaitForSeconds(winSymbolLoopDuration);
        }

        // The door has finished opening; taking the layer down leaves the revealed symbols standing.
        HideMysterySlots();

        // A beat between the reveal and the win presentation. Only spins that actually had a
        // Mystery pay this — a spin with none returns immediately from PlayMysteryReveal and never
        // reaches here.
        if (mysteryToWinAnimationDelay > 0f)
        {
            yield return new WaitForSeconds(mysteryToWinAnimationDelay);
        }

        onComplete?.Invoke();
    }

    // Puts the real symbols back into the reel icons while the closed doors are covering them.
    // Reads currentDisplayMatrix, which has held the post-reveal symbols all along — only the
    // icons were showing a Mystery, never the data.
    private void WriteRevealedSymbolsUnderMystery(List<int> positions)
    {
        if (positions == null || currentDisplayMatrix == null) return;

        int rowCount = RowCount;

        foreach (int flatIndex in positions)
        {
            int row = flatIndex / ReelCount;
            int col = flatIndex % ReelCount;

            if (col < 0 || col >= ReelCount || row < 0 || row >= rowCount) continue;
            if (col >= currentDisplayMatrix.Count || row >= currentDisplayMatrix[col].Count) continue;
            if (reelImagesList == null || col >= reelImagesList.Count) continue;

            var reel = reelImagesList[col];
            if (reel == null || reel.displayImages == null || row >= reel.displayImages.Count) continue;
            if (reel.displayImages[row] == null) continue;

            ApplySymbol(reel.displayImages[row], currentDisplayMatrix[col][row], manageRaycast: true);
        }

        // The override has served its purpose: any later write this spin should use the real
        // symbols, not put the Mystery back.
        mysteryCells.Clear();
    }

    private void HideMysterySlots()
    {
        if (mysterySlotColumns != null)
        {
            foreach (var column in mysterySlotColumns)
            {
                if (column == null || column.rows == null) continue;
                foreach (var slot in column.rows)
                {
                    if (slot == null) continue;

                    if (slot.animation != null)
                    {
                        slot.animation.onLoopComplete = null;
                        slot.animation.StopAnimation();
                    }

                    if (slot.image != null)
                    {
                        slot.image.DOKill();
                        slot.image.transform.localScale = Vector3.one;
                        slot.image.gameObject.SetActive(false);
                    }
                }
            }
        }

        if (mysteryLayerRoot != null) mysteryLayerRoot.SetActive(false);
    }

    #endregion

    #region Orb Layer

    // Draws every Orb on the board, replacing whatever was there before. This is the BASE GAME
    // path: each spin is a fresh board, so the layer is rebuilt from scratch and an Orb landing
    // where one already was still animates as a new landing.
    //
    // Hold & Spin must NOT use this. Inside a round the layer is additive — see HoldOrb.
    internal void ApplyOrbLayer(Dictionary<int, double> orbPrizes)
    {
        ClearOrbLayer();
        if (orbPrizes == null || orbPrizes.Count == 0) return;

        if (orbLayerRoot != null) orbLayerRoot.SetActive(true);

        foreach (var entry in orbPrizes)
        {
            WriteOrbSlot(entry.Key, entry.Value);
        }
    }

    // The base game's per-reel path, called as each column lands. Reads the spin's prize map the
    // same way the Mystery override reads its positions — straight off the controller's result,
    // rather than threading another argument through the whole stop sequence.
    //
    // Only runs outside a round: during Hold & Spin the column reels never stop, because they never
    // started, and held Orbs are drawn one at a time by the feature view instead.
    private void DrawOrbsForColumn(int columnIndex)
    {
        var orbPrizes = gameManager?.lastResult?.holdAndSpin?.orbPrizes;
        if (orbPrizes == null || orbPrizes.Count == 0) return;

        if (orbLayerRoot != null) orbLayerRoot.SetActive(true);

        int reelCount = ReelCount;
        for (int row = 0; row < RowCount; row++)
        {
            int flatIndex = row * reelCount + columnIndex;
            if (orbPrizes.TryGetValue(flatIndex, out double prize))
            {
                WriteOrbSlot(flatIndex, prize);
            }
        }
    }

    // Draws one Orb and leaves every other slot alone. This is the HOLD & SPIN path: an Orb is
    // written once when it lands and then never touched again until the round is taken, which is
    // what keeps held Orbs from restarting their animations on every respin. An untouched slot
    // cannot restart — the guarantee is structural rather than something to remember.
    internal void HoldOrb(int flatIndex, double prize)
    {
        if (orbLayerRoot != null) orbLayerRoot.SetActive(true);
        WriteOrbSlot(flatIndex, prize);
    }

    internal void ClearOrbLayer()
    {
        if (orbSlotColumns != null)
        {
            foreach (var column in orbSlotColumns)
            {
                if (column?.rows == null) continue;

                foreach (var slot in column.rows)
                {
                    if (slot == null) continue;

                    if (slot.animation != null)
                    {
                        slot.animation.StopAnimation();
                        slot.animation.onLoopComplete = null;
                    }

                    if (slot.prizeText != null) slot.prizeText.gameObject.SetActive(false);

                    if (slot.image != null)
                    {
                        slot.image.DOKill();
                        slot.image.transform.localScale = Vector3.one;
                        slot.image.gameObject.SetActive(false);
                    }
                }
            }
        }

        if (orbLayerRoot != null) orbLayerRoot.SetActive(false);
    }

    // Writes an Orb sprite, its prize and its looping animation into one slot.
    //
    // The prize is shown exactly as the server sent it — already multiplied out to cash. Never
    // divide back to the info page's 250/200/100 tiers: that would be client-side arithmetic on a
    // server-authoritative figure, and the paytable-in-multipliers / display-in-currency split is
    // how the rest of the game already reads.
    private void WriteOrbSlot(int flatIndex, double prize)
    {
        OrbSlot slot = ResolveOrbSlot(flatIndex);
        if (slot?.image == null) return;

        int orbId = OrbSymbolId;
        if (orbId < 0) return;

        Image slotImage = slot.image;
        slotImage.DOKill();
        ApplySymbol(slotImage, orbId);
        slotImage.transform.localScale = Vector3.one;
        Color c = slotImage.color;
        slotImage.color = new Color(c.r, c.g, c.b, 1f);
        slotImage.gameObject.SetActive(true);

        if (slot.prizeText != null)
        {
            slot.prizeText.text = prize.ToString("F2");
            slot.prizeText.gameObject.SetActive(true);
        }

        ImageAnimation imageAnim = slot.animation;
        if (imageAnim == null) return;

        List<Sprite> frames = (animationSpriteArrays != null && orbId < animationSpriteArrays.Length)
            ? animationSpriteArrays[orbId]
            : null;

        if (frames == null || frames.Count == 0) return;

        imageAnim.textureArray = frames;
        imageAnim.doLoopAnimation = true;
        imageAnim.onLoopComplete = null;

        // Written on every call, never assumed. AnimationSpeed is only read inside StartAnimation,
        // and these components are reused across spins, so an unwritten speed is a stale speed
        // inherited from whichever symbol used this slot last.
        imageAnim.AnimationSpeed = GetSymbolAnimationSpeed(orbId);
        imageAnim.StartAnimation();
    }

    private OrbSlot ResolveOrbSlot(int flatIndex)
    {
        if (orbSlotColumns == null || ReelCount <= 0) return null;

        int row = flatIndex / ReelCount;
        int col = flatIndex % ReelCount;

        if (col < 0 || col >= ReelCount || row < 0 || row >= RowCount) return null;
        if (col >= orbSlotColumns.Count) return null;

        var column = orbSlotColumns[col];
        if (column?.rows == null || row >= column.rows.Count) return null;

        return column.rows[row];
    }

    // Hold & Spin replaces the board in place: its 15 cell reels occupy the same positions, so the
    // column reels have to get out of the way. Everything else — SlotShed, Orb layer, backgrounds —
    // stays exactly where it is.
    internal void SetColumnReelsVisible(bool visible)
    {
        if (reelTransforms == null) return;

        for (int i = 0; i < reelTransforms.Length; i++)
        {
            if (reelTransforms[i] != null) reelTransforms[i].gameObject.SetActive(visible);
        }
    }

    // Lets the Hold & Spin cells fill their strips with correctly sized symbols. ApplySymbol is
    // private and does more than assign a sprite — the four oversized symbols need their own rect
    // size — so exposing it beats every caller re-deriving that.
    internal void WriteSymbol(Image image, int symbolId)
    {
        ApplySymbol(image, symbolId);
    }

    // Writes the empty-cell sprite. Not routed through ApplySymbol because that is keyed by symbol
    // id and this has none — it sizes to the normal pitch, which is what an empty cell should be
    // whatever symbol was there before.
    internal void WriteEmptySymbol(Image image)
    {
        if (image == null) return;

        image.sprite = spriteEmpty;
        image.rectTransform.sizeDelta = normalSymbolSize;
    }

    // The ids a Hold & Spin cell may scroll through: the base filler pool plus Orb. Orbs are what
    // the player is spinning for, so seeing them sweep past is part of the tension — unlike the
    // base game, where an Orb in the buffer would be showing a prize-less Orb the server never
    // sent. Mystery stays excluded for that same reason: it has no meaning outside a reveal.
    internal List<int> GetHoldAndSpinFillerIds()
    {
        EnsureFillerSymbolIds();

        var ids = new List<int>(fillerSymbolIds);

        int orbId = OrbSymbolId;
        if (orbId >= 0 && !ids.Contains(orbId)) ids.Add(orbId);

        return ids;
    }

    #endregion

    #region Win Line Animation

    internal void ShowWinLineAnimation(List<WinLine> winLines, System.Action onComplete)
    {
        if (winLines == null || winLines.Count == 0)
        {
            lastWinLines = null;
            // No win presentation is coming to inherit the dim, so a Mystery reveal that just
            // raised it has to let it go here — otherwise the board stays dark until the next spin.
            ReleaseHeldDim();
            onComplete?.Invoke();
            return;
        }

        lastWinLines = winLines;

        KillWinTweens();
        winAnimationCoroutine = StartCoroutine(PlayTwoPhaseWinLines(winLines, onComplete));
    }

    /// <summary>
    /// Starts the Phase 2 line-by-line cycle for the spin that just landed. Autoplay and free spins
    /// skip Phase 2 while they're running — a round ends with the presentation parked after Phase 1
    /// — so the controller calls this once the round is genuinely over. Loops until the next
    /// StartSpin kills it, same as an ordinary manual spin.
    /// </summary>
    internal void PlayWinLineCycle()
    {
        if (lastWinLines == null || lastWinLines.Count == 0) return;

        // The player can stop autoplay mid-presentation, in which case Phase 2 was never skipped and
        // is already running. Restarting would double up the coroutine and strobe the lines.
        if (winAnimationCoroutine != null) return;

        KillWinTweens();
        winAnimationCoroutine = StartCoroutine(PlayWinLineCycleRoutine(lastWinLines));
    }

    private IEnumerator PlayTwoPhaseWinLines(List<WinLine> winLines, System.Action onComplete)
    {
        int rowLimit = (gameManager != null && gameManager.gameConfig != null) ? gameManager.gameConfig.rowCount : 3;

        // ==========================================
        // PHASE 1: Show all winning icons at once
        // ==========================================
        HashSet<int> allWinPositions = new HashSet<int>();
        foreach (var winLine in winLines)
        {
            if (winLine.positions != null)
            {
                foreach (int flatIndex in winLine.positions)
                {
                    allWinPositions.Add(flatIndex);
                }
            }
        }

        Debug.Log($"[PlayTwoPhaseWinLines] Phase 1: Showing all {allWinPositions.Count} winning icons at once for {winLines.Count} win lines");

        // Calculate Phase 1 Total Win Amount
        double totalWinAmount = 0;
        foreach (var winLine in winLines)
        {
            totalWinAmount += winLine.winAmount;
        }
        if (totalWinAmount <= 0 && gameManager != null && gameManager.lastResult != null)
        {
            totalWinAmount = gameManager.lastResult.winAmount;
        }

        // Show Phase 1 Total Win Text with final win value
        ShowPhase1TotalWin(totalWinAmount);

        AudioManager.Instance?.PlayWinLinePhase1Start();

        // Animate all winning symbols and wait for their ImageAnimation loops to complete
        yield return StartCoroutine(AnimateWinPositions(allWinPositions));

        KillWinTweens(false);
        HidePhase1TotalWinText();

        // Invoke onComplete immediately after Phase 1 so game logic (Free Spins / Autoplay / Win complete) can proceed
        onComplete?.Invoke();

        // Skip Phase 2 if in Free Spins, Autoplay, or if a feature was triggered — the trigger
        // presentation takes the screen instead, and AnimateAllScatters does its own teardown.
        // A retrigger (spinsAwarded during a free spin) is deliberately not counted: the round is
        // already running and has no separate trigger sequence to make way for.
        bool freeGamesTriggered = gameManager != null && gameManager.lastResult != null
            && gameManager.lastResult.freeGame != null
            && gameManager.lastResult.freeGame.spinsAwarded
            && !gameManager.lastResult.freeGame.isFreeGame;

        // A Hold & Spin trigger takes the screen the same way, and for the same reason: the round's
        // intro starts as soon as this call's onComplete unwinds, so leaving Phase 2 to run would
        // cycle win lines underneath the feature for the next twenty seconds.
        bool holdAndSpinTriggered = gameManager != null && gameManager.lastResult != null
            && gameManager.lastResult.holdAndSpin != null
            && gameManager.lastResult.holdAndSpin.triggered;

        bool hasSpecialFeature = freeGamesTriggered || holdAndSpinTriggered;

        bool skipPhase2 = (gameManager != null && (gameManager.isInFreeSpins || gameManager.isInHoldAndSpin || gameManager.isAutoPlaying)) || hasSpecialFeature;
        if (skipPhase2)
        {
            // Take the presentation down on the way out. Mid-round this is invisible — the next
            // spin's KillAllTweens would have cleared it — but on the last autoplay spin, and at the
            // end of a free-games round, there is no next spin and the dim used to sit there until
            // the player span again. The controller restarts the cycle via PlayWinLineCycle when the
            // round is genuinely over; a special-feature spin is left alone, since AnimateAllScatters
            // does its own KillWinTweens.
            winAnimationCoroutine = null;

            // Only a Free Games trigger is left alone, because AnimateAllScatters immediately does
            // its own KillWinTweens. Hold & Spin has no equivalent — its trigger sequence never
            // touches the win layer — so the dim would sit over the whole feature if this were
            // skipped for it too.
            if (!freeGamesTriggered)
            {
                // Presentation is genuinely over here, so any dim the Mystery reveal was holding
                // is released before the teardown rather than surviving it.
                ReleaseHeldDim();
                KillWinTweens();
            }
            yield break;
        }

        yield return PlayWinLineCycleRoutine(winLines);
    }

    // ==========================================
    // PHASE 2: Individual Win Line presentation loop
    // ==========================================
    // Split out of PlayTwoPhaseWinLines so the controller can start it on its own once an autoplay
    // or free-games round ends. Loops until something kills the coroutine — normally the next
    // StartSpin.
    private IEnumerator PlayWinLineCycleRoutine(List<WinLine> winLines)
    {
        while (true)
        {
            foreach (var winLine in winLines)
            {
                if (winLine.positions == null || winLine.positions.Count == 0) continue;

                KillWinTweens(false);

                // Lines are a Phase 2 thing only — Phase 1 shows every winning symbol at once
                // with no line drawn, then this cycle walks them one at a time.
                ShowWinLine(winLine.lineId, winLine.winAmount);

                // Animate win line symbols and wait for their ImageAnimation loops to complete
                yield return StartCoroutine(AnimateWinPositions(winLine.positions));
            }
        }
    }

    private IEnumerator AnimateWinPositions(IEnumerable<int> flatPositions)
    {
        if (flatPositions == null) yield break;

        int rowLimit = (gameManager != null && gameManager.gameConfig != null) ? gameManager.gameConfig.rowCount : 3;
        int loopCountTarget = (gameManager != null && (gameManager.isInFreeSpins || gameManager.isAutoPlaying)) ? 1 : winSymbolLoopCount;

        List<ImageAnimation> activeAnims = new List<ImageAnimation>();
        int completedCount = 0;
        bool isCompleted = false;

        bool anyShown = false;

        foreach (int flatIndex in flatPositions)
        {
            int row = flatIndex / ReelCount;
            int col = flatIndex % ReelCount;

            if (col < 0 || col >= ReelCount || row < 0 || row >= rowLimit) continue;

            // Image lookup goes to the animation layer, which holds one slot per visible cell.
            if (animSlotColumns == null || col >= animSlotColumns.Count) continue;
            var column = animSlotColumns[col];
            if (column == null || column.rows == null || row >= column.rows.Count) continue;

            AnimSlot slot = column.rows[row];
            if (slot == null || slot.image == null) continue;

            Image slotImage = slot.image;

            int matrixRow = row;
            if (col >= currentDisplayMatrix.Count || matrixRow >= currentDisplayMatrix[col].Count) continue;
            int symbolId = currentDisplayMatrix[col][matrixRow];

            // The Bonus never takes part in a line win, but the server lists every cell a payline
            // passes through — not just the ones that paid — so a Bonus standing on a wild-driven
            // line arrives here like any other winning symbol. Leaving it dimmed is correct: it
            // didn't win, and it has its own presentation via AnimateAllScatters when three of
            // them actually trigger the feature.
            // (Blanks reach here the same way and are still lit; deliberately left for later.)
            int winBonusId = (gameManager != null && gameManager.gameConfig != null)
                ? gameManager.gameConfig.scatterSymbolId
                : 0;
            if (symbolId == winBonusId) continue;

            // Show the symbol first, unconditionally. Some symbols have no animation frames at all
            // (their anim list is left empty), and under the dim a skipped slot would leave a
            // winning symbol sitting dark while its neighbours light up.
            slotImage.DOKill();
            ApplySymbol(slotImage, symbolId);
            slotImage.transform.localScale = Vector3.one;
            Color c = slotImage.color;
            slotImage.color = new Color(c.r, c.g, c.b, 1f);
            slotImage.gameObject.SetActive(true);
            anyShown = true;

            // Take the reel icon underneath out of the picture entirely. It sits below the dim but
            // is still faintly visible through it, and an oversized neighbour can poke into this
            // cell — either way it reads as a ghost behind the bright copy. Hidden per-cell rather
            // than blanket-hiding the display block, so the scatter (skipped above) correctly stays
            // on screen and dimmed. HideWinSlots puts every icon back.
            SetDisplayIconActive(col, row, false);

            // Animate on top of that only if this symbol actually has frames.
            if (symbolId < 0 || symbolId >= animationSpriteArrays.Length) continue;
            List<Sprite> animSprites = animationSpriteArrays[symbolId];
            if (animSprites == null || animSprites.Count == 0) continue;

            ImageAnimation imageAnim = slot.animation;
            if (imageAnim == null) continue;

            imageAnim.textureArray = animSprites;
            imageAnim.doLoopAnimation = true;
            // Must be set before StartAnimation() below — that is the only place ImageAnimation
            // reads it, so a later change would not take effect until the next start.
            imageAnim.AnimationSpeed = GetSymbolAnimationSpeed(symbolId);

            activeAnims.Add(imageAnim);

            imageAnim.onLoopComplete = (currentLoop) =>
            {
                if (currentLoop >= loopCountTarget)
                {
                    imageAnim.onLoopComplete = null;
                    imageAnim.StopAnimation(); // reverts to textureArray[0], which equals the resting sprite

                    completedCount++;
                    if (completedCount >= activeAnims.Count)
                    {
                        isCompleted = true;
                    }
                }
            };
        }

        // Only raise the dim once something is actually on the layer — otherwise an empty or
        // fully-invalid position set would darken the reels with nothing shown on top.
        if (anyShown)
        {
            if (winDimOverlay != null) winDimOverlay.SetActive(true);
            if (winAnimationLayer != null) winAnimationLayer.SetActive(true);
        }

        if (winLineBoxToAnimationDelay > 0)
        {
            yield return new WaitForSeconds(winLineBoxToAnimationDelay);
        }

        foreach (var imageAnim in activeAnims)
        {
            imageAnim.StartAnimation();
        }

        if (activeAnims.Count > 0)
        {
            yield return new WaitUntil(() => isCompleted);
        }
        else
        {
            yield return new WaitForSeconds(winSymbolLoopDuration);
        }
    }

    private void ShowPhase1TotalWin(double totalWinAmount)
    {
        if (phase1TotalWinText != null)
        {
            phase1TotalWinText.text = totalWinAmount.ToString(SpriteTextFormatter.MoneyFormat);
            AnimateTextScaleAppear(phase1TotalWinText.transform);
        }
    }

    private void HidePhase1TotalWinText()
    {
        if (phase1TotalWinText != null)
        {
            phase1TotalWinText.transform.DOKill();
            phase1TotalWinText.transform.localScale = Vector3.one;
            phase1TotalWinText.gameObject.SetActive(false);
        }
    }

    private void AnimateTextScaleAppear(Transform textTransform, float popScale = 1.2f, float durationUp = 0.15f, float durationDown = 0.10f)
    {
        if (textTransform == null) return;
        textTransform.DOKill();
        textTransform.localScale = Vector3.zero;
        textTransform.gameObject.SetActive(true);

        Sequence seq = DOTween.Sequence();
        seq.Append(textTransform.DOScale(popScale, durationUp).SetEase(Ease.OutQuad));
        seq.Append(textTransform.DOScale(1.0f, durationDown).SetEase(Ease.InQuad));
        winTweens.Add(seq);
    }

    private void KillWinTweens(bool stopCoroutine = true)
    {
        foreach (var tween in winTweens)
        {
            tween?.Kill();
        }
        winTweens.Clear();

        if (stopCoroutine && winAnimationCoroutine != null)
        {
            StopCoroutine(winAnimationCoroutine);
            winAnimationCoroutine = null;
        }

        HidePhase1TotalWinText();

        // Stop all in-flight win animations and restore alpha for every icon — covers both the
        // buffer (images) and the display block (displayImages). ImageAnimation lives directly on
        // each display icon's SlotIcon root (sharing that same Image), so GetComponent finds it
        // there; buffer icons simply have none and are skipped.
        void RestoreImageList(List<Image> imageList)
        {
            if (imageList == null) return;
            foreach (var image in imageList)
            {
                if (image != null)
                {
                    image.DOKill();
                    image.transform.localScale = Vector3.one;
                    Color c = image.color;
                    image.color = new Color(c.r, c.g, c.b, 1f);

                    ImageAnimation imageAnim = image.GetComponent<ImageAnimation>();
                    if (imageAnim != null)
                    {
                        imageAnim.onLoopComplete = null;
                        imageAnim.StopAnimation();
                    }
                }
            }
        }

        foreach (var reel in reelImagesList)
        {
            RestoreImageList(reel.images);
            RestoreImageList(reel.displayImages);
        }

        // The win-layer slots are cleared on *every* call, including the between-cycle reset in
        // Phase 2 — each cycle shows one win line, so the previous line's symbols have to go
        // before the next line's appear. Its Image and ImageAnimation are separate explicit
        // references, so this can't reuse RestoreImageList's GetComponent-based pass.
        if (animSlotColumns != null)
        {
            foreach (var column in animSlotColumns)
            {
                if (column == null || column.rows == null) continue;
                foreach (var slot in column.rows)
                {
                    if (slot == null) continue;

                    if (slot.image != null)
                    {
                        slot.image.DOKill();
                        slot.image.transform.localScale = Vector3.one;
                        Color c = slot.image.color;
                        slot.image.color = new Color(c.r, c.g, c.b, 1f);
                    }

                    if (slot.animation != null)
                    {
                        slot.animation.onLoopComplete = null;
                        slot.animation.StopAnimation();
                    }
                }
            }
        }
        HideWinSlots();

        // Lines clear on every call too — Phase 2 shows one at a time, so the previous line has
        // to go before the next is raised.
        HideAllWinLines();

        // The dim itself only comes down on a full teardown. Hiding it on the between-cycle reset
        // would make it strobe once per win line.
        if (stopCoroutine) HideWinDim();
    }

    // Shows or hides the reel icon sitting behind one win-layer slot. Row indices need no
    // translation — displayImages holds one entry per visible row, in the same order as the
    // server matrix and the animation layer.
    private void SetDisplayIconActive(int col, int row, bool active)
    {
        if (reelImagesList == null || col < 0 || col >= reelImagesList.Count) return;

        var reel = reelImagesList[col];
        if (reel == null || reel.displayImages == null) return;
        if (row < 0 || row >= reel.displayImages.Count) return;

        Image icon = reel.displayImages[row];
        if (icon != null) icon.gameObject.SetActive(active);
    }

    // Takes the whole win layer down and restores every reel icon underneath it. The restore is
    // deliberately unconditional and paired with the hide in this one method: AnimateWinPositions
    // hides icons per winning cell, and if any of them were missed here that cell would stay blank
    // for the rest of the session. Every teardown path runs through here — between Phase 2 lines,
    // at the end of the cycle, on the next StartSpin, and on Start.
    private void HideWinSlots()
    {
        if (animSlotColumns != null)
        {
            foreach (var column in animSlotColumns)
            {
                if (column == null || column.rows == null) continue;
                foreach (var slot in column.rows)
                {
                    if (slot != null && slot.image != null) slot.image.gameObject.SetActive(false);
                }
            }
        }

        if (reelImagesList == null) return;
        for (int col = 0; col < reelImagesList.Count; col++)
        {
            var reel = reelImagesList[col];
            if (reel == null || reel.displayImages == null) continue;

            for (int row = 0; row < reel.displayImages.Count; row++)
            {
                if (reel.displayImages[row] != null) reel.displayImages[row].gameObject.SetActive(true);
            }
        }
    }

    // Raises one payline graphic and writes that line's own payout onto it. Indexed straight off
    // the server's lineIndex, so there's no naming convention or lookup table to keep in step with
    // the backend.
    private void ShowWinLine(int lineId, double winAmount)
    {
        if (winLineVisuals == null) return;

        if (lineId < 0 || lineId >= winLineVisuals.Length)
        {
            Debug.LogWarning($"[SlotView] Win line index {lineId} is outside winLineVisuals ({winLineVisuals.Length} entries) — no line shown.");
            return;
        }

        WinLineVisual visual = winLineVisuals[lineId];
        if (visual == null)
        {
            Debug.LogWarning($"[SlotView] No entry for win line index {lineId} — no line shown.");
            return;
        }

        // The two halves are reported separately: art and label are wired independently, so a
        // missing one shouldn't suppress the other. Naming the index makes the gap identifiable.
        if (visual.line != null)
        {
            visual.line.gameObject.SetActive(true);
        }
        else
        {
            Debug.LogWarning($"[SlotView] No graphic assigned for win line index {lineId} — no line shown.");
        }

        if (visual.amount != null)
        {
            visual.amount.text = winAmount.ToString(SpriteTextFormatter.MoneyFormat);
            visual.amount.gameObject.SetActive(true);
        }
        else
        {
            Debug.LogWarning($"[SlotView] No amount label assigned for win line index {lineId} — the line will show without its payout.");
        }
    }

    private void HideAllWinLines()
    {
        if (winLineVisuals == null) return;
        foreach (var visual in winLineVisuals)
        {
            if (visual == null) continue;
            if (visual.line != null) visual.line.gameObject.SetActive(false);
            // The label lives under a different parent to the line — it draws in front of the
            // winning symbols while the line draws behind them — so it needs its own hide.
            if (visual.amount != null) visual.amount.gameObject.SetActive(false);
        }
    }

    // The win layer always comes down, but the dim itself is skipped while the Mystery reveal is
    // holding it up: the reveal raises it and the win presentation that follows is meant to inherit
    // it. Without this, ShowWinLineAnimation's opening KillWinTweens would drop the dim a frame
    // before Phase 1 raised it again, which reads as a flicker.
    private void HideWinDim()
    {
        if (winAnimationLayer != null) winAnimationLayer.SetActive(false);

        if (dimHeld) return;
        if (winDimOverlay != null) winDimOverlay.SetActive(false);
    }

    // Releases the reveal's claim on the dim and takes it down. Called at the end of the whole
    // presentation, so a Mystery spin that produced no win still clears correctly.
    private void ReleaseHeldDim()
    {
        dimHeld = false;
        if (winDimOverlay != null) winDimOverlay.SetActive(false);
    }

    #endregion


    
    internal List<List<int>> GetCurrentDisplayMatrix()
    {
        return currentDisplayMatrix;
    }

    internal bool IsSpinning()
    {
        return isSpinning;
    }


    private void KillAllTweens()
    {
        foreach (var tween in spinTweens)
        {
            tween?.Kill();
        }
        spinTweens.Clear();

        KillWinTweens();
    }

    #region Cleanup

    private void OnDestroy()
    {
        KillAllTweens();
    }

    #endregion
}

// One win-animation slot: the Image that shows the symbol and the ImageAnimation that plays it.
// Both are wired explicitly rather than found with GetComponent — a missing component would
// otherwise just silently no-op, and these icons must have one while the reel icons must not.
// One payline: its graphic and its payout label. Paired in a single object for the same reason
// AnimSlot is — two arrays indexed by lineId could silently drift, and the failure would look
// exactly like the art-ordering bug that already cost a debugging session (line 5 drawn with
// line 6's amount). The label is NOT a child of the line: lines draw behind the winning symbols
// and the amounts in front, so they live under different parents and are shown/hidden separately.
[System.Serializable]
public class WinLineVisual
{
    public Image line;
    public TMPro.TMP_Text amount;
}

// Kept in a single struct so the two can never drift out of step with each other.
[System.Serializable]
public class AnimSlot
{
    public Image image;
    public ImageAnimation animation;
}

// One reel column's worth of win-animation slots. These live on a layer above the dim overlay,
// so a winning symbol can be shown bright while the real reel icon stays dimmed underneath.
// One slot per visible cell — 3 rows, matching the grid exactly.
[System.Serializable]
public class AnimSlotColumn
{
    public List<AnimSlot> rows = new List<AnimSlot>(3);   // index 0 = top active row
}

// One Orb-layer cell. Like AnimSlot but with the prize text, which is the whole reason this layer
// exists — no other surface in the game can draw a number on a symbol. ApplySymbol writes a sprite
// and a size and nothing else.
[System.Serializable]
public class OrbSlot
{
    public Image image;
    public ImageAnimation animation;
    public TMPro.TMP_Text prizeText;
}

// One reel column's worth of Orb slots — 3 rows, matching the grid, same shape as AnimSlotColumn.
[System.Serializable]
public class OrbSlotColumn
{
    public List<OrbSlot> rows = new List<OrbSlot>(3);     // index 0 = top active row
}

[System.Serializable]
public class ReelImages
{
    // Pure scroll buffer — everything except the real display-block icons below.
    public List<Image> images = new List<Image>(16);
    // Direct references to the real display-block icons, top row first — one per visible row, so
    // a row index here means the same thing it does in the server matrix. Wired manually per reel
    // in the Inspector, not derived from bufferRowsAbove, so each reel's buffer icon count can
    // differ without breaking which icons show the real backend result.
    public List<Image> displayImages = new List<Image>(3);
}