using System;
using System.Collections;
using UnityEngine;
using DG.Tweening;

/// <summary>
/// Presentation for the Free Games feature: the award prompt, the spins counter, and the closing
/// summary. See Assets/Scripts/MD/FreeGames.MD for the behaviour this implements.
///
/// View layer only. GameManager drives this via the public methods below and receives callbacks
/// when a sequence finishes — this script never calls back into the game loop (no RequestSpin, no
/// state changes). Attach to a GameObject that stays active for the whole session, NOT to
/// FreeGamesTexts or FreeGamesOver themselves: deactivating those would halt these coroutines
/// mid-sequence.
///
/// The Mystery reveal is deliberately NOT here. It draws over the reels, between them landing and
/// the win animations, so it belongs to SlotView along with the rest of the reel presentation.
/// </summary>
public class FreeGameView : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private UIManager uiManager;

    [Header("Counter Panel")]
    [Tooltip("The FreeGamesTexts panel. Holds all three states below and fades out as one at the " +
             "end of the round.")]
    [SerializeField] private GameObject freeGamesTexts;
    [SerializeField] private CanvasGroup freeGamesTextsGroup;

    [Tooltip("The \"PRESS START FEATURE BUTTON\" graphic. Its own CanvasGroup, because the pulse " +
             "is on this alone rather than the whole panel.")]
    [SerializeField] private GameObject pressStartFeature;
    [SerializeField] private CanvasGroup pressStartFeatureGroup;

    [Tooltip("The \"FREE SPINS COMPLETED\" graphic shown once the round ends.")]
    [SerializeField] private GameObject featureCompleted;

    [Tooltip("The FreeGamesRemaining parent. Its static \"FREE GAMES ... OF ...\" label needs no " +
             "reference — only the two numbers are written.")]
    [SerializeField] private GameObject freeGamesRemaining;
    [SerializeField] private TMPro.TMP_Text remainingFreeSpins;
    [SerializeField] private TMPro.TMP_Text totalFreeSpins;

    [Header("Closing Summary")]
    [Tooltip("The graphic shown at the end of a round, holding the total-win counter.")]
    [SerializeField] private GameObject freeGamesOver;
    [SerializeField] private CanvasGroup freeGamesOverGroup;
    [SerializeField] private TMPro.TMP_Text freeGamesWinAmount;
    [Tooltip("Optional clip on the summary graphic. Started when the summary appears and stopped " +
             "when it fades. Its frames, speed and loop flag are the component's own — unlike the " +
             "symbol animations, the code does not own this clip and only starts and stops it.")]
    [SerializeField] private ImageAnimation freeGamesOverAnim;

    [Header("Overlays")]
    [Tooltip("The 'top' parent holding the payout values. Faded to 0 and back during the closing sequence.")]
    [SerializeField] private CanvasGroup topGroup;
    [Tooltip("Dark overlay that sits behind the summary graphic, over the reels and background.")]
    [SerializeField] private CanvasGroup darkOverlayGroup;
    [SerializeField] private CanvasGroup fadeToBlackGroup;

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

    // No wording lives here any more. The prompt and the completion notice are baked into their own
    // graphics and the counter's "FREE GAMES ... OF ..." label is static, so this script only ever
    // toggles which of the three is visible and writes the two numbers.

    private Coroutine activeSequence;
    private Tween promptPulseTween;
    private Tween counterTween;
    private Tween totalWinTween;
    private Action pendingTakeCallback;
    private bool missingRefsLogged;

    #region Public API — called by GameManager

    /// <summary>
    /// Trigger landed: show FreeGamesTexts with PressStartFeature pulsing. The Start
    /// button itself is UIManager's, so this only owns the text.
    /// </summary>
    internal void ShowAwardPrompt()
    {
        if (!HasRequiredRefs()) return;

        StopActiveSequence();

        SetGroupAlpha(freeGamesTextsGroup, 1f, true);
        if (freeGamesTexts != null) freeGamesTexts.SetActive(true);
        ShowPanelState(prompt: true, remaining: false, completed: false);

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
        if (freeGamesTexts != null) freeGamesTexts.SetActive(true);
        ShowPanelState(prompt: false, remaining: true, completed: false);

        if (remainingFreeSpins != null) remainingFreeSpins.text = remaining.ToString();
        if (totalFreeSpins != null) totalFreeSpins.text = total.ToString();
    }

    /// <summary>
    /// Retrigger: animate the total up to its new value, the same way the opening sequence counts
    /// up from 0. The remaining count is already the post-retrigger figure and is shown at once.
    /// </summary>
    internal void AnimateTotalTo(int remaining, int fromTotal, int newTotal, Action onComplete)
    {
        if (!HasRequiredRefs() || totalFreeSpins == null)
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

        if (freeGamesOverAnim != null) freeGamesOverAnim.StopAnimation();

        ShowPanelState(prompt: false, remaining: false, completed: false);
        if (freeGamesTexts != null) freeGamesTexts.SetActive(false);
        if (freeGamesOver != null) freeGamesOver.SetActive(false);

        if (pressStartFeatureGroup != null) pressStartFeatureGroup.alpha = 1f;
        SetGroupAlpha(freeGamesTextsGroup, 0f, false);
        SetGroupAlpha(freeGamesOverGroup, 0f, false);
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
        SetGroupAlpha(freeGamesTextsGroup, 1f, true);

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

    // Shared by the opening count-up (0 -> total) and a retrigger (old total -> new total). Only
    // the total animates; the remaining count is already its final figure and is set once.
    private IEnumerator CountTotal(int remaining, int fromTotal, int toTotal)
    {
        if (freeGamesTexts != null) freeGamesTexts.SetActive(true);
        ShowPanelState(prompt: false, remaining: true, completed: false);

        if (remainingFreeSpins != null) remainingFreeSpins.text = remaining.ToString();

        if (totalFreeSpins == null) yield break;

        bool done = false;
        if (counterTween != null) counterTween.Kill();

        counterTween = DOVirtual.Int(fromTotal, toTotal, counterCountUpDuration, value =>
        {
            if (totalFreeSpins != null) totalFreeSpins.text = value.ToString();
        }).OnComplete(() =>
        {
            if (totalFreeSpins != null) totalFreeSpins.text = toTotal.ToString();
            counterTween = null;
            done = true;
        });

        yield return new WaitUntil(() => done);
    }

    // The panel's three states are mutually exclusive, so they are always set together rather than
    // toggled individually — that way no combination of calls can leave two of them showing.
    private void ShowPanelState(bool prompt, bool remaining, bool completed)
    {
        if (pressStartFeature != null) pressStartFeature.SetActive(prompt);
        if (freeGamesRemaining != null) freeGamesRemaining.SetActive(remaining);
        if (featureCompleted != null) featureCompleted.SetActive(completed);
    }

    // Pulses the prompt alone, not the whole panel — the panel's own group is reserved for the
    // fade-out at the end of the round.
    private void StartPromptPulse()
    {
        StopPromptPulse();
        if (pressStartFeatureGroup == null) return;

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

        // 3. The counter gives way to the completion notice.
        ShowPanelState(prompt: false, remaining: false, completed: true);
        SetGroupAlpha(freeGamesTextsGroup, 1f, true);

        // 4. FreeGamesOver appears, and its clip starts with it. Started explicitly rather than
        //    left to the component's StartOnEnable, so the sequence owns the timing and a change
        //    to that checkbox cannot silently turn the animation off.
        if (freeGamesOver != null) freeGamesOver.SetActive(true);
        SetGroupAlpha(freeGamesOverGroup, 1f, true);
        if (freeGamesOverAnim != null) freeGamesOverAnim.StartAnimation();
        if (freeGamesWinAmount != null) freeGamesWinAmount.text = 0d.ToString(SpriteTextFormatter.MoneyFormat);

        yield return new WaitForSeconds(summaryHoldBeforeCountUp);

        // 5. The round's total counts up.
        bool countUpDone = false;
        if (freeGamesWinAmount != null)
        {
            if (totalWinTween != null) totalWinTween.Kill();

            totalWinTween = DOVirtual.Float(0f, (float)roundWin, totalWinCountUpDuration, value =>
            {
                if (freeGamesWinAmount != null) freeGamesWinAmount.text = value.ToString(SpriteTextFormatter.MoneyFormat);
            }).OnComplete(() =>
            {
                if (freeGamesWinAmount != null) freeGamesWinAmount.text = roundWin.ToString(SpriteTextFormatter.MoneyFormat);
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
        Tween summaryOut = freeGamesOverGroup != null ? freeGamesOverGroup.DOFade(0f, overlayFadeDuration) : null;
        Tween counterOut = freeGamesTextsGroup != null ? freeGamesTextsGroup.DOFade(0f, overlayFadeDuration) : null;
        Tween overlayOut = darkOverlayGroup != null ? darkOverlayGroup.DOFade(0f, overlayFadeDuration) : null;

        if (summaryOut != null) yield return summaryOut.WaitForCompletion();
        else if (counterOut != null) yield return counterOut.WaitForCompletion();
        else if (overlayOut != null) yield return overlayOut.WaitForCompletion();
        else yield return new WaitForSeconds(overlayFadeDuration);

        // Stopped explicitly: ImageAnimation drives itself with Invoke, so deactivating the object
        // is not a reliable way to end a looping clip.
        if (freeGamesOverAnim != null) freeGamesOverAnim.StopAnimation();

        if (freeGamesOver != null) freeGamesOver.SetActive(false);
        ShowPanelState(prompt: false, remaining: false, completed: false);
        if (freeGamesTexts != null) freeGamesTexts.SetActive(false);
        if (darkOverlayGroup != null) darkOverlayGroup.gameObject.SetActive(false);

        SetGroupAlpha(topGroup, 1f, true);
    }

    #endregion

    #region Helpers

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
        if (freeGamesTexts != null && freeGamesRemaining != null) return true;

        if (!missingRefsLogged)
        {
            missingRefsLogged = true;
            Debug.LogWarning("[FreeGameView] Counter references are not wired — free games will run without their presentation.");
        }
        return false;
    }

    #endregion
}
