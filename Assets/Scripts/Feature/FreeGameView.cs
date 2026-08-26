using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Presentation for the free games (pick-a-box) bonus: the trigger frame, the mystery box pick,
/// the prize reveal, the persistent multiplier / X-of-Y displays, and the closing total-win
/// summary.
///
/// View layer only. GameManager drives this via the public methods below and receives callbacks
/// when a sequence finishes — this script never calls back into the game loop (no RequestSpin,
/// no state changes). Attach to a GameObject that stays active for the whole session, NOT to the
/// frame itself: deactivating the frame would halt these coroutines mid-sequence.
/// </summary>
public class FreeGameView : MonoBehaviour
{
    // Pairs a free-games prize tier with the prefab that shows it. Prefab names already match the
    // spin counts (5/8/10/15FreeGames), so the award count alone selects the right one — no need
    // to map the server's boxId string, which keeps an unexpected boxId from breaking the reveal.
    [Serializable]
    public class PrizeFront
    {
        public int awardedSpins;
        public GameObject frontPrefab;
    }

    [Header("References")]
    [SerializeField] private UIManager uiManager;

    [Header("Frame")]
    [Tooltip("Root of the free-games frame graphic. Scaled 0->1 on trigger, 1->0 after the pick.")]
    [SerializeField] private GameObject frameRoot;
    [SerializeField] private RectTransform frameRect;

    [Header("Frame Text")]
    [SerializeField] private CanvasGroup awardedTextGroup;      // "FREE GAMES AWARDED"
    [SerializeField] private CanvasGroup chooseTextGroup;       // "CHOOSE YOUR FREE GAMES"
    [Tooltip("Heading on the closing summary. The outro used to re-show awardedTextGroup, so the " +
             "round ended on the same \"FREE GAMES AWARDED\" line it opened with.")]
    [SerializeField] private CanvasGroup summaryTextGroup;
    [Tooltip("Total win counter shown in the closing summary.")]
    [SerializeField] private TMPro.TMP_Text totalWinText;

    [Header("Mystery Boxes")]
    [Tooltip("The 4 positions the mystery boxes are placed into, left to right.")]
    [SerializeField] private Transform[] boxSlots = new Transform[4];
    [Tooltip("The face-down purple box shown before the pick.")]
    [SerializeField] private GameObject mysteryBackPrefab;
    [Tooltip("One entry per prize tier. Matched against the server's awardedSpins.")]
    [SerializeField] private PrizeFront[] prizeFronts = new PrizeFront[4];

    [Header("Multiplier Panel")]
    [SerializeField] private GameObject multiplierPanelRoot;
    [SerializeField] private TMPro.TMP_Text multiplierValueText;
    // Same reasoning as the timing block below — code-owned, not overridable by the scene.
    private const float multiplierPulseScale = 1.15f;
    private const float multiplierPulseDuration = 0.6f;

    [Header("Free Games Counter")]
    [SerializeField] private GameObject freeSpinCountContainer;
    [SerializeField] private TMPro.TMP_Text remainingFreeSpinsText;

    [Header("Background Swap")]
    [Tooltip("The SlotObject image — swapped for the whole feature, reverted on the closing fade.")]
    [SerializeField] private Image slotObjectImage;
    [SerializeField] private Sprite slotObjectNormalSprite;
    [SerializeField] private Sprite slotObjectFreeGamesSprite;
    [SerializeField] private Image reelBackgroundImage;
    [SerializeField] private Sprite reelBackgroundNormalSprite;
    [SerializeField] private Sprite reelBackgroundFreeGamesSprite;

    [Header("Closing Fade")]
    [SerializeField] private CanvasGroup fadeToBlackGroup;

    // Deliberately NOT [SerializeField]. These were serialized, which meant the scene's saved
    // values silently overrode any change made here — retuning in code appeared to do nothing.
    // Tuning these is now a code edit, and the code is the single source of truth.
    private const float frameOpenDuration = 1.25f;
    private const float frameCloseDuration = 1.0f;
    private const float textFadeDuration = 0.4f;
    private const float awardedTextHold = 2.0f;
    private const float revealHold = 1.5f;
    private const float boxCrossfadeDuration = 0.4f;
    private const float totalWinCountUpDuration = 2.0f;
    private const float fadeToBlackDuration = 0.5f;
    // Applied to both the face-down back and the prize front, since they crossfade in the same
    // slot — a mismatch would make the reveal jump in size. Has to live here rather than on the
    // prefabs: CenterInSlot overwrites localScale on every instantiate.
    private const float boxScale = 0.85f;

    // Live box instances for the current round, destroyed on reset.
    private readonly List<GameObject> spawnedBacks = new List<GameObject>();
    private readonly List<GameObject> spawnedFronts = new List<GameObject>();

    private Coroutine activeSequence;
    private Tween multiplierPulseTween;
    private Tween totalWinTween;
    private bool boxPicked;
    private int pickedSlotIndex = -1;
    private Action pendingTakeCallback;
    private bool missingRefsLogged;

    #region Public API — called by GameManager

    /// <summary>
    /// Frame in, text beats, box pick, prize reveal, frame out. Invokes onComplete once the
    /// player has picked and the frame has closed.
    /// </summary>
    internal void PlayIntroSequence(string boxId, int awardedSpins, Action onComplete)
    {
        if (!HasRequiredRefs())
        {
            // UI not built/wired yet — skip straight through so the round still runs.
            onComplete?.Invoke();
            return;
        }

        StopActiveSequence();
        activeSequence = StartCoroutine(IntroRoutine(boxId, awardedSpins, onComplete));
    }

    /// <summary>Shows the multiplier panel and starts its continuous pulse.</summary>
    internal void ShowMultiplierPanel()
    {
        if (multiplierPanelRoot == null) return;

        multiplierPanelRoot.SetActive(true);
        StartMultiplierPulse();
    }

    /// <summary>
    /// Pushes the round's live numbers into the persistent displays. Called after every free spin.
    /// A null multiplier means the server didn't send one for this spin — the previous value is
    /// left on screen rather than blanking out.
    /// </summary>
    internal void UpdateCounters(int spinsUsed, int totalSpins, double? multiplier)
    {
        if (freeSpinCountContainer != null) freeSpinCountContainer.SetActive(true);
        if (remainingFreeSpinsText != null)
        {
            remainingFreeSpinsText.text = $"FREE GAME  {spinsUsed}  OF  {totalSpins}";
        }

        if (multiplier.HasValue && multiplierValueText != null)
        {
            multiplierValueText.text = "X" + multiplier.Value.ToString("0.###");
        }
    }

    /// <summary>
    /// Closing summary: frame reappears (no tween), total win counts up, Take button unlocks once
    /// the count finishes, then fade to black, reset, fade back. onComplete fires after the fade.
    /// </summary>
    internal void PlayOutroSequence(double totalRoundWin, Action onComplete)
    {
        if (!HasRequiredRefs())
        {
            ResetToDefault();
            onComplete?.Invoke();
            return;
        }

        StopActiveSequence();
        activeSequence = StartCoroutine(OutroRoutine(totalRoundWin, onComplete));
    }

    /// <summary>
    /// Puts everything back the way the base game expects it. Safe to call at any point.
    /// </summary>
    internal void ResetToDefault()
    {
        StopActiveSequence();
        StopMultiplierPulse();

        if (totalWinTween != null) { totalWinTween.Kill(); totalWinTween = null; }

        SwapBackgrounds(false);
        DestroyBoxes();

        if (frameRect != null) frameRect.DOKill();
        if (frameRoot != null) frameRoot.SetActive(false);
        if (multiplierPanelRoot != null) multiplierPanelRoot.SetActive(false);
        if (freeSpinCountContainer != null) freeSpinCountContainer.SetActive(false);

        SetGroupAlpha(awardedTextGroup, 0f, false);
        SetGroupAlpha(chooseTextGroup, 0f, false);
        SetGroupAlpha(summaryTextGroup, 0f, false);

        pendingTakeCallback = null;
        boxPicked = false;
        pickedSlotIndex = -1;
    }

    /// <summary>
    /// Invoked by UIManager when the player presses Take on the closing summary.
    /// </summary>
    internal void OnTakePressed()
    {
        var callback = pendingTakeCallback;
        pendingTakeCallback = null;
        callback?.Invoke();
    }

    #endregion

    #region Intro

    private IEnumerator IntroRoutine(string boxId, int awardedSpins, Action onComplete)
    {
        boxPicked = false;
        pickedSlotIndex = -1;

        // Backgrounds swap at the same moment the frame starts growing, and stay swapped for the
        // entire feature — they only revert on the closing fade.
        SwapBackgrounds(true);

        SetGroupAlpha(chooseTextGroup, 0f, false);
        SetGroupAlpha(summaryTextGroup, 0f, false);
        if (totalWinText != null) totalWinText.gameObject.SetActive(false);

        // "FREE GAMES AWARDED" is already on screen as the frame grows, so it scales up with it
        // rather than appearing once the frame has settled.
        SetGroupAlpha(awardedTextGroup, 1f, true);

        frameRoot.SetActive(true);
        frameRect.localScale = Vector3.zero;

        // Cue lands with the frame starting to grow, since the text is visible from that moment.
        AudioManager.Instance?.PlayFreeGamesAwarded();

        yield return frameRect.DOScale(1f, frameOpenDuration).SetEase(Ease.OutQuad).WaitForCompletion();

        // Hold at full size, then clear it to make way for the pick.
        yield return new WaitForSeconds(awardedTextHold);
        yield return FadeGroup(awardedTextGroup, 0f);

        // "CHOOSE YOUR FREE GAMES" plus the boxes.
        SpawnBoxes(boxId, awardedSpins);
        AudioManager.Instance?.PlayChooseFreeGames();
        yield return FadeGroup(chooseTextGroup, 1f);

        yield return new WaitUntil(() => boxPicked);

        // Only the picked box crossfades — the other three stay exactly as they are.
        yield return CrossfadePickedBox();
        yield return new WaitForSeconds(revealHold);

        yield return frameRect.DOScale(0f, frameCloseDuration).SetEase(Ease.InQuad).WaitForCompletion();
        frameRoot.SetActive(false);
        SetGroupAlpha(chooseTextGroup, 0f, false);
        DestroyBoxes();

        if (freeSpinCountContainer != null) freeSpinCountContainer.SetActive(true);
        UpdateCounters(0, awardedSpins, null);

        activeSequence = null;
        onComplete?.Invoke();
    }

    private void SpawnBoxes(string boxId, int awardedSpins)
    {
        DestroyBoxes();

        GameObject frontPrefab = ResolveFrontPrefab(awardedSpins, boxId);

        // One entry per slot either way — the lists are indexed by slot, so a missing slot or
        // prefab still has to occupy its position.
        for (int i = 0; i < boxSlots.Length; i++)
        {
            Transform slot = boxSlots[i];

            GameObject front = null;
            GameObject back = null;

            if (slot != null)
            {
                // The front is instantiated first so it sits behind the back in sibling order.
                // The prize is already decided server-side, so every slot hides the same front.
                if (frontPrefab != null)
                {
                    front = Instantiate(frontPrefab, slot);
                    CenterInSlot(front);
                    EnsureCanvasGroup(front).alpha = 0f;
                }

                if (mysteryBackPrefab != null)
                {
                    back = Instantiate(mysteryBackPrefab, slot);
                    CenterInSlot(back);
                    EnsureCanvasGroup(back).alpha = 1f;

                    int slotIndex = i;
                    Button button = EnsureButton(back);
                    button.onClick.AddListener(() => OnBoxClicked(slotIndex));
                }
            }

            spawnedFronts.Add(front);
            spawnedBacks.Add(back);
        }
    }

    // Selection is by awarded spin count, which the prefabs are already named after. boxId is
    // accepted only so an unexpected value can be logged rather than silently mis-revealing.
    private GameObject ResolveFrontPrefab(int awardedSpins, string boxId)
    {
        if (prizeFronts == null || prizeFronts.Length == 0) return null;

        foreach (var entry in prizeFronts)
        {
            if (entry != null && entry.awardedSpins == awardedSpins && entry.frontPrefab != null)
            {
                return entry.frontPrefab;
            }
        }

        Debug.LogWarning($"[FreeGameView] No prize front configured for {awardedSpins} free games (boxId '{boxId}'). Falling back to the first entry.");
        foreach (var entry in prizeFronts)
        {
            if (entry != null && entry.frontPrefab != null) return entry.frontPrefab;
        }
        return null;
    }

    private void OnBoxClicked(int slotIndex)
    {
        if (boxPicked) return;

        boxPicked = true;
        pickedSlotIndex = slotIndex;

        // Lock all four the instant one is chosen.
        foreach (var back in spawnedBacks)
        {
            if (back == null) continue;
            Button button = back.GetComponent<Button>();
            if (button != null) button.interactable = false;
        }
    }

    private IEnumerator CrossfadePickedBox()
    {
        if (pickedSlotIndex < 0) yield break;

        AudioManager.Instance?.PlayCardReveal();

        CanvasGroup backGroup = GetGroupAt(spawnedBacks, pickedSlotIndex);
        CanvasGroup frontGroup = GetGroupAt(spawnedFronts, pickedSlotIndex);

        if (backGroup != null) backGroup.DOFade(0f, boxCrossfadeDuration);
        if (frontGroup != null) yield return frontGroup.DOFade(1f, boxCrossfadeDuration).WaitForCompletion();
        else yield return new WaitForSeconds(boxCrossfadeDuration);
    }

    #endregion

    #region Outro

    private IEnumerator OutroRoutine(double totalRoundWin, Action onComplete)
    {
        StopMultiplierPulse();
        if (multiplierPanelRoot != null) multiplierPanelRoot.SetActive(false);

        // Reappears at full size with no tween, unlike the trigger.
        frameRoot.SetActive(true);
        frameRect.DOKill();
        frameRect.localScale = Vector3.one;

        SetGroupAlpha(summaryTextGroup, 1f, true);
        // Both of the intro's headings are cleared explicitly. They should already be down by now,
        // but a round cut short earlier could otherwise leave one stacked behind the summary.
        SetGroupAlpha(awardedTextGroup, 0f, false);
        SetGroupAlpha(chooseTextGroup, 0f, false);

        // Take is visible from the start of the summary but greyed out until the count-up ends.
        bool takePressed = false;
        pendingTakeCallback = () => takePressed = true;
        if (uiManager != null) uiManager.SetSpinButtonMode(UIManager.SpinButtonMode.FreeGamesTake, false);

        bool countUpDone = false;
        if (totalWinText != null)
        {
            totalWinText.gameObject.SetActive(true);
            totalWinText.text = SpriteTextFormatter.ToSpriteMoney(0);

            totalWinTween = DOVirtual.Float(0f, (float)totalRoundWin, totalWinCountUpDuration, value =>
            {
                if (totalWinText != null)
                {
                    totalWinText.text = SpriteTextFormatter.ToSpriteMoney(value);
                }
            }).OnComplete(() =>
            {
                if (totalWinText != null)
                {
                    totalWinText.text = SpriteTextFormatter.ToSpriteMoney(totalRoundWin);
                }
                totalWinTween = null;
                countUpDone = true;
            });

            yield return new WaitUntil(() => countUpDone);
        }

        // Count-up finished — Take becomes pressable.
        if (uiManager != null) uiManager.SetSpinButtonMode(UIManager.SpinButtonMode.FreeGamesTake, true);

        yield return new WaitUntil(() => takePressed);

        yield return FadeToBlack(1f);

        // Everything reverts while the screen is covered.
        frameRoot.SetActive(false);
        SetGroupAlpha(summaryTextGroup, 0f, false);
        if (totalWinText != null) totalWinText.gameObject.SetActive(false);
        if (freeSpinCountContainer != null) freeSpinCountContainer.SetActive(false);
        SwapBackgrounds(false);
        DestroyBoxes();

        yield return FadeToBlack(0f);

        activeSequence = null;
        onComplete?.Invoke();
    }

    private IEnumerator FadeToBlack(float targetAlpha)
    {
        if (fadeToBlackGroup == null)
        {
            yield break;
        }

        fadeToBlackGroup.gameObject.SetActive(true);
        yield return fadeToBlackGroup.DOFade(targetAlpha, fadeToBlackDuration).WaitForCompletion();

        if (Mathf.Approximately(targetAlpha, 0f))
        {
            fadeToBlackGroup.gameObject.SetActive(false);
        }
    }

    #endregion

    #region Helpers

    // Both images swap together in one place so they can never drift out of sync.
    private void SwapBackgrounds(bool toFreeGames)
    {
        if (slotObjectImage != null)
        {
            Sprite target = toFreeGames ? slotObjectFreeGamesSprite : slotObjectNormalSprite;
            if (target != null) slotObjectImage.sprite = target;
        }

        if (reelBackgroundImage != null)
        {
            Sprite target = toFreeGames ? reelBackgroundFreeGamesSprite : reelBackgroundNormalSprite;
            if (target != null) reelBackgroundImage.sprite = target;
        }
    }

    private void StartMultiplierPulse()
    {
        StopMultiplierPulse();
        if (multiplierValueText == null) return;

        multiplierValueText.transform.localScale = Vector3.one;
        multiplierPulseTween = multiplierValueText.transform
            .DOScale(multiplierPulseScale, multiplierPulseDuration)
            .SetEase(Ease.InOutSine)
            .SetLoops(-1, LoopType.Yoyo);
    }

    private void StopMultiplierPulse()
    {
        if (multiplierPulseTween != null)
        {
            multiplierPulseTween.Kill();
            multiplierPulseTween = null;
        }

        if (multiplierValueText != null)
        {
            multiplierValueText.transform.localScale = Vector3.one;
        }
    }

    private IEnumerator FadeGroup(CanvasGroup group, float targetAlpha)
    {
        if (group == null) yield break;

        group.gameObject.SetActive(true);
        yield return group.DOFade(targetAlpha, textFadeDuration).WaitForCompletion();

        if (Mathf.Approximately(targetAlpha, 0f)) group.gameObject.SetActive(false);
    }

    private void SetGroupAlpha(CanvasGroup group, float alpha, bool active)
    {
        if (group == null) return;

        group.DOKill();
        group.alpha = alpha;
        group.gameObject.SetActive(active);
    }

    private CanvasGroup GetGroupAt(List<GameObject> list, int index)
    {
        if (index < 0 || index >= list.Count) return null;
        GameObject go = list[index];
        return go != null ? go.GetComponent<CanvasGroup>() : null;
    }

    private void DestroyBoxes()
    {
        foreach (var go in spawnedBacks) { if (go != null) Destroy(go); }
        foreach (var go in spawnedFronts) { if (go != null) Destroy(go); }
        spawnedBacks.Clear();
        spawnedFronts.Clear();
    }

    // The prefab roots carry whatever position they sit at on the info page, so zero the offset
    // once instantiated — the slot's own position is what should place the box. Done in code
    // rather than by editing the prefabs, which would move them on the info page too.
    private void CenterInSlot(GameObject instance)
    {
        RectTransform rect = instance.transform as RectTransform;
        if (rect == null) return;

        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one * boxScale;
    }

    // These are expected on the prefabs so they can be configured in the Inspector (button
    // transition, hover/pressed sprites, etc.). One is still added as a fallback so a
    // half-configured prefab doesn't break the round, but it warns — a runtime-added Button only
    // ever gets Unity's defaults, so a warning here means the hover/pressed states are missing.
    private CanvasGroup EnsureCanvasGroup(GameObject go)
    {
        CanvasGroup group = go.GetComponent<CanvasGroup>();
        if (group == null)
        {
            Debug.LogWarning($"[FreeGameView] '{go.name}' has no CanvasGroup — add one to the prefab so its fade is authored rather than defaulted.", go);
            group = go.AddComponent<CanvasGroup>();
        }
        return group;
    }

    private Button EnsureButton(GameObject go)
    {
        Button button = go.GetComponent<Button>();
        if (button == null)
        {
            Debug.LogWarning($"[FreeGameView] '{go.name}' has no Button — add one to the prefab so its transition and hover/pressed states are authored rather than defaulted.", go);
            button = go.AddComponent<Button>();
        }
        button.interactable = true;
        return button;
    }

    private void StopActiveSequence()
    {
        if (activeSequence != null)
        {
            StopCoroutine(activeSequence);
            activeSequence = null;
        }
    }

    // The scene UI is built after this script, so missing references are expected for a while.
    // Warn once, then let every sequence no-op straight to its callback so the round still runs.
    private bool HasRequiredRefs()
    {
        if (frameRoot != null && frameRect != null) return true;

        if (!missingRefsLogged)
        {
            missingRefsLogged = true;
            Debug.LogWarning("[FreeGameView] Frame references are not wired — free games will run without their presentation.");
        }
        return false;
    }

    #endregion
}
