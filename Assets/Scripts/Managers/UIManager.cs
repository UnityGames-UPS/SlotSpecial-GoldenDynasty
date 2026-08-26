using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.EventSystems;

public class UIManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameManager gameManager;
    [SerializeField] private PopupManager popupManager;
    [SerializeField] private JSFunctCalls jsFunctCalls;
    [SerializeField] private FreeGameView freeGameView;

    [Header("Loading & Intro")]
    [SerializeField] private GameObject gameScreen;
    [SerializeField] private GameObject gameLogoObject;



    [Header("Bet Controls")]
    [SerializeField] private TMP_Text betAmountText;
    [SerializeField] private Button betPlusButton;
    [SerializeField] private Button betMinusButton;
    [Header("Bet Controls - Portrait")]
    [SerializeField] private TMP_Text betAmountTextPortrait;
    [Tooltip("Number of paylines currently being bet on. Scene objects are named LIneCountTxt (note the capital I).")]
    [SerializeField] private TMP_Text lineCountText;
    [SerializeField] private TMP_Text lineCountTextPortrait;
    [SerializeField] private Button betPlusButtonPortrait;
    [SerializeField] private Button betMinusButtonPortrait;

    [Header("Balance & Win")]
    [SerializeField] private TMP_Text balanceText;
    [SerializeField] private TMP_Text winAmountText;
    [SerializeField] private GameObject winTextObject;
    [SerializeField] private GameObject goodLuckObject;
    [Header("Balance & Win - Portrait")]
    [SerializeField] private TMP_Text balanceTextPortrait;
    [SerializeField] private TMP_Text winAmountTextPortrait;
    [SerializeField] private GameObject winTextObjectPortrait;
    [SerializeField] private GameObject goodLuckObjectPortrait;

    [SerializeField] private CanvasGroup transitionBackFilm;

    [Header("Universal Win Popup")]
    [SerializeField] private GameObject universalWinPopup;
    [SerializeField] private RectTransform universalWinPopupRect;
    [SerializeField] private TMP_Text bigWinAmount;

    // One button, six modes. Spin / Stop / Take / AutoplayStop used to be four separate GameObjects
    // (plus portrait twins) toggled by SetActive, all sitting at the same position and size — so
    // "which object is active" and "what does the button do" were two things that had to be kept in
    // step by hand. Now the mode is the single source of truth for the art, the click routing and
    // the count text.
    [Header("Spin Button")]
    [SerializeField] private Button spinButton;
    [Header("Spin Button - Portrait")]
    [SerializeField] private Button spinButtonPortrait;

    [Header("Spin Button Sprite Sets (one per mode)")]
    [Tooltip("Landscape and portrait share these — both buttons use the same art.")]
    [SerializeField] private ButtonSpriteSet spinSprites;
    [SerializeField] private ButtonSpriteSet stopSprites;
    [Tooltip("Shared by the free-games summary Take and the big-win popup Take.")]
    [SerializeField] private ButtonSpriteSet takeSprites;
    [SerializeField] private ButtonSpriteSet startSprites;
    [SerializeField] private ButtonSpriteSet autoplayStopSprites;

    [Header("Auto Play Count")]
    [Tooltip("Was a child of the old AutoplayStopBtn, so it hid with that object. Now a child of " +
             "the shared spin button, shown only in AutoplayStop mode.")]
    [SerializeField] private GameObject autoSpinRemainingObject;
    [SerializeField] private TMP_Text autoSpinRemainingText;
    [Header("Auto Play Count - Portrait")]
    [SerializeField] private GameObject autoSpinRemainingObjectPortrait;
    [SerializeField] private TMP_Text autoSpinRemainingTextPortrait;

    [Header("Auto Play Panel")]
    [SerializeField] private GameObject autoPlayPanel;
    [SerializeField] private RectTransform autoPlayPanelRect;
    [SerializeField] private Button autoPlayCloseButton;
    [Header("Auto Play Selection Buttons")]
    [SerializeField] private Button autoPlay10Button;
    [SerializeField] private Button autoPlay50Button;
    [SerializeField] private Button autoPlay100Button;
    [SerializeField] private Button autoPlay200Button;
    [SerializeField] private Button autoPlay500Button;
    [SerializeField] private Button autoPlayInfiniteButton;

    [Header("Auto Play Panel - Portrait")]
    [SerializeField] private GameObject autoPlayPanelPortrait;
    [SerializeField] private RectTransform autoPlayPanelRectPortrait;
    [SerializeField] private Button autoPlayCloseButtonPortrait;
    [SerializeField] private Button autoPlay10ButtonPortrait;
    [SerializeField] private Button autoPlay50ButtonPortrait;
    [SerializeField] private Button autoPlay100ButtonPortrait;
    [SerializeField] private Button autoPlay200ButtonPortrait;
    [SerializeField] private Button autoPlay500ButtonPortrait;
    [SerializeField] private Button autoPlayInfiniteButtonPortrait;

    [Header("Settings Panel")]
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private RectTransform settingsPanelRect;
    [SerializeField] private Button settingsOpenButton;
    [SerializeField] private Button settingsCloseButton;
    [SerializeField] private Button settingsBgCloseButton;
    [SerializeField] private Button gameQuitButton;
    [Header("Settings Panel - Portrait")]
    [SerializeField] private GameObject settingsPanelPortrait;
    [SerializeField] private RectTransform settingsPanelRectPortrait;
    [SerializeField] private Button settingsOpenButtonPortrait;
    [SerializeField] private Button settingsCloseButtonPortrait;
    [SerializeField] private Button settingsBgCloseButtonPortrait;
    [SerializeField] private Button gameQuitButtonPortrait;

    // All four sprites a Sprite Swap button needs, kept together. Setting only the idle sprite
    // (what this used to do) leaves the Button's own SpriteState untouched, so hovering showed
    // whichever mode's hover art was baked into the scene regardless of the current mode.
    [System.Serializable]
    public class ButtonSpriteSet
    {
        public Sprite normal;
        public Sprite highlighted;
        public Sprite pressed;
        public Sprite disabled;
    }

    [Header("Speed Button (Sprite-Swapped Cycle)")]
    [SerializeField] private Button speedButton;
    [SerializeField] private Button speedButtonPortrait;
    [Tooltip("Landscape and portrait share these — both buttons use the same art.")]
    [SerializeField] private ButtonSpriteSet speedNormalSprites;
    [SerializeField] private ButtonSpriteSet speedTurboSprites;
    [SerializeField] private ButtonSpriteSet speedQuickSpinSprites;

    [Header("Sound Panel")]
    [SerializeField] private GameObject soundPanel;
    [SerializeField] private RectTransform soundPanelRect;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Button soundPanelCloseButton;
    [SerializeField] private Button soundPanelOpenButton;
    [SerializeField] private Button soundPanelOpenButtonPortrait;

    [Header("Game Rules Panel")]
    [SerializeField] private GameObject gameRulesPanel;
    [SerializeField] private RectTransform gameRulesPanelRect;
    [SerializeField] private Button gameRulesOpenButton;
    [SerializeField] private Button gameRulesBackButton;
    [Header("Game Rules Panel - Portrait")]
    [SerializeField] private Button gameRulesOpenButtonPortrait;

    [Header("Guide Panel")]
    [SerializeField] private GameObject guidePanel;
    [SerializeField] private RectTransform guidePanelRect;
    [SerializeField] private Button guideOpenButton;
    [SerializeField] private Button guideBackButton;
    [Header("Guide Panel - Portrait")]
    [SerializeField] private Button guideOpenButtonPortrait;

    [Header("Ping Display")]
    [SerializeField] private TMP_Text pingText;
    [SerializeField] private TMP_Text pingTextPortrait;

    [Header("Platform Jackpot")]
    [SerializeField] private TMP_Text grandJackpotText;
    [SerializeField] private TMP_Text majorJackpotText;
    [SerializeField] private TMP_Text minorJackpotText;
    [SerializeField] private TMP_Text miniJackpotText;

    [Header("Platform Jackpot - Portrait")]
    [SerializeField] private TMP_Text grandJackpotTextPortrait;
    [SerializeField] private TMP_Text majorJackpotTextPortrait;
    [SerializeField] private TMP_Text minorJackpotTextPortrait;
    [SerializeField] private TMP_Text miniJackpotTextPortrait;

    [Header("Expand-Shrink Control (Sprite-Swapped Toggle)")]
    [SerializeField] private Button expandShrinkButton;
    [SerializeField] private Button expandShrinkButtonPortrait;
    [SerializeField] private Sprite spriteExpandIcon;
    [SerializeField] private Sprite spriteShrinkIcon;

    private bool isExpanded = false;
    private bool isSettingsPanelOpen = false;

    private Tween balanceTween;
    private Tween winTween;

    // Optimistic balance: the locally-deducted balance shown while the spin is in flight
    private double optimisticBalance = 0;
    private bool hasOptimisticBalance = false;

    [Header("Rapid Stop Cooldown")]
    [Tooltip("Seconds the player must wait before pressing Stop again after an immediate stop.")]
    [SerializeField] private float rapidStopCooldown = 1f;
    private float lastRapidStopTime = -99f;

    private int currentRulesPage = 0;
    private bool isPageAnimating;
    [Header("UI State")]
    private double currentWinDisplayValue = 0;
    private bool isSpecialWinActive = false;
    public bool IsSpecialWinActive => isSpecialWinActive;
    public System.Action OnSpecialWinComplete;

    // Universal Win Popup state
    private System.Action universalWinPopupCallback;
    private Coroutine uwpAutoCloseCoroutine;
    private Tween uwpWinTween;
    // Which popup is currently open — BigWin uses its own open/close animation, so the close
    // path (which has no type parameter of its own) needs to know what was shown.
    private WinPopupType currentPopupType;
    // The amount the count-up is heading for. Taking early kills that tween mid-number, so the
    // close path needs the target to snap the label to before it collapses.
    private double uwpTargetWinAmount;
    [SerializeField] private float uwpAutoCloseDelay = 5f;

    private void Awake()
    {
        if (jsFunctCalls != null)
        {
            jsFunctCalls.RegisterVisibilityListener(gameObject.name);
        }
    }



    public void OnFocusChanged(string value)
    {
        bool focused = value == "1";
        Debug.Log("UNITY FOCUS CHANGED: " + value + " (focused: " + focused + ")");
        AudioManager.Instance?.SetMuteAll(!focused);
        if (gameManager != null && gameManager.socketManager != null)
        {
            gameManager.socketManager.HandleFocusChange(focused);
        }
    }

    private void Start()
    {
        SetupButtons();
        SetupAutoPlayPanel();
        SetupSettingsPanel();
        SetupGameRulesPanel();
        SetupGuidePanel();

        InitializeExpandShrink();

        if (gameScreen) gameScreen.SetActive(true);
        InitializeUI();
        StartCoroutine(WaitForInitialization());
        RegisterFullscreenListener();
    }

    private void InitializeUI()
    {
        if (soundPanel) soundPanel.SetActive(false);
        SetGameObjectActive(autoPlayPanel, autoPlayPanelPortrait, false);
        if (autoPlayPanelRect) autoPlayPanelRect.anchoredPosition = new Vector2(autoPlayPanelRect.anchoredPosition.x, -600f);
        if (autoPlayPanelRectPortrait) autoPlayPanelRectPortrait.anchoredPosition = new Vector2(autoPlayPanelRectPortrait.anchoredPosition.x, -600f);

        // Puts the button into Spin mode, which also hides the autoplay count.
        SetSpinStopButtonStates(isSpinningState: false, isInteractable: true);
        UpdateSpeedButtonsVisibility(gameManager.currentSpinSpeed);

        isSettingsPanelOpen = false;
        SetGameObjectActive(settingsPanel, settingsPanelPortrait, false);
        if (gameRulesPanel) gameRulesPanel.SetActive(false);
        if (guidePanel) guidePanel.SetActive(false);
        if (uwpWinTween != null) { uwpWinTween.Kill(); uwpWinTween = null; }
        if (universalWinPopup) universalWinPopup.SetActive(false);

        if (transitionBackFilm) transitionBackFilm.gameObject.SetActive(false);
        UpdatePingDisplay("-- ms");
    }

    #region Loading & Intro Sequence

    private IEnumerator WaitForInitialization()
    {
        float initializationTimeout = 20f;
        float timer = 0f;
        while (!gameManager.isInitialized && !gameManager.initializationFailed && timer < initializationTimeout)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        if (gameManager.initializationFailed || !gameManager.isInitialized)
        {
            if (gameManager.socketManager != null)
            {
                gameManager.socketManager.SetRaycastBlocker(false);
            }

            if (popupManager != null)
            {
                string errorMsg = gameManager.initializationFailed ? "Game failed to initialize." : "Initialization timed out. Please check your connection.";
                popupManager.ShowErrorPopup("Connection Error", errorMsg, true);
            }
        }
        else
        {
            AudioManager.Instance?.PlayBgMusic();
        }
    }

    #endregion

    #region UI Synchronization Helpers

    private void SetTMPText(TMP_Text text1, TMP_Text text2, string content)
    {
        if (text1) text1.text = content;
        if (text2) text2.text = content;
    }

    private void SetGameObjectActive(GameObject obj1, GameObject obj2, bool active)
    {
        if (obj1) obj1.SetActive(active);
        if (obj2) obj2.SetActive(active);
    }

    private void SetButtonInteractable(Button btn1, Button btn2, bool interactable)
    {
        if (btn1) btn1.interactable = interactable;
        if (btn2) btn2.interactable = interactable;
    }

    private void SetButtonActive(Button btn1, Button btn2, bool active)
    {
        if (btn1) btn1.gameObject.SetActive(active);
        if (btn2) btn2.gameObject.SetActive(active);
    }

    #endregion

    #region Button Setup

    private void SetupButtons()
    {
        // Bet buttons
        if (betPlusButton)  betPlusButton.onClick.AddListener(() => gameManager.IncreaseBet());
        if (betMinusButton) betMinusButton.onClick.AddListener(() => gameManager.DecreaseBet());
        if (betPlusButtonPortrait)  betPlusButtonPortrait.onClick.AddListener(() => gameManager.IncreaseBet());
        if (betMinusButtonPortrait) betMinusButtonPortrait.onClick.AddListener(() => gameManager.DecreaseBet());

        // Spin button
        if (spinButton)
        {
            var holdHandler = spinButton.GetComponent<SpinButtonHoldHandler>();
            if (holdHandler != null)
            {
                holdHandler.OnClick.AddListener(OnSpinButtonPressed);
                holdHandler.OnHoldThreeSeconds.AddListener(OnSpinButtonHeld);
            }
            else
            {
                spinButton.onClick.AddListener(OnSpinButtonPressed);
            }
        }
        if (spinButtonPortrait)
        {
            var holdHandler = spinButtonPortrait.GetComponent<SpinButtonHoldHandler>();
            if (holdHandler != null)
            {
                holdHandler.OnClick.AddListener(OnSpinButtonPressed);
                holdHandler.OnHoldThreeSeconds.AddListener(OnSpinButtonHeld);
            }
            else
            {
                spinButtonPortrait.onClick.AddListener(OnSpinButtonPressed);
            }
        }

        // Stop, autoplay-stop and both Takes no longer have buttons of their own — OnSpinButtonPressed
        // routes to each of them off the current mode.

        if (autoPlayCloseButton) autoPlayCloseButton.onClick.AddListener(CloseAutoPlayPanel);
        if (autoPlayCloseButtonPortrait) autoPlayCloseButtonPortrait.onClick.AddListener(CloseAutoPlayPanel);

        if (gameQuitButton) gameQuitButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); OnExitButtonPressed(); });
        if (gameQuitButtonPortrait) gameQuitButtonPortrait.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); OnExitButtonPressed(); });

        if (expandShrinkButton) expandShrinkButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); OnExpandShrinkButtonPressed(); });
        if (expandShrinkButtonPortrait) expandShrinkButtonPortrait.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); OnExpandShrinkButtonPressed(); });

        // Speed button setup (single sprite-swapped cycle button)
        if (speedButton) speedButton.onClick.AddListener(OnSpeedButtonPressed);
        if (speedButtonPortrait) speedButtonPortrait.onClick.AddListener(OnSpeedButtonPressed);
    }

    private void SetupAutoPlayPanel()
    {
        if (autoPlay10Button)       autoPlay10Button.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(10); });
        if (autoPlay50Button)       autoPlay50Button.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(50); });
        if (autoPlay100Button)      autoPlay100Button.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(100); });
        if (autoPlay200Button)      autoPlay200Button.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(200); });
        if (autoPlay500Button)      autoPlay500Button.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(500); });
        if (autoPlayInfiniteButton) autoPlayInfiniteButton.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(-1); });

        if (autoPlay10ButtonPortrait)       autoPlay10ButtonPortrait.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(10); });
        if (autoPlay50ButtonPortrait)       autoPlay50ButtonPortrait.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(50); });
        if (autoPlay100ButtonPortrait)      autoPlay100ButtonPortrait.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(100); });
        if (autoPlay200ButtonPortrait)      autoPlay200ButtonPortrait.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(200); });
        if (autoPlay500ButtonPortrait)      autoPlay500ButtonPortrait.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(500); });
        if (autoPlayInfiniteButtonPortrait) autoPlayInfiniteButtonPortrait.onClick.AddListener(() => { AudioManager.Instance?.PlayButton(); StartAutoplayWithRounds(-1); });
    }

    private void SetupSettingsPanel()
    {
        if (settingsOpenButton) settingsOpenButton.onClick.AddListener(() => { 
            AudioManager.Instance?.PlayButton(); 
            if (isSettingsPanelOpen)
                CloseSettingsPanel();
            else
                OpenSettingsPanel();
        });
        if (settingsOpenButtonPortrait) settingsOpenButtonPortrait.onClick.AddListener(() => { 
            AudioManager.Instance?.PlayButton(); 
            if (isSettingsPanelOpen)
                CloseSettingsPanel();
            else
                OpenSettingsPanel();
        });

        if (settingsCloseButton) settingsCloseButton.onClick.AddListener(() => { 
            AudioManager.Instance?.PlayButton(); 
            CloseSettingsPanel(); 
        });
        if (settingsCloseButtonPortrait) settingsCloseButtonPortrait.onClick.AddListener(() => { 
            AudioManager.Instance?.PlayButton(); 
            CloseSettingsPanel(); 
        });
        if (settingsBgCloseButton) settingsBgCloseButton.onClick.AddListener(() => { 
            AudioManager.Instance?.PlayButton(); 
            CloseSettingsPanel(); 
        });
        if (settingsBgCloseButtonPortrait) settingsBgCloseButtonPortrait.onClick.AddListener(() => { 
            AudioManager.Instance?.PlayButton(); 
            CloseSettingsPanel(); 
        });

        SetButtonActive(settingsOpenButton, settingsOpenButtonPortrait, true);
        SetButtonActive(settingsCloseButton, settingsCloseButtonPortrait, false);
        SetButtonActive(settingsBgCloseButton, settingsBgCloseButtonPortrait, false);

        // Sound panel buttons & sliders
        if (soundPanelOpenButton) soundPanelOpenButton.onClick.AddListener(OpenSoundPanel);
        if (soundPanelOpenButtonPortrait) soundPanelOpenButtonPortrait.onClick.AddListener(OpenSoundPanel);
        if (soundPanelCloseButton) soundPanelCloseButton.onClick.AddListener(CloseSoundPanel);

        if (musicSlider)
        {
            if (AudioManager.Instance != null) musicSlider.value = AudioManager.Instance.MusicVolume;
            musicSlider.onValueChanged.AddListener(OnMusicSliderChanged);
        }
        if (sfxSlider)
        {
            if (AudioManager.Instance != null) sfxSlider.value = AudioManager.Instance.SfxVolume;
            sfxSlider.onValueChanged.AddListener(OnSfxSliderChanged);
        }
    }

    private void SetupGameRulesPanel()
    {
        if (gameRulesOpenButton) gameRulesOpenButton.onClick.AddListener(OpenGameRulesPanel);
        if (gameRulesOpenButtonPortrait) gameRulesOpenButtonPortrait.onClick.AddListener(OpenGameRulesPanel);

        if (gameRulesBackButton) gameRulesBackButton.onClick.AddListener(() => { AudioManager.Instance?.PlayPopupClose(); CloseGameRulesPanel(); });
    }

    private void SetupGuidePanel()
    {
        if (guideOpenButton) guideOpenButton.onClick.AddListener(OpenGuidePanel);
        if (guideOpenButtonPortrait) guideOpenButtonPortrait.onClick.AddListener(OpenGuidePanel);

        if (guideBackButton) guideBackButton.onClick.AddListener(() => { AudioManager.Instance?.PlayPopupClose(); CloseGuidePanel(); });
    }

    #endregion

    #region Game Events

    internal void OnGameInitialized()
    {
        currentWinDisplayValue = 0;
        UpdateBetDisplay();
        UpdateBalanceDisplay();
        UpdateWinDisplay(0);
        UpdateLineCountDisplay();
    }

    // Server-driven and fixed for the session — set once on init rather than per spin.
    private void UpdateLineCountDisplay()
    {
        if (gameManager == null || gameManager.gameConfig == null) return;

        string lines = gameManager.gameConfig.activeLine.ToString();
        SetTMPText(lineCountText, lineCountTextPortrait, lines);
    }

    internal void OnSpinStarted()
    {
        AudioManager.Instance?.PlaySpinStart();

        if (gameManager.isInFreeSpins)
        {
            SetSpinStopButtonStates(isSpinningState: true, isInteractable: false);
        }
        else
        {
            SetSpinStopButtonStates(isSpinningState: true, isInteractable: true);
            SetBetControlsEnabled(false);
            SetButtonInteractable(settingsOpenButton, settingsOpenButtonPortrait, true);
        }

        UpdateBalanceDisplay();

        // In free games the win box shows the round's running total, so clearing it here made every
        // spin snap to 0 and then back up once the reels landed. The round total is cleared where it
        // actually resets: StartFreeSpins before the first spin, and SetSpinButtonMode(Spin) once
        // the player has taken the win.
        if (gameManager == null || !gameManager.isInFreeSpins)
        {
            UpdateWinDisplay(0);
        }

        CloseAutoPlayPanelImmediate();
    }

    internal void OnSpinResultReceived()
    {
        if (gameManager.isAutoPlaying)
        {
            SetSpinStopButtonStates(isSpinningState: true, isInteractable: true);
        }
        else if (gameManager.isInFreeSpins)
        {
            SetSpinStopButtonStates(isSpinningState: true, isInteractable: false);
        }
        else
        {
            SetSpinStopButtonStates(isSpinningState: true, isInteractable: true);
        }
    }

    internal void OnSpinStopping(SpinResult result = null)
    {
        UpdateBalanceDisplay();
        if (result != null)
        {
            // In free games the win box shows the round's running total, which GameManager
            // accumulates — the server sends only this spin's win.
            double displayWin = (gameManager != null && gameManager.isInFreeSpins) ? gameManager.freeSpinsRoundWin : result.winAmount;
            UpdateWinDisplay(displayWin);
        }
    }

    internal void OnSpinCompleted(SpinResult result = null)
    {
        if (result != null)
        {
            // In free games the win box shows the round's running total, which GameManager
            // accumulates — the server sends only this spin's win.
            double displayWin = (gameManager != null && gameManager.isInFreeSpins) ? gameManager.freeSpinsRoundWin : result.winAmount;
            UpdateWinDisplay(displayWin);
        }
        UpdateBalanceDisplay();

        if (gameManager.isAutoPlaying)
        {
            SetSpinStopButtonStates(isSpinningState: true, isInteractable: true);
        }
        else if (gameManager.isInFreeSpins)
        {
            SetSpinStopButtonStates(isSpinningState: true, isInteractable: false);
        }
        else
        {
            SetSpinStopButtonStates(isSpinningState: false, isInteractable: true);

            SetBetControlsEnabled(true);
            SetButtonInteractable(settingsOpenButton, settingsOpenButtonPortrait, true);
        }
    }

    internal void TriggerBigWinPopup(SpinResult result, System.Action onComplete = null)
    {
        double winAmount = (result != null) ? result.winAmount : 0;
        ShowUniversalWinPopup(WinPopupType.BigWin, winAmount, onComplete);
    }

    internal void DisableControlsDuringWinAnimation()
    {
        SetBetControlsEnabled(false);
        SetSpinStopButtonStates(isSpinningState: false, isInteractable: false);
    }

    internal void EnableControlsAfterWinAnimation()
    {
        if (isSpecialWinActive) return;

        if (gameManager.isAutoPlaying)
        {
            SetSpinStopButtonStates(isSpinningState: true, isInteractable: true);
        }
        else if (gameManager.isInFreeSpins)
        {
            SetSpinStopButtonStates(isSpinningState: true, isInteractable: false);
        }
        else
        {
            SetBetControlsEnabled(true);
            SetButtonInteractable(settingsOpenButton, settingsOpenButtonPortrait, true);
            SetSpinStopButtonStates(isSpinningState: false, isInteractable: true);
        }
    }

    #endregion

    #region Spin Button

    /// <summary>
    /// The one click handler for the shared button. Routes entirely on the current mode — what the
    /// player sees and what the press does can't disagree.
    /// </summary>
    public void OnSpinButtonPressed()
    {
        switch (spinButtonMode)
        {
            case SpinButtonMode.FreeGamesStart:
                AudioManager.Instance?.PlayButton();
                SetSpinButtonMode(SpinButtonMode.FreeGamesStart, interactable: false);
                gameManager.StartFirstFreeSpin();
                return;

            case SpinButtonMode.FreeGamesTake:
                AudioManager.Instance?.PlayTakeButton();
                SetSpinButtonMode(SpinButtonMode.FreeGamesTake, interactable: false);
                if (freeGameView != null) freeGameView.OnTakePressed();
                return;

            case SpinButtonMode.BigWinTake:
                OnUniversalWinTakeButtonClicked();
                return;

            case SpinButtonMode.AutoplayStop:
                AudioManager.Instance?.PlayAutoplayStop();
                gameManager.StopAutoPlay();
                return;

            case SpinButtonMode.Stop:
                if (gameManager.IsSpinning())
                {
                    // Rapid-stop cooldown: prevent the player from spamming the stop button
                    if (Time.unscaledTime - lastRapidStopTime < rapidStopCooldown) return;

                    lastRapidStopTime = Time.unscaledTime;
                    AudioManager.Instance?.PlaySpinStop();
                    gameManager.RequestStop();
                }
                return;

            default:
                // Autoplay can be running while the button still reads Spin (it is re-derived on
                // every state change), so keep the stop-autoplay path reachable here too.
                if (gameManager.isAutoPlaying)
                {
                    AudioManager.Instance?.PlayAutoplayStop();
                    gameManager.StopAutoPlay();
                    return;
                }

                if (!gameManager.IsSpinning())
                {
                    gameManager.RequestSpin();
                }
                return;
        }
    }

    /// <summary>
    /// Derives the button's mode from the current spin/autoplay state. Kept on its original
    /// signature because ~20 call sites speak in those terms; it now picks a mode instead of
    /// toggling four GameObjects.
    /// </summary>
    internal void SetSpinStopButtonStates(bool isSpinningState, bool isInteractable)
    {
        // Free-games and big-win modes are owned by whoever set them and outlive the spin events
        // that would otherwise reset the button — the closing summary in particular has to hold
        // Take through its count-up. Only the interactable flag is honoured while one is active.
        if (IsExplicitMode(spinButtonMode))
        {
            SetButtonInteractable(spinButton, spinButtonPortrait, isInteractable);
            return;
        }

        SpinButtonMode mode;
        if (gameManager != null && gameManager.isAutoPlaying) mode = SpinButtonMode.AutoplayStop;
        else if (isSpinningState)                            mode = SpinButtonMode.Stop;
        else                                                 mode = SpinButtonMode.Spin;

        ApplySpinButtonState(mode, isInteractable);
    }

    #endregion

    #region Bet Controls

    internal void UpdateBetDisplay()
    {
        if (gameManager.gameConfig == null) return;

        double totalPay = gameManager.GetTotalPay();

        if (betAmountText) betAmountText.text = FormatAmount(totalPay);
        if (betAmountTextPortrait) betAmountTextPortrait.text = "TOTAL PAY : " + FormatAmount(totalPay);

        UpdateBetButtonStates();
    }

    private void UpdateBetButtonStates()
    {
        SetButtonInteractable(betMinusButton, betMinusButtonPortrait, true);
        SetButtonInteractable(betPlusButton, betPlusButtonPortrait, true);
    }

    #endregion

    #region Auto Play Panel

    public void OnSpinButtonHeld()
    {
        // Spin mode only. The hold handler used to live on a SpinBtn object that was hidden in every
        // other state, so visibility did this gating for free; the shared button is always visible,
        // so holding on Stop / Take / Start / AutoplayStop has to be rejected explicitly.
        if (spinButtonMode != SpinButtonMode.Spin) return;

        if (gameManager.currentState == GameState.Idle && !gameManager.isAutoPlaying)
        {
            AudioManager.Instance?.PlayButton();
            OpenAutoPlayPanel();
        }
    }

    private void OpenAutoPlayPanel()
    {
        AudioManager.Instance?.PlayAutoplayPanelOpen();
        if (isSettingsPanelOpen)
            CloseSettingsPanelImmediate();

        SetGameObjectActive(autoPlayPanel, autoPlayPanelPortrait, true);
        if (autoPlayPanelRect)
        {
            autoPlayPanelRect.anchoredPosition = new Vector2(autoPlayPanelRect.anchoredPosition.x, -600f);
            autoPlayPanelRect.DOAnchorPosY(0f, 0.35f).SetEase(Ease.OutCubic);
        }
        if (autoPlayPanelRectPortrait)
        {
            autoPlayPanelRectPortrait.anchoredPosition = new Vector2(autoPlayPanelRectPortrait.anchoredPosition.x, -600f);
            autoPlayPanelRectPortrait.DOAnchorPosY(0f, 0.35f).SetEase(Ease.OutCubic);
        }
    }

    private void CloseAutoPlayPanel()
    {
        AudioManager.Instance?.PlayPopupClose();

        if (autoPlayPanelRect)
        {
            autoPlayPanelRect.DOAnchorPosY(-600f, 0.35f).SetEase(Ease.InCubic).OnComplete(() =>
            {
                if (autoPlayPanel) autoPlayPanel.SetActive(false);
            });
        }
        else
        {
            if (autoPlayPanel) autoPlayPanel.SetActive(false);
        }

        if (autoPlayPanelRectPortrait)
        {
            autoPlayPanelRectPortrait.DOAnchorPosY(-600f, 0.35f).SetEase(Ease.InCubic).OnComplete(() =>
            {
                if (autoPlayPanelPortrait) autoPlayPanelPortrait.SetActive(false);
            });
        }
        else
        {
            if (autoPlayPanelPortrait) autoPlayPanelPortrait.SetActive(false);
        }
    }

    private void StartAutoplayWithRounds(int rounds)
    {
        CloseAutoPlayPanel();
        gameManager.StartAutoPlay(rounds);
    }

    internal void OnAutoPlayStarted()
    {
        UpdateAutoPlayCount();
        SetSpinStopButtonStates(isSpinningState: true, isInteractable: true);
        SetBetControlsEnabled(false);
    }

    internal void OnAutoPlayStopped()
    {
        bool isRoundActive = gameManager.IsSpinning() || gameManager.lastResult != null;

        if (!isRoundActive && !gameManager.isInFreeSpins)
        {
            SetSpinStopButtonStates(isSpinningState: false, isInteractable: true);
            SetBetControlsEnabled(true);
            SetButtonInteractable(settingsOpenButton, settingsOpenButtonPortrait, true);
        }
        else if (isRoundActive)
        {
            // The last autoplay round is still resolving. Hold the AutoplayStop face, greyed out,
            // until it finishes — isAutoPlaying is already false, so the mode has to be forced.
            ApplySpinButtonState(SpinButtonMode.AutoplayStop, interactable: false);
        }
    }

    internal void UpdateAutoPlayCount()
    {
        string displayStr = "";
        if (gameManager.autoPlayTotalRounds == -1 || gameManager.autoPlayRemainingRounds < 0)
        {
            displayStr = "∞";
        }
        else
        {
            displayStr = $"{gameManager.autoPlayRemainingRounds}";
        }

        SetTMPText(autoSpinRemainingText, autoSpinRemainingTextPortrait, displayStr);
    }

    #endregion

    #region Spin Speed Universal Toggle Logic

    private void OnSpeedButtonPressed()
    {
        AudioManager.Instance?.PlayTurboButton();
        SetSpeedMode(GetNextSpinSpeed(gameManager.currentSpinSpeed));
    }

    private SpinSpeed GetNextSpinSpeed(SpinSpeed current)
    {
        switch (current)
        {
            case SpinSpeed.Normal: return SpinSpeed.Turbo;
            case SpinSpeed.Turbo: return SpinSpeed.QuickSpin;
            case SpinSpeed.QuickSpin: return SpinSpeed.Normal;
            default: return SpinSpeed.Normal;
        }
    }

    public void SetSpeedMode(SpinSpeed speed)
    {
        gameManager.SetSpinSpeed(speed);
        UpdateSpeedButtonsVisibility(speed);
    }

    private void UpdateSpeedButtonsVisibility(SpinSpeed speed)
    {
        ButtonSpriteSet targetSet;
        switch (speed)
        {
            case SpinSpeed.Turbo: targetSet = speedTurboSprites; break;
            case SpinSpeed.QuickSpin: targetSet = speedQuickSpinSprites; break;
            default: targetSet = speedNormalSprites; break;
        }

        ApplyButtonSprites(speedButton, targetSet);
        ApplyButtonSprites(speedButtonPortrait, targetSet);
    }

    /// <summary>
    /// Swaps every visual state of a Sprite Swap button at once. Assigning only the idle sprite
    /// leaves the hover/pressed/disabled art on whatever the scene baked in, which is how the speed
    /// button ended up showing Normal's hover graphic while in Turbo or QuickSpin.
    /// </summary>
    private void ApplyButtonSprites(Button button, ButtonSpriteSet set)
    {
        if (button == null || set == null) return;

        // button.image is targetGraphic as Image — respects whichever graphic the button actually
        // drives, unlike a GetComponent<Image>() on the same object.
        Image img = button.image;
        if (img != null && set.normal != null)
        {
            // Sprite Swap shows its hover art via overrideSprite, and the mode only ever changes
            // from a click — so the pointer is still over the button and the old mode's hover
            // sprite would stay on screen until it left. Clearing it shows the new mode's idle art
            // immediately; Unity re-applies the correct hover on the next pointer event.
            img.overrideSprite = null;
            img.sprite = set.normal;
        }

        // spriteState is a struct property: mutating its fields in place does nothing, the whole
        // value has to be reassigned. selectedSprite is deliberately left null, matching how these
        // buttons are authored in the scene.
        button.spriteState = new SpriteState
        {
            highlightedSprite = set.highlighted,
            pressedSprite = set.pressed,
            disabledSprite = set.disabled
        };
    }

    #endregion

    #region Sound Panel

    private void OpenSoundPanel()
    {
        AudioManager.Instance?.PlayButton();
        if (soundPanel == null) return;
        soundPanel.SetActive(true);
        if (soundPanelRect != null)
        {
            AnimatePopupOpen(soundPanelRect);
        }
        if (musicSlider && AudioManager.Instance != null)
        {
            musicSlider.value = AudioManager.Instance.MusicVolume;
        }
        if (sfxSlider && AudioManager.Instance != null)
        {
            sfxSlider.value = AudioManager.Instance.SfxVolume;
        }
    }

    private void CloseSoundPanel()
    {
        if (soundPanel == null || !soundPanel.activeSelf) return;
        if (soundPanelRect != null)
        {
            AnimatePopupClose(soundPanelRect, () =>
            {
                soundPanel.SetActive(false);
            });
        }
        else
        {
            soundPanel.SetActive(false);
        }
    }

    private void OnMusicSliderChanged(float val)
    {
        AudioManager.Instance?.SetMusicVolume(val);
    }

    private void OnSfxSliderChanged(float val)
    {
        AudioManager.Instance?.SetSfxVolume(val);
    }

    #endregion

    #region Settings Panel

    private void OpenSettingsPanel()
    {
        if ((autoPlayPanel && autoPlayPanel.activeSelf) || (autoPlayPanelPortrait && autoPlayPanelPortrait.activeSelf))
            CloseAutoPlayPanelImmediate();

        isSettingsPanelOpen = true;

        SetButtonActive(settingsOpenButton, settingsOpenButtonPortrait, false);
        SetButtonActive(settingsCloseButton, settingsCloseButtonPortrait, true);
        SetButtonActive(settingsBgCloseButton, settingsBgCloseButtonPortrait, true);

        if (settingsPanel)
        {
            settingsPanel.SetActive(true);
            CanvasGroup cg = settingsPanel.GetComponent<CanvasGroup>();
            if (cg == null) cg = settingsPanel.AddComponent<CanvasGroup>();
            cg.DOKill();
            cg.DOFade(1f, 0.35f);
        }

        if (settingsPanelPortrait)
        {
            settingsPanelPortrait.SetActive(true);
            CanvasGroup cg = settingsPanelPortrait.GetComponent<CanvasGroup>();
            if (cg == null) cg = settingsPanelPortrait.AddComponent<CanvasGroup>();
            cg.DOKill();
            cg.DOFade(1f, 0.35f);
        }
    }

    private void CloseSettingsPanel()
    {
        isSettingsPanelOpen = false;

        SetButtonActive(settingsOpenButton, settingsOpenButtonPortrait, true);
        SetButtonActive(settingsCloseButton, settingsCloseButtonPortrait, false);
        SetButtonActive(settingsBgCloseButton, settingsBgCloseButtonPortrait, false);

        if (settingsPanel)
        {
            CanvasGroup cg = settingsPanel.GetComponent<CanvasGroup>();
            if (cg == null) cg = settingsPanel.AddComponent<CanvasGroup>();
            cg.DOKill();
            cg.DOFade(0f, 0.35f).OnComplete(() =>
            {
                settingsPanel.SetActive(false);
            });
        }

        if (settingsPanelPortrait)
        {
            CanvasGroup cg = settingsPanelPortrait.GetComponent<CanvasGroup>();
            if (cg == null) cg = settingsPanelPortrait.AddComponent<CanvasGroup>();
            cg.DOKill();
            cg.DOFade(0f, 0.35f).OnComplete(() =>
            {
                settingsPanelPortrait.SetActive(false);
            });
        }
    }

    private void CloseSettingsPanelImmediate()
    {
        isSettingsPanelOpen = false;

        SetButtonActive(settingsOpenButton, settingsOpenButtonPortrait, true);
        SetButtonActive(settingsCloseButton, settingsCloseButtonPortrait, false);
        SetButtonActive(settingsBgCloseButton, settingsBgCloseButtonPortrait, false);

        if (settingsPanel)
        {
            CanvasGroup cg = settingsPanel.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.DOKill();
                cg.alpha = 0f;
            }
            settingsPanel.SetActive(false);
        }

        if (settingsPanelPortrait)
        {
            CanvasGroup cg = settingsPanelPortrait.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.DOKill();
                cg.alpha = 0f;
            }
            settingsPanelPortrait.SetActive(false);
        }
    }

    private void CloseAutoPlayPanelImmediate()
    {
        if (autoPlayPanelRect) autoPlayPanelRect.localScale = Vector3.one;
        if (autoPlayPanelRectPortrait) autoPlayPanelRectPortrait.localScale = Vector3.one;
        SetGameObjectActive(autoPlayPanel, autoPlayPanelPortrait, false);
    }

    #endregion

    #region Game Rules Panel

    private void OpenGameRulesPanel()
    {
        if (isSettingsPanelOpen)
        {
            CloseSettingsPanelImmediate();
        }
        ShowGameRulesPanel();
    }

    private void ShowGameRulesPanel()
    {
        if (gameRulesPanel == null) return;
        gameRulesPanel.SetActive(true);
    }

    private void CloseGameRulesPanel()
    {
        if (gameRulesPanel == null) return;
        gameRulesPanel.SetActive(false);
    }

    #endregion

    #region Guide Panel

    private void OpenGuidePanel()
    {
        if (isSettingsPanelOpen)
        {
            CloseSettingsPanelImmediate();
        }
        ShowGuidePanel();
    }

    private void ShowGuidePanel()
    {
        if (guidePanel == null) return;
        guidePanel.SetActive(true);
    }

    private void CloseGuidePanel()
    {
        if (guidePanel == null) return;
        guidePanel.SetActive(false);
    }

    #endregion

    #region Free Games Button Modes

    // Every state the one shared button can be in. Spin/Stop/AutoplayStop used to be separate
    // GameObjects toggled by SetActive; the free-games and big-win states were sprite swaps on the
    // spin object. All six are now modes on the same button.
    //
    // The two Takes share their art but stay distinct because they answer to different owners:
    // FreeGamesTake calls back into FreeGameView, BigWinTake closes the popup.
    internal enum SpinButtonMode
    {
        Spin,
        Stop,
        AutoplayStop,
        FreeGamesStart,
        FreeGamesTake,
        BigWinTake
    }

    private SpinButtonMode spinButtonMode = SpinButtonMode.Spin;

    // True while a mode was set explicitly by SetSpinButtonMode rather than derived from the spin
    // state. SetSpinStopButtonStates must not stomp on those — the free-games summary and the
    // big-win popup both hold the button in a mode across events that would otherwise reset it.
    private static bool IsExplicitMode(SpinButtonMode mode)
    {
        return mode == SpinButtonMode.FreeGamesStart
            || mode == SpinButtonMode.FreeGamesTake
            || mode == SpinButtonMode.BigWinTake;
    }

    internal void SetSpinButtonMode(SpinButtonMode mode, bool interactable = true)
    {
        if (mode == SpinButtonMode.FreeGamesStart)
        {
            if (gameLogoObject) gameLogoObject.SetActive(false);
        }
        else if (mode == SpinButtonMode.Spin)
        {
            if (gameLogoObject) gameLogoObject.SetActive(true);
            UpdateWinDisplay(0);
        }

        ApplySpinButtonState(mode, interactable);
    }

    /// <summary>
    /// The single place the shared button's appearance, interactability and count text are set.
    /// </summary>
    private void ApplySpinButtonState(SpinButtonMode mode, bool interactable)
    {
        spinButtonMode = mode;

        ButtonSpriteSet set;
        switch (mode)
        {
            case SpinButtonMode.Stop:           set = stopSprites; break;
            case SpinButtonMode.AutoplayStop:   set = autoplayStopSprites; break;
            case SpinButtonMode.FreeGamesStart: set = startSprites; break;
            case SpinButtonMode.FreeGamesTake:
            case SpinButtonMode.BigWinTake:     set = takeSprites; break;
            default:                            set = spinSprites; break;
        }

        // ApplyButtonSprites clears overrideSprite before writing. Without that the art stays
        // whatever Unity's last Sprite Swap transition stamped on — which is why the Take button
        // used to stay invisible until the player clicked it.
        ApplyButtonSprites(spinButton, set);
        ApplyButtonSprites(spinButtonPortrait, set);

        SetButtonInteractable(spinButton, spinButtonPortrait, interactable);

        // The count used to be a child of the autoplay-stop object and hid with it.
        SetGameObjectActive(autoSpinRemainingObject, autoSpinRemainingObjectPortrait,
                            mode == SpinButtonMode.AutoplayStop);
    }

    /// <summary>
    /// Locks down everything the player shouldn't touch during free games. Start/Take, fullscreen
    /// and the turbo/quickspin toggle stay live. The darker look comes from Unity's built-in
    /// disabled tint already configured on these buttons, so no extra sprites are needed.
    /// </summary>
    internal void SetFreeGamesButtonLock(bool locked)
    {
        bool enabled = !locked;

        SetBetControlsEnabled(enabled);
        SetButtonInteractable(settingsOpenButton, settingsOpenButtonPortrait, enabled);
        SetButtonInteractable(gameRulesOpenButton, gameRulesOpenButtonPortrait, enabled);
        SetButtonInteractable(guideOpenButton, guideOpenButtonPortrait, enabled);
        SetButtonInteractable(soundPanelOpenButton, soundPanelOpenButtonPortrait, enabled);
        // The autoplay-stop button used to be locked here too. It no longer exists as its own
        // object, and autoplay can't be running during free games anyway — StartFreeSpins stops it.
    }

    #endregion

    #region Expand / Shrink

    private void InitializeExpandShrink()
    {
        SetExpandShrinkButtons(isExpanded: false);
    }

    private void OnExpandShrinkButtonPressed()
    {
        isExpanded = !isExpanded;
        if (isExpanded) jsFunctCalls?.RequestExpandGame();
        else jsFunctCalls?.RequestShrinkGame();
        SetExpandShrinkButtons(isExpanded);
    }

    private void SetExpandShrinkButtons(bool isExpanded)
    {
        SetExpandShrinkButtonSprite(isExpanded ? spriteShrinkIcon : spriteExpandIcon);
    }

    private void SetExpandShrinkButtonSprite(Sprite sprite)
    {
        if (sprite == null) return;
        if (expandShrinkButton) { var img = expandShrinkButton.GetComponent<Image>(); if (img) img.sprite = sprite; }
        if (expandShrinkButtonPortrait) { var img = expandShrinkButtonPortrait.GetComponent<Image>(); if (img) img.sprite = sprite; }
    }

    private void RegisterFullscreenListener()
    {
        jsFunctCalls?.RegisterFullscreenListener(gameObject.name);
    }

    internal void OnFullscreenChanged(string isFullscreen)
    {
        bool newExpandedState = isFullscreen == "1";
        Debug.Log($"[UI] OnFullscreenChanged callback: isFullscreen={isFullscreen}, newState={newExpandedState}");

        if (isExpanded != newExpandedState)
        {
            isExpanded = newExpandedState;
            SetExpandShrinkButtons(isExpanded);
            Debug.Log($"[UI] Button states synced to fullscreen: {(isExpanded ? "EXPANDED" : "SHRINK")}");
        }
    }
    
    #endregion

    #region Popup Animations (Generic)

    private void AnimatePopupOpen(RectTransform popupRect)
    {
        if (!popupRect) return;
        popupRect.localScale = Vector3.zero;
        popupRect.DOScale(1.4f, 0.3f).SetEase(Ease.OutBack);
    }

    private void AnimatePopupClose(RectTransform popupRect, System.Action onComplete)
    {
        if (!popupRect) return;

        AudioManager.Instance?.PlayPopupClose();

        Sequence closeSeq = DOTween.Sequence();
        closeSeq.Append(popupRect.DOScale(1.5f, 0.1f));
        closeSeq.Append(popupRect.DOScale(0f, 0.2f).SetEase(Ease.InBack));
        closeSeq.OnComplete(() =>
        {
            popupRect.localScale = Vector3.one * 1.4f;
            onComplete?.Invoke();
        });
    }

    #endregion

    #region Display Updates

    internal void UpdatePingDisplay(int pingMs)
    {
        SetTMPText(pingText, pingTextPortrait, $"{pingMs} ms");
    }

    internal void UpdatePingDisplay(string content)
    {
        SetTMPText(pingText, pingTextPortrait, content);
    }

    internal void UpdateJackpotDisplay(JackpotValues values)
    {
        if (values == null) return;

        SetTMPText(grandJackpotText, grandJackpotTextPortrait, FormatJackpotValue(values.grandJackpot));
        SetTMPText(majorJackpotText, majorJackpotTextPortrait, FormatJackpotValue(values.majorJackpot));
        SetTMPText(minorJackpotText, minorJackpotTextPortrait, FormatJackpotValue(values.minorJackpot));
        SetTMPText(miniJackpotText, miniJackpotTextPortrait, FormatJackpotValue(values.miniJackpot));
    }

    private string FormatJackpotValue(string val)
    {
        if (string.IsNullOrEmpty(val)) return "$0.00";
        return val.StartsWith("$") ? val : "$" + val;
    }

    internal void UpdateBalanceDisplay()
    {
        SetTMPText(balanceText, balanceTextPortrait, "BALANCE : " + FormatAmount(gameManager.playerData.balance));
    }

    private void UpdateWinDisplay(double amount)
    {
        currentWinDisplayValue = amount;
        if (winAmountText) winAmountText.text = FormatAmount(amount);
        if (winAmountTextPortrait) winAmountTextPortrait.text = "WIN " + FormatAmount(amount);

        bool showWinText = amount > 0 || (gameManager != null && gameManager.isInFreeSpins);

        if (showWinText)
        {
            SetGameObjectActive(goodLuckObject, goodLuckObjectPortrait, false);
            SetGameObjectActive(winTextObject, winTextObjectPortrait, true);
        }
        else
        {
            SetGameObjectActive(goodLuckObject, goodLuckObjectPortrait, true);
            SetGameObjectActive(winTextObject, winTextObjectPortrait, false);
        }
    }

    private void AnimateBalanceUpdate(double newBalance, double startBalance = -1f, float durationOverride = -1f)
    {
        if (balanceTween != null) balanceTween.Kill();
        hasOptimisticBalance = false;
        SetTMPText(balanceText, balanceTextPortrait, "BALANCE : " + FormatAmount(newBalance));
    }

    private void AnimateWinUpdate(double targetWin, float duration = 0.8f)
    {
        if (winTween != null) winTween.Kill();
        UpdateWinDisplay(targetWin);
    }

    #endregion

    #region Helper Methods

    // Plain-text money (balance, win box, total pay). Shares its format with the sprite-digit
    // displays via SpriteTextFormatter.MoneyFormat so the two can't drift apart.
    private string FormatAmount(double amount)
    {
        return amount.ToString(SpriteTextFormatter.MoneyFormat);
    }

    private void SetBetControlsEnabled(bool enabled)
    {
        SetButtonInteractable(betPlusButton, betPlusButtonPortrait, enabled);
        SetButtonInteractable(betMinusButton, betMinusButtonPortrait, enabled);
    }

    #endregion

    #region Cleanup

    private void OnDestroy()
    {
        if (balanceTween != null) balanceTween.Kill();
        if (winTween != null) winTween.Kill();
        DOTween.KillAll();
    }

    #endregion

    

    #region Connection Popup Management

    private void OnExitButtonPressed()
    {
        if (popupManager != null)
        {
            popupManager.ShowExitGamePopup();
        }
        else if (gameManager != null)
        {
            gameManager.ExitGame();
        }
    }

    #endregion

    #region Universal Win Popup

    internal void ShowUniversalWinPopup(WinPopupType type, double winAmount, System.Action onTakePressed = null)
    {
        if (universalWinPopup == null) return;

        AudioManager.Instance?.PlayWinObjectBg();
        isSpecialWinActive = true;
        universalWinPopupCallback = onTakePressed;
        currentPopupType = type;
        uwpTargetWinAmount = winAmount;

        if (uwpWinTween != null)
        {
            uwpWinTween.Kill();
            uwpWinTween = null;
        }

        if (bigWinAmount) bigWinAmount.gameObject.SetActive(false);

        if (bigWinAmount)
        {
            bigWinAmount.gameObject.SetActive(true);
            bigWinAmount.text = SpriteTextFormatter.ToSpriteDigits(FormatAmount(winAmount));
        }

        // Take lets the player cut the popup short. It still auto-closes after uwpAutoCloseDelay if
        // they don't press it — without the button the ~5s presentation was unskippable, since the
        // big-win path disables every other control for its whole duration.
        SetSpinButtonMode(SpinButtonMode.BigWinTake, interactable: true);

        universalWinPopup.SetActive(true);
        if (universalWinPopupRect)
        {
            universalWinPopupRect.localScale = Vector3.zero;
            Sequence openSeq = DOTween.Sequence();

            if (type == WinPopupType.BigWin)
            {
                // Fast snap in, then a slow constant swell that runs until just before the
                // auto-close (0.5s pop + 4s swell + 0.5s hold = uwpAutoCloseDelay). No overshoot.
                openSeq.Append(universalWinPopupRect.DOScale(1.1f, 0.5f).SetEase(Ease.OutQuad));
                openSeq.Append(universalWinPopupRect.DOScale(1.5f, 4f).SetEase(Ease.Linear));
            }
            else
            {
                openSeq.Append(universalWinPopupRect.DOScale(1.2f, 0.5f).SetEase(Ease.OutCubic));
                openSeq.Append(universalWinPopupRect.DOScale(1f, 0.3f).SetEase(Ease.InOutSine));
            }
        }

        if (bigWinAmount != null && bigWinAmount.gameObject.activeSelf && winAmount > 0)
        {
            // Fixed money format throughout the count-up. This previously derived its decimal
            // count from the win value, so a 4.8 win counted with one decimal while a whole
            // number counted as integers — the same inconsistency the money format now removes.
            bigWinAmount.text = SpriteTextFormatter.ToSpriteMoney(0);

            float countUpDuration = (type == WinPopupType.BigWin) ? 3.8f : 1.0f;

            uwpWinTween = DOVirtual.Float(0f, (float)winAmount, countUpDuration, (val) =>
            {
                if (bigWinAmount != null)
                {
                    bigWinAmount.text = SpriteTextFormatter.ToSpriteMoney(val);
                }
            }).OnComplete(() =>
            {
                if (bigWinAmount != null)
                {
                    bigWinAmount.text = SpriteTextFormatter.ToSpriteMoney(winAmount);
                }
                uwpWinTween = null;
            });
        }

        if (uwpAutoCloseCoroutine != null) StopCoroutine(uwpAutoCloseCoroutine);
        uwpAutoCloseCoroutine = StartCoroutine(AutoCloseUniversalWinPopup());
    }

    private IEnumerator AutoCloseUniversalWinPopup()
    {
        yield return new WaitForSeconds(uwpAutoCloseDelay);
        uwpAutoCloseCoroutine = null;
        CloseUniversalWinPopup();
    }

    private void OnUniversalWinTakeButtonClicked()
    {
        AudioManager.Instance?.StopWinObjectBg();
        AudioManager.Instance?.PlayTakeButton();
        CloseUniversalWinPopup();
    }

    private void CloseUniversalWinPopup()
    {
        if (universalWinPopup == null || !universalWinPopup.activeSelf) return;

        AudioManager.Instance?.StopWinObjectBg();

        if (uwpWinTween != null)
        {
            // Snap to the full amount before killing the count-up. Taking early stops it wherever it
            // had reached, so without this the player would watch a partial figure collapse away —
            // and the number they were shown wouldn't be the number they were paid.
            uwpWinTween.Kill();
            uwpWinTween = null;

            if (bigWinAmount != null)
            {
                bigWinAmount.text = SpriteTextFormatter.ToSpriteMoney(uwpTargetWinAmount);
            }
        }

        if (uwpAutoCloseCoroutine != null)
        {
            StopCoroutine(uwpAutoCloseCoroutine);
            uwpAutoCloseCoroutine = null;
        }

        System.Action callback = universalWinPopupCallback;
        universalWinPopupCallback = null;

        // Greyed immediately so the popup can't be taken twice while it collapses. The mode is
        // released below, once the popup is actually gone.
        SetButtonInteractable(spinButton, spinButtonPortrait, false);

        if (universalWinPopupRect)
        {
            // The BigWin open sequence runs for 4.5s, so a close triggered before it finishes
            // would otherwise leave two scale tweens fighting over the same rect.
            universalWinPopupRect.DOKill();

            Sequence closeSeq = DOTween.Sequence();

            if (currentPopupType == WinPopupType.BigWin)
            {
                // Straight collapse from wherever the swell left it — no anticipation bump.
                closeSeq.Append(universalWinPopupRect.DOScale(0f, 0.5f).SetEase(Ease.InQuad));
            }
            else
            {
                closeSeq.Append(universalWinPopupRect.DOScale(1.1f, 0.1f));
                closeSeq.Append(universalWinPopupRect.DOScale(0f, 0.2f).SetEase(Ease.InBack));
            }

            closeSeq.OnComplete(() =>
            {
                universalWinPopupRect.localScale = Vector3.one;
                universalWinPopup.SetActive(false);

                // Release the mode before re-enabling controls, or EnableControlsAfterWinAnimation's
                // SetSpinStopButtonStates would see BigWinTake still held and only touch interactable.
                spinButtonMode = SpinButtonMode.Spin;
                isSpecialWinActive = false;
                EnableControlsAfterWinAnimation();

                callback?.Invoke();
            });
        }
        else
        {
            universalWinPopup.SetActive(false);
            spinButtonMode = SpinButtonMode.Spin;
            isSpecialWinActive = false;
            EnableControlsAfterWinAnimation();

            callback?.Invoke();
        }
    }

    #endregion

}