using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System.Collections.Generic;

public class OCController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private OrientationChange orientationChange;
    [SerializeField] private CanvasScaler canvasScaler;
    [SerializeField] private Transform slotObject;
    [SerializeField] private List<RectTransform> resizedObjects = new List<RectTransform>();
    [SerializeField] private List<RectTransform> squareResizedObjects = new List<RectTransform>();

    [Header("Panel Toggle Settings")]
    [SerializeField] private GameObject landscapePanelObject;
    [SerializeField] private GameObject portraitPanelObject;

    [Header("Background Toggle Settings")]
    [SerializeField] private GameObject landscapeBackground;
    [SerializeField] private GameObject portraitBackground;

    [Header("Canvas Scaler Resolutions")]
    [SerializeField] private Vector2 landscapeReferenceResolution = new Vector2(1920f, 1080f);
    [SerializeField] private Vector2 portraitReferenceResolution = new Vector2(1080f, 1920f);

    [Header("Resized Object Dimensions")]
    [SerializeField] private Vector2 landscapeResizedObjectSize = new Vector2(1920f, 1080f);
    [SerializeField] private Vector2 portraitResizedObjectSize = new Vector2(1080f, 1920f);

    [Header("Square Resized Object Dimensions")]
    [SerializeField] private Vector2 landscapeSquareResizedObjectSize = new Vector2(1920f, 1080f);
    [SerializeField] private Vector2 portraitSquareResizedObjectSize = new Vector2(1920f, 1920f);

    [Header("Slot Object Settings")]
    [SerializeField] private Vector3 landscapeSlotScale = Vector3.one;
    [SerializeField] private Vector3 portraitSlotScale = new Vector3(0.73f, 0.73f, 0.73f);
    [SerializeField] private Vector3 landscapeSlotPosition = Vector3.zero;
    [SerializeField] private Vector3 portraitSlotPosition = new Vector3(0f, -150f, 0f);

    [Header("Logo Object Settings")]
    [SerializeField] private RectTransform logoObject;
    [SerializeField] private Vector3 landscapeLogoScale = Vector3.one;
    [SerializeField] private Vector3 portraitLogoScale = new Vector3(1.27f, 1.27f, 1.27f);
    [SerializeField] private Vector2 landscapeLogoPosition = new Vector2(0f, 355f);
    [SerializeField] private Vector2 portraitLogoPosition = new Vector2(0f, 500f);

    [Header("Info Page & Guide Settings")]
    [SerializeField] private RectTransform infoPageScrollObject;
    [SerializeField] private RectTransform guideScrollObject;

    [Header("Animation Settings")]
    [SerializeField] private float transitionDuration = 0.2f;

    [Header("Jackpot Levitation - Portrait")]
    [Tooltip("The four jackpot tier parents inside the portrait Top prefab. Each floats up and back " +
             "down while the game is in portrait.")]
    [SerializeField] private RectTransform grandJackpotPortraitParent;
    [SerializeField] private RectTransform majorJackpotPortraitParent;
    [SerializeField] private RectTransform minorJackpotPortraitParent;
    [SerializeField] private RectTransform miniJackpotPortraitParent;
    [SerializeField] private bool enableJackpotPortraitLevitation = true;
    [SerializeField] private float jackpotLevitateHeight = 10f;
    [SerializeField] private float jackpotLevitateDuration = 1.4f;
    [SerializeField] private float jackpotStaggerDelay = 0.15f;

    private List<Tween> activeTweens = new List<Tween>();

    // Levitation tweens are tracked separately from activeTweens on purpose: those are the
    // one-shot orientation transitions and get killed at the top of every HandleOrientationChange,
    // whereas these loop for as long as the game stays in portrait.
    private readonly List<Tween> jackpotPortraitTweens = new List<Tween>();

    // Each parent's resting position, captured the first time it levitates so the loop can always
    // be returned to exactly where the layout put it.
    private readonly Dictionary<Transform, Vector3> jackpotInitialLocalPositions = new Dictionary<Transform, Vector3>();

    private void Awake()
    {
        if (orientationChange == null)
        {
            orientationChange = GetComponent<OrientationChange>();
            if (orientationChange == null)
            {
                orientationChange = Object.FindFirstObjectByType<OrientationChange>();
            }
        }
        if (canvasScaler == null && orientationChange != null)
        {
            canvasScaler = orientationChange.GetComponent<CanvasScaler>();
        }
    }

    private void OnEnable()
    {
        OrientationChange.OnOrientationChanged += HandleOrientationChange;
        if (orientationChange != null)
        {
            orientationChange.OnOrientationChangedInstance += HandleOrientationChange;
        }

        // The event only fires on a *change*, so a session that starts in portrait would otherwise
        // never levitate until the player rotated away and back.
        if (orientationChange != null) UpdateJackpotPortraitLevitation(orientationChange.CurrentMode);
    }

    private void OnDisable()
    {
        OrientationChange.OnOrientationChanged -= HandleOrientationChange;
        if (orientationChange != null)
        {
            orientationChange.OnOrientationChangedInstance -= HandleOrientationChange;
        }

        // Looping tweens outlive this component otherwise — nothing else would ever kill them.
        StopJackpotPortraitLevitation();
    }

    private void HandleOrientationChange(OrientationChange.OrientationMode mode, int width, int height)
    {
        KillActiveTweens();

        bool isMobilePortrait = (mode == OrientationChange.OrientationMode.MobilePortrait);

        UpdateJackpotPortraitLevitation(mode);

        // 1. Toggle Landscape vs Portrait Panel Objects
        if (landscapePanelObject != null)
        {
            landscapePanelObject.SetActive(!isMobilePortrait);
        }
        if (portraitPanelObject != null)
        {
            portraitPanelObject.SetActive(isMobilePortrait);
        }

        // 2. Toggle Landscape vs Portrait Background Objects
        if (landscapeBackground != null)
        {
            landscapeBackground.SetActive(!isMobilePortrait);
        }
        if (portraitBackground != null)
        {
            portraitBackground.SetActive(isMobilePortrait);
        }

        // 3. Update Canvas Scaler Reference Resolution
        if (canvasScaler != null)
        {
            Vector2 targetRefRes = isMobilePortrait ? portraitReferenceResolution : landscapeReferenceResolution;
            canvasScaler.referenceResolution = targetRefRes;
        }

        // 4. Resize Target RectTransforms
        Vector2 targetSize = isMobilePortrait ? portraitResizedObjectSize : landscapeResizedObjectSize;
        if (resizedObjects != null)
        {
            foreach (var rect in resizedObjects)
            {
                if (rect != null)
                {
                    if (transitionDuration > 0)
                    {
                        Tween t = rect.DOSizeDelta(targetSize, transitionDuration).SetEase(Ease.OutCubic);
                        activeTweens.Add(t);
                    }
                    else
                    {
                        rect.sizeDelta = targetSize;
                    }
                }
            }
        }

        // 4b. Resize Target RectTransforms (1920x1080 Landscape, 1920x1920 Portrait)
        Vector2 targetSquareSize = isMobilePortrait ? portraitSquareResizedObjectSize : landscapeSquareResizedObjectSize;
        if (squareResizedObjects != null)
        {
            foreach (var rect in squareResizedObjects)
            {
                if (rect != null)
                {
                    if (transitionDuration > 0)
                    {
                        Tween t = rect.DOSizeDelta(targetSquareSize, transitionDuration).SetEase(Ease.OutCubic);
                        activeTweens.Add(t);
                    }
                    else
                    {
                        rect.sizeDelta = targetSquareSize;
                    }
                }
            }
        }

        // 5. Update Slot Object Scale and Position
        if (slotObject != null)
        {
            Vector3 targetScale = isMobilePortrait ? portraitSlotScale : landscapeSlotScale;
            Vector3 targetPosition = isMobilePortrait ? portraitSlotPosition : landscapeSlotPosition;

            if (transitionDuration > 0)
            {
                Tween scaleTween = slotObject.DOScale(targetScale, transitionDuration).SetEase(Ease.OutCubic);
                Tween posTween = slotObject.DOLocalMove(targetPosition, transitionDuration).SetEase(Ease.OutCubic);
                activeTweens.Add(scaleTween);
                activeTweens.Add(posTween);
            }
            else
            {
                slotObject.localScale = targetScale;
                slotObject.localPosition = targetPosition;
            }
        }

        // 6. Update Logo Object Scale and Position
        if (logoObject != null)
        {
            Vector3 targetScale = isMobilePortrait ? portraitLogoScale : landscapeLogoScale;
            Vector2 targetPosition = isMobilePortrait ? portraitLogoPosition : landscapeLogoPosition;

            if (transitionDuration > 0)
            {
                Tween scaleTween = logoObject.DOScale(targetScale, transitionDuration).SetEase(Ease.OutCubic);
                Tween posTween = logoObject.DOAnchorPos(targetPosition, transitionDuration).SetEase(Ease.OutCubic);
                activeTweens.Add(scaleTween);
                activeTweens.Add(posTween);
            }
            else
            {
                logoObject.localScale = targetScale;
                logoObject.anchoredPosition = targetPosition;
            }
        }

        // 7. Update Info Page Scroll Object Height (1080 for Landscape, 1920 for Mobile Portrait)
        if (infoPageScrollObject != null)
        {
            float targetHeight = isMobilePortrait ? 1920f : 1080f;
            Vector2 targetScrollSize = new Vector2(infoPageScrollObject.sizeDelta.x, targetHeight);
            if (transitionDuration > 0)
            {
                Tween scrollTween = infoPageScrollObject.DOSizeDelta(targetScrollSize, transitionDuration).SetEase(Ease.OutCubic);
                activeTweens.Add(scrollTween);
            }
            else
            {
                infoPageScrollObject.sizeDelta = targetScrollSize;
            }
        }

        // 8. Update Guide Scroll Object Height (1080 for Landscape, 1920 for Mobile Portrait)
        if (guideScrollObject != null)
        {
            float targetHeight = isMobilePortrait ? 1920f : 1080f;
            Vector2 targetScrollSize = new Vector2(guideScrollObject.sizeDelta.x, targetHeight);
            if (transitionDuration > 0)
            {
                Tween scrollTween = guideScrollObject.DOSizeDelta(targetScrollSize, transitionDuration).SetEase(Ease.OutCubic);
                activeTweens.Add(scrollTween);
            }
            else
            {
                guideScrollObject.sizeDelta = targetScrollSize;
            }
        }
    }

    #region Jackpot Portrait Levitation

    // Portrait here means MobilePortrait only, matching HandleOrientationChange's own test — that's
    // the mode where portraitPanelObject is actually shown, so in any other mode these objects are
    // hidden and animating them would be wasted work.
    private void UpdateJackpotPortraitLevitation(OrientationChange.OrientationMode mode)
    {
        if (mode == OrientationChange.OrientationMode.MobilePortrait)
        {
            StartJackpotPortraitLevitation();
        }
        else
        {
            StopJackpotPortraitLevitation();
        }
    }

    private List<RectTransform> GetJackpotPortraitTransforms()
    {
        List<RectTransform> list = new List<RectTransform>();

        if (grandJackpotPortraitParent != null) list.Add(grandJackpotPortraitParent);
        if (majorJackpotPortraitParent != null) list.Add(majorJackpotPortraitParent);
        if (minorJackpotPortraitParent != null) list.Add(minorJackpotPortraitParent);
        if (miniJackpotPortraitParent != null) list.Add(miniJackpotPortraitParent);

        return list;
    }

    private void StartJackpotPortraitLevitation()
    {
        if (!enableJackpotPortraitLevitation) return;

        // Clears any previous run first, so repeated portrait entries can't stack tweens on the
        // same objects.
        StopJackpotPortraitLevitation();

        List<RectTransform> portraitJackpots = GetJackpotPortraitTransforms();
        if (portraitJackpots.Count == 0) return;

        for (int i = 0; i < portraitJackpots.Count; i++)
        {
            RectTransform tr = portraitJackpots[i];

            // Captured once and never overwritten: re-reading it on a later run would bake in
            // whatever mid-float position the object happened to be at.
            if (!jackpotInitialLocalPositions.ContainsKey(tr))
            {
                jackpotInitialLocalPositions[tr] = tr.localPosition;
            }

            Vector3 startPos = jackpotInitialLocalPositions[tr];
            tr.localPosition = startPos;

            Tween posTween = tr.DOLocalMoveY(startPos.y + jackpotLevitateHeight, jackpotLevitateDuration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetDelay(i * jackpotStaggerDelay);

            jackpotPortraitTweens.Add(posTween);
        }
    }

    private void StopJackpotPortraitLevitation()
    {
        for (int i = 0; i < jackpotPortraitTweens.Count; i++)
        {
            if (jackpotPortraitTweens[i] != null && jackpotPortraitTweens[i].IsActive())
            {
                jackpotPortraitTweens[i].Kill();
            }
        }
        jackpotPortraitTweens.Clear();

        // Snap back to the captured resting position. DOTween.Kill on the target is a belt-and-
        // braces sweep for a tween that somehow escaped the list; it's safe here only because
        // nothing else in this class tweens these objects — activeTweens covers a disjoint set
        // (BG, InfoPageObject, GuidepageObject, SoundPanel, PopupObject, BlackFlim, RayCastBlocker,
        // plus slotObject/logoObject/the two scroll objects). Re-check that if these parents are
        // ever added to resizedObjects.
        foreach (var kvp in jackpotInitialLocalPositions)
        {
            if (kvp.Key != null)
            {
                DOTween.Kill(kvp.Key);
                kvp.Key.localPosition = kvp.Value;
            }
        }
    }

    #endregion

    private void KillActiveTweens()
    {
        foreach (var t in activeTweens)
        {
            if (t != null && t.IsActive())
            {
                t.Kill();
            }
        }
        activeTweens.Clear();
    }
}
