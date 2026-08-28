using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Presentation for the Free Games feature: the award prompt, the spins counter, and the closing
/// summary. See Assets/Scripts/MD/FreeGames.MD for the behaviour this implements.
///
/// View layer only. GameManager drives this via the public methods below and receives callbacks
/// when a sequence finishes — this script never calls back into the game loop (no RequestSpin, no
/// state changes). Attach to a GameObject that stays active for the whole session, NOT to the
/// counter or summary graphics themselves: deactivating those would halt these coroutines
/// mid-sequence.
///
/// The Mystery reveal is deliberately NOT here. It draws over the reels, between them landing and
/// the win animations, so it belongs to SlotView along with the rest of the reel presentation.
/// </summary>
public class FreeGameView : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIManager uiManager;

    [Header("Counter Graphic")]
    [Tooltip("The graphic carrying the counter text. One object, three states across a round: the " +
             "\"press start\" prompt, then the FREE GAME x OF y counter, then \"FEATURE COMPLETED\".")]
    [SerializeField] private GameObject counterRoot;
    [SerializeField] private CanvasGroup counterGroup;
    [SerializeField] private TMPro.TMP_Text counterText;

    [Header("Closing Summary")]
    [Tooltip("The graphic shown at the end of a round, holding the total-win counter.")]
    [SerializeField] private GameObject summaryRoot;
    [SerializeField] private CanvasGroup summaryGroup;
    [SerializeField] private TMPro.TMP_Text summaryWinText;

    [Header("Overlays")]
    [Tooltip("The 'top' parent holding the payout values. Faded to 0 and back during the closing sequence.")]
    [SerializeField] private CanvasGroup topGroup;
    [Tooltip("Dark overlay that sits behind the summary graphic, over the reels and background.")]
    [SerializeField] private CanvasGroup darkOverlayGroup;
    [SerializeField] private CanvasGroup fadeToBlackGroup;

    [Header("Background Swap")]
    [Tooltip("The SlotObject image — swapped for the whole feature, reverted on the closing fade.")]
    [SerializeField] private Image slotObjectImage;
    [SerializeField] private Sprite slotObjectNormalSprite;
    [SerializeField] private Sprite slotObjectFreeGamesSprite;
    [SerializeField] private Image reelBackgroundImage;
    [SerializeField] private Sprite reelBackgroundNormalSprite;
    [SerializeField] private Sprite reelBackgroundFreeGamesSprite;

    // Deliberately NOT [SerializeField]. These were serialized on the old view, which meant the
    // scene's saved values silently overrode any change made here — retuning in code appeared to do
    // nothing. Code is the single source of truth; the trade is that they need a recompile.
    private const float promptPulseAlpha = 0.25f;      // alpha the prompt text dips to
    private const float promptPulseDuration = 0.7f;
    private const float counterCountUpDuration = 1.0f;
    private const float totalWinCountUpDuration = 2.0f;
    private const float overlayFadeDuration = 0.5f;
    private const float darkOverlayAlpha = 0.75f;
    private const float summaryHoldBeforeCountUp = 0.3f;

    private const string PromptText = "PRESS START FEATURE BUTTON";
    private const string CompletedText = "FEATURE COMPLETED";

    private Coroutine activeSequence;
    private Tween promptPulseTween;
    private Tween counterTween;
    private Tween totalWinTween;
    private Action pendingTakeCallback;
    private bool missingRefsLogged;

    #region Public API — called by GameManager

    /// <summary>
    /// Trigger landed: show the counter graphic with the pulsing "press start" prompt. The Start
    /// button itself is UIManager's, so this only owns the text.
    /// </summary>
    internal void ShowAwardPrompt()
    {
        if (!HasRequiredRefs()) return;

        StopActiveSequence();
        SwapBackgrounds(true);

        SetGroupAlpha(counterGroup, 1f, true);
        if (counterRoot != null) counterRoot.SetActive(true);
        if (counterText != null) counterText.text = PromptText;

        StartPromptPulse();
    }

    /// <summary>
    /// Player pressed Start: stop the pulse and turn the prompt into the counter, with the total
    /// counting up from 0. Invokes onComplete once the count-up finishes, which is the cue to spin.
    /// </summary>
    internal void PlayCounterIntro(int total, Action onComplete)
    {
        if (!HasRequiredRefs())
        {
            onComplete?.Invoke();
            return;
        }

        StopActiveSequence();
        activeSequence = StartCoroutine(CounterIntroRoutine(total, onComplete));
    }

    /// <summary>Sets the counter with no animation. Called after every free spin.</summary>
    internal void UpdateCounter(int remaining, int total)
    {
        if (counterText == null) return;

        if (counterRoot != null) counterRoot.SetActive(true);
        counterText.text = FormatCounter(remaining, total);
    }

    /// <summary>
    /// Retrigger: animate the total up to its new value, the same way the opening sequence counts
    /// up from 0. The remaining count is already the post-retrigger figure and is shown at once.
    /// </summary>
    internal void AnimateTotalTo(int remaining, int fromTotal, int newTotal, Action onComplete)
    {
        if (!HasRequiredRefs() || counterText == null)
        {
            onComplete?.Invoke();
            return;
        }

        StopActiveSequence();
        activeSequence = StartCoroutine(CountTotalRoutine(remaining, fromTotal, newTotal, onComplete));
    }

    /// <summary>
    /// The closing sequence. onCountUpComplete fires when the total-win count-up finishes — that is
    /// the controller's cue to make Take pressable. onComplete fires after the player has taken the
    /// win and everything has faded out.
    /// </summary>
    internal void PlayOutroSequence(double roundWin, Action onCountUpComplete, Action onComplete)
    {
        if (!HasRequiredRefs())
        {
            ResetToDefault();
            onCountUpComplete?.Invoke();
            onComplete?.Invoke();
            return;
        }

        StopActiveSequence();
        activeSequence = StartCoroutine(OutroRoutine(roundWin, onCountUpComplete, onComplete));
    }

    /// <summary>Invoked by UIManager when the player presses Take on the closing summary.</summary>
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

        if (counterTween != null) { counterTween.Kill(); counterTween = null; }
        if (totalWinTween != null) { totalWinTween.Kill(); totalWinTween = null; }

        SwapBackgrounds(false);

        if (counterRoot != null) counterRoot.SetActive(false);
        if (summaryRoot != null) summaryRoot.SetActive(false);

        SetGroupAlpha(counterGroup, 0f, false);
        SetGroupAlpha(summaryGroup, 0f, false);
        SetGroupAlpha(darkOverlayGroup, 0f, false);
        SetGroupAlpha(fadeToBlackGroup, 0f, false);
        SetGroupAlpha(topGroup, 1f, true);

        pendingTakeCallback = null;
    }

    #endregion

    #region Intro / counter

    private IEnumerator CounterIntroRoutine(int total, Action onComplete)
    {
        StopPromptPulse();
        SetGroupAlpha(counterGroup, 1f, true);

        yield return CountTotal(total, 0, total);

        activeSequence = null;
        onComplete?.Invoke();
    }

    private IEnumerator CountTotalRoutine(int remaining, int fromTotal, int newTotal, Action onComplete)
    {
        yield return CountTotal(remaining, fromTotal, newTotal);

        activeSequence = null;
        onComplete?.Invoke();
    }

    // Shared by the opening count-up (0 -> total) and a retrigger (old total -> new total).
    private IEnumerator CountTotal(int remaining, int fromTotal, int toTotal)
    {
        if (counterText == null) yield break;

        if (counterRoot != null) counterRoot.SetActive(true);

        bool done = false;
        if (counterTween != null) counterTween.Kill();

        counterTween = DOVirtual.Int(fromTotal, toTotal, counterCountUpDuration, value =>
        {
            if (counterText != null) counterText.text = FormatCounter(remaining, value);
        }).OnComplete(() =>
        {
            if (counterText != null) counterText.text = FormatCounter(remaining, toTotal);
            counterTween = null;
            done = true;
        });

        yield return new WaitUntil(() => done);
    }

    // Single definition of the counter's wording, so the intro, the per-spin update and a retrigger
    // can never drift apart.
    private static string FormatCounter(int remaining, int total)
    {
        return $"FREE GAME  {remaining}  OF  {total}";
    }

    private void StartPromptPulse()
    {
        StopPromptPulse();
        if (counterGroup == null) return;

        counterGroup.alpha = 1f;
        promptPulseTween = counterGroup
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

        if (counterGroup != null) counterGroup.alpha = 1f;
    }

    #endregion

    #region Outro

    private IEnumerator OutroRoutine(double roundWin, Action onCountUpComplete, Action onComplete)
    {
        // 1. Dark overlay up and the payout values out. This lands BEFORE the text changes, which
        //    looks mistimed but is the intended order — see FreeGames.MD.
        if (darkOverlayGroup != null) darkOverlayGroup.gameObject.SetActive(true);
        Tween overlayIn = darkOverlayGroup != null ? darkOverlayGroup.DOFade(darkOverlayAlpha, overlayFadeDuration) : null;
        Tween topOut = topGroup != null ? topGroup.DOFade(0f, overlayFadeDuration) : null;

        if (topOut != null) yield return topOut.WaitForCompletion();
        else if (overlayIn != null) yield return overlayIn.WaitForCompletion();
        else yield return new WaitForSeconds(overlayFadeDuration);

        // 2. Everything fades back in.
        if (topGroup != null) yield return topGroup.DOFade(1f, overlayFadeDuration).WaitForCompletion();

        // 3. The counter becomes the completion notice.
        if (counterText != null) counterText.text = CompletedText;
        SetGroupAlpha(counterGroup, 1f, true);

        // 4. The summary graphic appears.
        if (summaryRoot != null) summaryRoot.SetActive(true);
        SetGroupAlpha(summaryGroup, 1f, true);
        if (summaryWinText != null) summaryWinText.text = 0d.ToString(SpriteTextFormatter.MoneyFormat);

        yield return new WaitForSeconds(summaryHoldBeforeCountUp);

        // 5. The round's total counts up.
        bool countUpDone = false;
        if (summaryWinText != null)
        {
            if (totalWinTween != null) totalWinTween.Kill();

            totalWinTween = DOVirtual.Float(0f, (float)roundWin, totalWinCountUpDuration, value =>
            {
                if (summaryWinText != null) summaryWinText.text = value.ToString(SpriteTextFormatter.MoneyFormat);
            }).OnComplete(() =>
            {
                if (summaryWinText != null) summaryWinText.text = roundWin.ToString(SpriteTextFormatter.MoneyFormat);
                totalWinTween = null;
                countUpDone = true;
            });

            yield return new WaitUntil(() => countUpDone);
        }

        // 6. Count-up finished — the controller turns the button into a pressable Take.
        bool takePressed = false;
        pendingTakeCallback = () => takePressed = true;
        onCountUpComplete?.Invoke();

        // 7. The summary holds until the player takes the win.
        yield return new WaitUntil(() => takePressed);

        // 8. Everything free-games fades out together, the completion notice included.
        yield return FadeOutRoundElements();

        activeSequence = null;
        onComplete?.Invoke();
    }

    private IEnumerator FadeOutRoundElements()
    {
        SwapBackgrounds(false);

        Tween summaryOut = summaryGroup != null ? summaryGroup.DOFade(0f, overlayFadeDuration) : null;
        Tween counterOut = counterGroup != null ? counterGroup.DOFade(0f, overlayFadeDuration) : null;
        Tween overlayOut = darkOverlayGroup != null ? darkOverlayGroup.DOFade(0f, overlayFadeDuration) : null;

        if (summaryOut != null) yield return summaryOut.WaitForCompletion();
        else if (counterOut != null) yield return counterOut.WaitForCompletion();
        else if (overlayOut != null) yield return overlayOut.WaitForCompletion();
        else yield return new WaitForSeconds(overlayFadeDuration);

        if (summaryRoot != null) summaryRoot.SetActive(false);
        if (counterRoot != null) counterRoot.SetActive(false);
        if (darkOverlayGroup != null) darkOverlayGroup.gameObject.SetActive(false);

        SetGroupAlpha(topGroup, 1f, true);
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

    // The scene UI is built after this script, so missing references are expected for a while.
    // Warn once, then let every sequence no-op straight to its callback so the round still runs.
    private bool HasRequiredRefs()
    {
        if (counterRoot != null && counterText != null) return true;

        if (!missingRefsLogged)
        {
            missingRefsLogged = true;
            Debug.LogWarning("[FreeGameView] Counter references are not wired — free games will run without their presentation.");
        }
        return false;
    }

    #endregion
}
