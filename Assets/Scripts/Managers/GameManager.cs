using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] internal SocketIOManager socketManager;
    [SerializeField] internal UIManager uiManager;
    [SerializeField] private PopupManager popupManager;
    [SerializeField] private SlotView slotView;
    [SerializeField] private FreeGameView freeGameView;

    [Header("Spin Settings")]
    [SerializeField] private float normalSpinDuration = 3.5f;
    [SerializeField] private float turboSpinDuration = 2.0f;
    [SerializeField] private float quickSpinCycleDuration = 0.1f;

    [Header("Free Games Timing")]
    [Tooltip("How long the scatters animate before the award prompt or, on a retrigger, before the counter climbs.")]
    [SerializeField] private float scatterTriggerHold = 3.5f;
    [Tooltip("Scatter animation loops on a retrigger. The initial trigger uses 0 (runs until the first free spin starts) because the player controls when that ends.")]
    [SerializeField] private int scatterTriggerLoops = 2;

    [Header("Win Settings")]
    [SerializeField] private double bigWinMultiplierThreshold = 500.0;
    public double BigWinMultiplierThreshold => bigWinMultiplierThreshold;

    internal GameConfig gameConfig;
    internal PlayerData playerData;
    internal SpinResult lastResult;

    internal GameState currentState;
    internal SpinSpeed currentSpinSpeed;

    internal int currentBetIndex;
    internal double currentBetAmount;

    internal bool isAutoPlaying;
    internal int autoPlayTotalRounds;
    internal int autoPlayRemainingRounds;
    internal bool wasAutoPlayingBeforeFreeSpins;
    internal int savedAutoPlayRemainingRounds;
    internal int savedAutoPlayTotalRounds;

    internal bool isInFreeSpins;
    internal int freeSpinsRemaining;      // server-authoritative, already decremented for this spin
    internal int freeSpinsUsed;           // counted here — one per free spin actually played
    internal double freeSpinsRoundWin;    // server-authoritative, from features.freeGame.totalRoundWin

    // Total spins the round has awarded, including every retrigger. Derived rather than tracked:
    // the server never sends an award size, but used + remaining is always the total, and it
    // self-corrects if a response is ever missed.
    internal int FreeSpinsTotalAwarded => freeSpinsUsed + freeSpinsRemaining;

    // The total the counter was showing before a retrigger landed, so its count-up has somewhere to
    // start from. -1 when no retrigger is pending presentation.
    private int retriggerTotalBefore = -1;

    internal bool isInitialized;
    internal bool initializationFailed;

    private Coroutine spinCoroutine;
    private bool stopRequested;
    private bool waitingForSpecialWin;

    #region Initialization

    private void Start()
    {
        currentState = GameState.Initializing;
        currentSpinSpeed = SpinSpeed.Normal;
        isInitialized = false;
        initializationFailed = false;
    }

    internal void OnInitDataReceived(GameConfig config, PlayerData player, List<List<int>> initialMatrix)
    {
        gameConfig = config;
        playerData = player;
        currentBetIndex = playerData.currentBetIndex;
        UpdateBetAmount();

        if (initialMatrix != null && slotView != null)
        {
            slotView.SetInitialMatrix(initialMatrix);
        }

        isInitialized = true;
        currentState = GameState.Idle;

        uiManager.OnGameInitialized();
    }

    #endregion

    #region Bet Management

    internal void IncreaseBet()
    {
        if (currentState != GameState.Idle || isAutoPlaying) return;
        if (gameConfig == null || gameConfig.availableBets == null || gameConfig.availableBets.Count == 0) return;

        int maxIndex = gameConfig.availableBets.Count - 1;
        int nextIndex = currentBetIndex + 1;
        if (nextIndex > maxIndex)
        {
            nextIndex = 0;
        }

        if (nextIndex == maxIndex)
        {
            AudioManager.Instance?.PlayMaxBetReached();
        }
        else
        {
            AudioManager.Instance?.PlayBetPlusMinus();
        }

        SetBetIndex(nextIndex);
    }

    internal void DecreaseBet()
    {
        if (currentState != GameState.Idle || isAutoPlaying) return;
        if (gameConfig == null || gameConfig.availableBets == null || gameConfig.availableBets.Count == 0) return;

        int maxIndex = gameConfig.availableBets.Count - 1;
        int nextIndex = currentBetIndex - 1;
        if (nextIndex < 0)
        {
            nextIndex = maxIndex;
        }

        if (nextIndex == maxIndex)
        {
            AudioManager.Instance?.PlayMaxBetReached();
        }
        else
        {
            AudioManager.Instance?.PlayBetPlusMinus();
        }

        SetBetIndex(nextIndex);
    }

    internal void SetBetIndex(int index)
    {
        currentBetIndex = index;
        UpdateBetAmount();
        uiManager.UpdateBetDisplay();
        if (slotView != null) slotView.OnBetChanged();
    }

    private void UpdateBetAmount()
    {
        currentBetAmount = gameConfig.availableBets[currentBetIndex];
    }

    #endregion

    #region Spin Control
    
    internal void RequestSpin()
    {
        if (currentState != GameState.Idle) return;
        if (!socketManager.isConnected) return;

        double totalPay = GetTotalPay();
        if (!isInFreeSpins && playerData.balance < totalPay)
        {
            if (popupManager != null)
            {
                popupManager.ShowInsufficientFundsError();
            }
            return;
        }

        StartSpin();
    }

    internal void RequestStop()
    {
        if (currentState == GameState.Spinning)
        {
            if (isAutoPlaying)
            {
                StopAutoPlay();
            }
            else if (!isInFreeSpins)
            {
                stopRequested = true;
                uiManager.SetSpinStopButtonStates(isSpinningState: true, isInteractable: false);
            }
        }
    }

    private void StartSpin()
    {
        if (lastResult != null)
        {
            ProcessSpinResult();
        }

        lastResult = null;
        currentState = GameState.Spinning;
        stopRequested = false;

        // Deduct total pay from balance on spin start (except in free spins)
        if (!isInFreeSpins)
        {
            playerData.balance -= GetTotalPay();
            if (playerData.balance < 0) playerData.balance = 0;
        }

        uiManager.OnSpinStarted();

        if (slotView != null)
        {
            slotView.StartSpin();
        }

        socketManager.SendSpinRequest(currentBetIndex, isInFreeSpins);

        if (spinCoroutine != null)
            StopCoroutine(spinCoroutine);
        spinCoroutine = StartCoroutine(SpinRoutine());
    }

    private IEnumerator SpinRoutine()
    {
        float spinDuration = GetSpinDuration();
        float elapsed = 0f;

        while (elapsed < spinDuration && !stopRequested)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        // Player pressed Stop manually — hold for 0.5s so the reels keep
        // spinning briefly before snapping, giving clear visual feedback.
        if (stopRequested)
        {
            yield return new WaitForSeconds(0.5f);
        }

        while (lastResult == null)
        {
            yield return null;
        }

        currentState = GameState.Stopping;

        if (slotView != null && lastResult.resultMatrix != null)
        {
            if (currentSpinSpeed == SpinSpeed.QuickSpin || stopRequested)
            {
                slotView.QuickStop(lastResult.resultMatrix);

                // Wait for the snap animation to settle before processing result
                float quickStopWaitTime = 0.5f;
                yield return new WaitForSeconds(quickStopWaitTime);

                OnReelsStoppedComplete();
            }
            else
            {
                slotView.StopSpin(lastResult.resultMatrix, OnReelsStoppedComplete);
            }
        }
        else
        {
            OnReelsStoppedComplete();
        }
    }

    private void OnReelsStoppedComplete()
    {
        // Safety net. StopSpinSequence already cuts the loop at the exact landing moment, but it is
        // bypassed entirely when SlotView has no reels or no result matrix to stop onto. Without this
        // the loop would run until the next spin restarted it. No-ops when already stopped.
        AudioManager.Instance?.StopSpinLoop();

        if (lastResult != null)
        {
            playerData = new PlayerData
            {
                balance = lastResult.playerData != null ? lastResult.playerData.balance : 0,
                currentBetIndex = lastResult.playerData != null ? lastResult.playerData.currentBetIndex : currentBetIndex
            };
        }

        // Mystery symbols open before anything else is presented. The reveal has to finish for
        // every cell before the win animations start, so the rest of this runs from its callback.
        // Spins with no Mystery fall straight through.
        if (slotView != null && lastResult != null && lastResult.mysteryPositions != null && lastResult.mysteryPositions.Count > 0)
        {
            slotView.PlayMysteryReveal(lastResult.mysteryPositions, PresentSpinOutcome);
        }
        else
        {
            PresentSpinOutcome();
        }
    }

    // Everything that happens once the board is final — after the Mystery reveal, if there was one.
    private void PresentSpinOutcome()
    {
        if (lastResult != null && lastResult.winAmount > 0 && lastResult.winLines != null && lastResult.winLines.Count > 0)
        {
            double totalPay = GetTotalPay();
            double multiplier = totalPay > 0 ? (lastResult.winAmount / totalPay) : 0;

            if (multiplier >= bigWinMultiplierThreshold)
            {
                uiManager.DisableControlsDuringWinAnimation();
                currentState = GameState.Idle;
                slotView.ShowWinLineAnimation(lastResult.winLines, OnWinAnimationComplete);
                StartCoroutine(TriggerWinPopupWithDelay(1.5f, lastResult));
            }
            else
            {
                // For normal wins, trigger UI update immediately and enable controls
                uiManager.OnSpinStopping(lastResult);
                uiManager.EnableControlsAfterWinAnimation();
                uiManager.OnSpinCompleted(lastResult);
                currentState = GameState.Idle;
                slotView.ShowWinLineAnimation(lastResult.winLines, OnWinAnimationComplete);
            }
        }
        else
        {
            uiManager.OnSpinStopping(lastResult);
            currentState = GameState.Idle;
            OnWinAnimationComplete();
        }
    }

    private IEnumerator TriggerWinPopupWithDelay(float delay, SpinResult result)
    {
        double totalPay = GetTotalPay();
        double multiplier = totalPay > 0 ? (result.winAmount / totalPay) : 0;
        if (multiplier < bigWinMultiplierThreshold)
        {
            waitingForSpecialWin = false;
            yield break;
        }

        waitingForSpecialWin = true;

        yield return new WaitForSeconds(delay);

        if (lastResult == result && multiplier >= bigWinMultiplierThreshold)
        {
            uiManager.TriggerBigWinPopup(result, () =>
            {
                waitingForSpecialWin = false;
            });
        }
        else
        {
            waitingForSpecialWin = false;
        }
    }

    private void OnWinAnimationComplete()
    {
        if (lastResult != null)
        {
            double totalPay = GetTotalPay();
            double multiplier = totalPay > 0 ? (lastResult.winAmount / totalPay) : 0;

            // Only update UI here if it wasn't already updated in OnReelsStoppedComplete (multiplier < bigWinMultiplierThreshold)
            if (multiplier >= bigWinMultiplierThreshold)
            {
                uiManager.OnSpinStopping(lastResult);
            }
        }

        StartCoroutine(ProcessSpecialFeaturesAfterWin());
    }

    private IEnumerator ProcessSpecialFeaturesAfterWin()
    {
        // Wait for special win popup to finish before starting special features
        while (waitingForSpecialWin || uiManager.IsSpecialWinActive)
        {
            yield return null;
        }

        // The initial trigger — a paid base spin that awarded spins. Its scatter sequence runs
        // before the round is entered.
        if (lastResult != null && lastResult.freeGame != null
            && lastResult.freeGame.spinsAwarded && !lastResult.freeGame.isFreeGame)
        {
            yield return StartCoroutine(DelayScatterTriggerResult());
            yield break;
        }

        // A retrigger — same scatter sequence, then the counter's total climbs to its new figure.
        // No prompt and no Start button; the round simply carries on.
        if (isInFreeSpins && retriggerTotalBefore >= 0)
        {
            yield return StartCoroutine(PlayRetriggerSequence());
        }

        ResumeAfterSpecialFeature();
    }

    private IEnumerator PlayRetriggerSequence()
    {
        int fromTotal = retriggerTotalBefore;
        retriggerTotalBefore = -1;


        AudioManager.Instance?.Play3UspinWinLineLoop();
        if (slotView != null) slotView.AnimateAllScatters(scatterTriggerLoops);

        yield return new WaitForSeconds(scatterTriggerHold);

        if (freeGameView == null) yield break;

        bool countUpDone = false;
        freeGameView.AnimateTotalTo(freeSpinsRemaining, fromTotal, FreeSpinsTotalAwarded, () => countUpDone = true);
        yield return new WaitUntil(() => countUpDone);
    }

    private void ResumeAfterSpecialFeature()
    {
        if (isAutoPlaying || isInFreeSpins)
        {
            StartCoroutine(DelayBeforeNextRound());
        }
        else
        {
            ProcessSpinResult();
        }
    }

    private IEnumerator DelayScatterTriggerResult()
    {
        // Play special feature trigger sound AFTER all reels have stopped
        AudioManager.Instance?.Play3UspinWinLineLoop();

        // Animate the scatters indefinitely (0 = no self-stop) so they keep playing behind the
        // award prompt while the player decides to press Start. The first free spin's StartSpin
        // stops them.
        slotView.AnimateAllScatters(0);

        // Wait for scatter hit animations to play
        yield return new WaitForSeconds(scatterTriggerHold);
        ProcessSpinResult();
    }

    private IEnumerator DelayBeforeNextRound()
    {
        float delayTime = currentSpinSpeed == SpinSpeed.QuickSpin ? 0.3f : 0.5f;
        yield return new WaitForSeconds(delayTime);

        // Wait for special win popup using the flag and active state
        while (waitingForSpecialWin || uiManager.IsSpecialWinActive)
        {
            yield return null;
        }

        ProcessSpinResult();
    }

    private float GetSpinDuration()
    {
        return currentSpinSpeed switch
        {
            SpinSpeed.Normal => normalSpinDuration,
            SpinSpeed.Turbo => turboSpinDuration,
            SpinSpeed.QuickSpin => quickSpinCycleDuration,
            _ => normalSpinDuration
        };
    }

    internal void OnSpinResultReceived(SpinResult result)
    {
        lastResult = result;

        // The result is not handed to SlotView here. It writes the display-block sprites itself
        // when each reel lands, in StopSingleReel — in the same frame as the landing position
        // snap, so the swap is never on screen. An earlier "preload" wrote them mid-spin as well,
        // on a hand-tuned delay; it duplicated the landing write, was visible whenever the delay
        // missed its narrow window, and telegraphed the result each time the icons swept back
        // through the reel. Removed rather than retuned.

        // Update the round's numbers as soon as the response lands so the displays never lag the
        // reels. A retrigger needs no special handling: the server has already folded the extra
        // spins into spinsRemaining, so the count simply goes up instead of down.
        if (isInFreeSpins && result.freeGame != null)
        {
            int totalBefore = FreeSpinsTotalAwarded;

            freeSpinsUsed++;
            freeSpinsRemaining = result.freeGame.spinsRemaining;
            freeSpinsRoundWin = result.freeGame.roundWin;

            // A retrigger animates the total up to its new figure; an ordinary spin just sets the
            // counter. The retrigger's own count-up is started later, once the scatters have
            // animated — this only records what it will count from.
            if (result.freeGame.spinsAwarded)
            {
                retriggerTotalBefore = totalBefore;
            }
            else if (freeGameView != null)
            {
                freeGameView.UpdateCounter(freeSpinsRemaining, FreeSpinsTotalAwarded);
            }
        }
    }

    private void ProcessSpinResult()
    {
        playerData = lastResult.playerData;

        uiManager.OnSpinCompleted(lastResult);

        // A trigger is a spin that awarded spins while not itself being a free spin — the awarding
        // spin is an ordinary paid base spin. A retrigger has spinsAwarded set too, but with
        // isFreeGame true, and needs nothing here: the extra spins are already in spinsRemaining.
        FreeGameData freeGame = lastResult.freeGame;
        if (!isInFreeSpins && freeGame != null && freeGame.spinsAwarded && !freeGame.isFreeGame)
        {
            StartFreeSpins(freeGame.spinsRemaining);
            lastResult = null;
            return;
        }

        lastResult = null;

        if (isAutoPlaying && !isInFreeSpins)
        {
            if (autoPlayTotalRounds != -1)
            {
                autoPlayRemainingRounds--;
            }

            uiManager.UpdateAutoPlayCount();

            if (autoPlayTotalRounds != -1 && autoPlayRemainingRounds <= 0)
            {
                currentState = GameState.Idle;
                StopAutoPlay();
            }
            else
            {
                // Before requesting the next spin, verify the player can still afford it.
                // If not, stop autoplay (restores all UI) then show the popup.
                double totalPay = GetTotalPay();
                if (playerData.balance < totalPay)
                {
                    currentState = GameState.Idle;
                    StopAutoPlay();
                    if (popupManager != null) popupManager.ShowInsufficientFundsError();
                }
                else
                {
                    currentState = GameState.Idle;
                    RequestSpin();
                }
            }
        }
        else if (isInFreeSpins)
        {
            // Counters were updated in OnSpinResultReceived. spinsRemaining is the count *after*
            // this spin, so zero means the round is done — there is no separate round-over flag.
            if (freeSpinsRemaining <= 0)
            {
                EndFreeSpins();
            }
            else
            {
                currentState = GameState.Idle;
                StartCoroutine(DelayBeforeNextFreeSpin());
            }
        }
        else
        {
            currentState = GameState.Idle;
        }
    }

    #endregion

    #region Spin Speed Control

    internal void SetSpinSpeed(SpinSpeed speed)
    {
        currentSpinSpeed = speed;
    }

    #endregion



    #region Auto Play

    internal void StartAutoPlay(int rounds)
    {
        if (currentState != GameState.Idle) return;

        // Check balance BEFORE locking any UI — if insufficient, show popup and bail.
        double totalPay = GetTotalPay();
        if (playerData.balance < totalPay)
        {
            if (popupManager != null) popupManager.ShowInsufficientFundsError();
            return;
        }

        isAutoPlaying = true;
        autoPlayTotalRounds = rounds;
        autoPlayRemainingRounds = rounds;
        wasAutoPlayingBeforeFreeSpins = false;

        uiManager.OnAutoPlayStarted();
        RequestSpin();
    }

    internal void StopAutoPlay()
    {
        isAutoPlaying = false;
        autoPlayRemainingRounds = 0;
        wasAutoPlayingBeforeFreeSpins = false;

        uiManager.OnAutoPlayStopped();

        // Autoplay skips the per-line cycle while it runs, so the round it just finished is parked
        // after Phase 1. Now that no further spin is coming, present it the way a manual spin would.
        // Covers both endings: the last scheduled round, and the player stopping part-way.
        if (!isInFreeSpins && slotView != null) slotView.PlayWinLineCycle();
    }

    internal bool ShouldResumeAutoPlay()
    {
        return wasAutoPlayingBeforeFreeSpins && (savedAutoPlayTotalRounds == -1 || savedAutoPlayRemainingRounds > 0);
    }

    internal void ResumeAutoPlay()
    {
        if (!ShouldResumeAutoPlay()) return;

        int remaining = savedAutoPlayRemainingRounds;
        int total = savedAutoPlayTotalRounds;
        wasAutoPlayingBeforeFreeSpins = false;

        if (currentState != GameState.Idle) return;

        double totalPay = GetTotalPay();
        if (playerData.balance < totalPay)
        {
            if (popupManager != null) popupManager.ShowInsufficientFundsError();
            return;
        }

        isAutoPlaying = true;
        autoPlayTotalRounds = total;
        autoPlayRemainingRounds = remaining;

        uiManager.OnAutoPlayStarted();
        RequestSpin();
    }

    #endregion

    #region Free Spins

    // Entered from a base spin that awarded spins. There is no pick and no player choice over the
    // prize — Golden Dynasty awards a flat count on 3+ scatters — but the player does choose when
    // the round begins, via the Start button that replaces Spin.
    private void StartFreeSpins(int spins)
    {
        isInFreeSpins = true;
        freeSpinsRemaining = spins;
        freeSpinsUsed = 0;
        freeSpinsRoundWin = 0;
        retriggerTotalBefore = -1;

        AudioManager.Instance?.PlayFreeSpinBg();

        int prevTotal = autoPlayTotalRounds;
        int prevRemaining = autoPlayRemainingRounds;

        if (isAutoPlaying)
        {
            StopAutoPlay();
            wasAutoPlayingBeforeFreeSpins = true;
            savedAutoPlayTotalRounds = prevTotal;
            savedAutoPlayRemainingRounds = (prevTotal != -1) ? (prevRemaining - 1) : -1;
        }

        // The prompt pulses until the player acts; the scatters keep animating underneath it,
        // started by DelayScatterTriggerResult and stopped by the first free spin.
        if (freeGameView != null) freeGameView.ShowAwardPrompt();

        uiManager.SetFreeGamesButtonLock(true);
        uiManager.SetSpinButtonMode(UIManager.SpinButtonMode.FreeGamesStart);

        currentState = GameState.Idle;
    }

    // The Start button — routed here by UIManager's FreeGamesStart mode. The prompt becomes the
    // counter, the total counts up from 0, and the first spin follows.
    internal void StartFirstFreeSpin()
    {
        uiManager.SetSpinButtonMode(UIManager.SpinButtonMode.FreeGamesStart, interactable: false);

        if (freeGameView == null)
        {
            StartCoroutine(DelayBeforeFirstFreeSpin());
            return;
        }

        freeGameView.PlayCounterIntro(FreeSpinsTotalAwarded, () => StartCoroutine(DelayBeforeFirstFreeSpin()));
    }

    private IEnumerator DelayBeforeFirstFreeSpin()
    {
        yield return new WaitForSeconds(0.5f);
        RequestSpin();
    }

    private IEnumerator DelayBeforeNextFreeSpin()
    {
        yield return new WaitForSeconds(0.3f);

        // Wait for special win popup if it's still active or pending
        while (waitingForSpecialWin || uiManager.IsSpecialWinActive)
        {
            yield return null;
        }

        RequestSpin();
    }

    private void EndFreeSpins()
    {
        double roundWin = freeSpinsRoundWin;

        isInFreeSpins = false;
        freeSpinsRemaining = 0;
        AudioManager.Instance?.PlayMainBg();

        // Free spins skip the per-line cycle, so the final spin is parked after Phase 1. Start it
        // here so it plays on the reels beneath the closing summary rather than making the player
        // wait for it afterwards. Must come after isInFreeSpins is cleared — PlayWinLineCycle is a
        // no-op during free spins.
        if (slotView != null) slotView.PlayWinLineCycle();

        if (freeGameView != null)
        {
            freeGameView.PlayOutroSequence(roundWin, OnFreeGamesCountUpComplete, OnFreeGamesOutroComplete);
        }
        else
        {
            OnFreeGamesOutroComplete();
        }
    }

    // The summary's total has finished counting up — Take becomes pressable. FreeGameView owns
    // what happens on the press and calls back through OnFreeGamesOutroComplete.
    private void OnFreeGamesCountUpComplete()
    {
        uiManager.SetSpinButtonMode(UIManager.SpinButtonMode.FreeGamesTake);
    }

    // Player took the win and the closing fade finished — restore the base game.
    private void OnFreeGamesOutroComplete()
    {
        freeSpinsUsed = 0;
        freeSpinsRoundWin = 0;
        retriggerTotalBefore = -1;

        uiManager.SetSpinButtonMode(UIManager.SpinButtonMode.Spin);
        uiManager.SetFreeGamesButtonLock(false);

        currentState = GameState.Idle;

        if (ShouldResumeAutoPlay())
        {
            ResumeAutoPlay();
        }
    }

    #endregion

    #region Connection Events

    internal void OnDisconnected()
    {
        if (spinCoroutine != null)
        {
            StopCoroutine(spinCoroutine);
            spinCoroutine = null;
        }

        wasAutoPlayingBeforeFreeSpins = false;
        if (isAutoPlaying)
        {
            StopAutoPlay();
        }

        currentState = GameState.Idle;
        // Note: The disconnection popup is shown by SocketIOManager.OnSocketDisconnected()
        // to avoid duplicates. GameManager only cleans up state here.
    }

    internal void ExitGame()
    {
        socketManager.CloseSocket();

    }

    #endregion

    #region Helper Methods

    internal double GetTotalPay()
    {
        double activeLine = (gameConfig != null && gameConfig.activeLine > 0) ? gameConfig.activeLine : 27;
        return currentBetAmount * activeLine;
    }

    internal bool CanAffordBet()
    {
        double totalPay = GetTotalPay();
        return playerData.balance >= totalPay;
    }

    internal bool IsSpinning()
    {
        return currentState == GameState.Spinning || currentState == GameState.Stopping;
    }

    #endregion
}