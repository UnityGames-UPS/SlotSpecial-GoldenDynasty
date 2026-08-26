using UnityEngine;

public class AudioManager : MonoBehaviour
{
    internal static AudioManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _musicEnabled = PlayerPrefs.GetInt(PrefKeyMusic, 1) == 1;
        _sfxEnabled   = PlayerPrefs.GetInt(PrefKeysfx,   1) == 1;
        _musicVolume  = PlayerPrefs.GetFloat(PrefKeyMusicVol, 0.5f);
        _sfxVolume    = PlayerPrefs.GetFloat(PrefKeySfxVol,   1.0f);

        ApplyMusicVolume();
        ApplySfxVolume();
    }

    private const string PrefKeyMusic    = "audio_music_enabled";
    private const string PrefKeysfx      = "audio_sfx_enabled";
    private const string PrefKeyMusicVol = "audio_music_volume";
    private const string PrefKeySfxVol   = "audio_sfx_volume";

    [Header("Audio Sources")]
    [SerializeField] private AudioSource bgMusicSource;
    [SerializeField] private AudioSource uiSource;
    [SerializeField] private AudioSource wheelSegmentSource;
    [SerializeField] private AudioSource reserveSource;
    [Tooltip("Dedicated source for the spin loop. Needs its own source because the loop is stopped " +
             "at landing, and stopping a shared source would cut the reel-stop one-shots firing at " +
             "that same moment.")]
    [SerializeField] private AudioSource spinLoopSource;

    [Header("Audio Clips")]
    [SerializeField] private AudioClip clipGameMainBg;
    [SerializeField] private AudioClip clipBetPlusMinus;
    [SerializeField] private AudioClip clipMaxBetReached;
    [SerializeField] private AudioClip clip3UspinWinLineLoop;
    [SerializeField] private AudioClip clipWinObjectBg;
    [Tooltip("Spin button only. Stop / Take / AutoplayStop share clipPrimaryActionButton below.")]
    [SerializeField] private AudioClip clipSpinStart;
    [SerializeField] private AudioClip clipPrimaryActionButton;
    [SerializeField] private AudioClip clipGeneralButtonClick;
    [SerializeField] private AudioClip clipPopupOpenClose;
    [SerializeField] private AudioClip clipAutoplayPanelOpen;
    [Tooltip("Currently unused — nothing calls PlayFeatureOpenLoop. Kept deliberately.")]
    [SerializeField] private AudioClip clipFeatureOpenLoop;
    [SerializeField] private AudioClip clipFreeSpinBg;
    [SerializeField] private AudioClip clipWinLinePhase1Start;
    [SerializeField] private AudioClip clipReelStop;
    [Tooltip("Plays once for any reel that lands at least one wild in the active rows.")]
    [SerializeField] private AudioClip clipWildLand;
    [Tooltip("Plays once for any reel that lands at least one bonus in the active rows.")]
    [SerializeField] private AudioClip clipBonusLand;
    [SerializeField] private AudioClip clipTurboButton;

    [Header("Audio Clips - Free Games")]
    [SerializeField] private AudioClip clipFreeGamesAwarded;
    [SerializeField] private AudioClip clipChooseFreeGames;
    [SerializeField] private AudioClip clipCardReveal;

    private bool _musicEnabled = true;
    private bool _sfxEnabled   = true;
    private float _musicVolume = 0.5f;
    private float _sfxVolume   = 1.0f;

    internal bool MusicEnabled => _musicEnabled;
    internal bool SfxEnabled   => _sfxEnabled;
    internal float MusicVolume => _musicVolume;
    internal float SfxVolume   => _sfxVolume;

    internal void SetMusicEnabled(bool on)
    {
        _musicEnabled = on;
        PlayerPrefs.SetInt(PrefKeyMusic, on ? 1 : 0);
        PlayerPrefs.Save();
        ApplyMusicVolume();
    }

    internal void SetSfxEnabled(bool on)
    {
        _sfxEnabled = on;
        PlayerPrefs.SetInt(PrefKeysfx, on ? 1 : 0);
        PlayerPrefs.Save();
        ApplySfxVolume();
    }

    internal void SetMusicVolume(float volume)
    {
        _musicVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(PrefKeyMusicVol, _musicVolume);
        PlayerPrefs.Save();
        ApplyMusicVolume();
    }

    internal void SetSfxVolume(float volume)
    {
        _sfxVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(PrefKeySfxVol, _sfxVolume);
        PlayerPrefs.Save();
        ApplySfxVolume();
    }

    private void ApplyMusicVolume()
    {
        if (bgMusicSource == null) return;
        bgMusicSource.volume = _musicEnabled ? _musicVolume : 0f;
    }

    private void ApplySfxVolume()
    {
        float v = _sfxEnabled ? _sfxVolume : 0f;
        if (uiSource           != null) uiSource.volume           = v;
        if (wheelSegmentSource != null) wheelSegmentSource.volume = v;
        if (reserveSource      != null) reserveSource.volume      = v;
        if (spinLoopSource     != null) spinLoopSource.volume     = v;
    }

    /// <summary>
    /// Uses UI source (AudioSource 2). If busy/playing, falls back to reserve source (AudioSource 4).
    /// </summary>
    private void PlayUISound(AudioClip clip)
    {
        if (!_sfxEnabled || clip == null) return;

        if (uiSource != null && !uiSource.isPlaying)
        {
            uiSource.PlayOneShot(clip);
        }
        else if (reserveSource != null)
        {
            reserveSource.PlayOneShot(clip);
        }
        else if (uiSource != null)
        {
            uiSource.PlayOneShot(clip);
        }
    }

    // MUSIC-BED loops only — it sets the source's volume from the music slider. Anything that
    // should follow the sfx slider belongs in PlaySfxLoop below; using this for an effect leaves
    // the source stuck at music volume, which is how the big-win and bonus-trigger sounds ended up
    // ignoring the sfx setting.
    private void PlayLoop(AudioSource source, AudioClip clip)
    {
        if (source == null || clip == null) return;
        source.clip   = clip;
        source.loop   = true;
        source.volume = _musicEnabled ? _musicVolume : 0f;
        source.Play();
    }

    // PlayLoop's sfx-volume twin. The loops above are all music-bed sounds; a looping *effect*
    // has to follow the sfx toggle instead, or muting sfx would leave it audible.
    private void PlaySfxLoop(AudioSource source, AudioClip clip)
    {
        if (source == null || clip == null) return;
        source.clip   = clip;
        source.loop   = true;
        source.volume = _sfxEnabled ? _sfxVolume : 0f;
        source.Play();
    }

    private void StopSource(AudioSource source)
    {
        if (source == null) return;
        source.Stop();
        source.loop = false;
    }

    // 1. Game Main BG
    internal void PlayBgMusic()
    {
        if (bgMusicSource == null || clipGameMainBg == null) return;
        if (bgMusicSource.isPlaying && bgMusicSource.clip == clipGameMainBg) return;

        bgMusicSource.clip   = clipGameMainBg;
        bgMusicSource.loop   = true;
        bgMusicSource.volume = _musicEnabled ? _musicVolume : 0f;
        bgMusicSource.Play();
    }

    internal void PlayMainBg() => PlayBgMusic();

    internal void StopBgMusic()
    {
        StopSource(bgMusicSource);
    }

    // 2. Bet Plus / Bet Minus (one for both)
    internal void PlayBetPlusMinus()
    {
        PlayUISound(clipBetPlusMinus);
    }

    internal void PlayBetPlus()  => PlayBetPlusMinus();
    internal void PlayBetMinus() => PlayBetPlusMinus();

    // 3. Max Bet Reached
    internal void PlayMaxBetReached()
    {
        PlayUISound(clipMaxBetReached);
    }

    // 4. Bonus-trigger stinger — a one-shot, despite the CNY-era "Loop" in the name. It used to go
    // through PlayLoop, which sets loop = true, and the matching Stop method had no callers — so the
    // clip repeated for the rest of the session from the moment free games triggered. PlayUISound
    // already null-guards and honours _sfxEnabled, so no guard is needed here.
    internal void Play3UspinWinLineLoop()
    {
        PlayUISound(clip3UspinWinLineLoop);
    }

    // 5. Win Object BG (Play at Open)
    internal void PlayWinObjectBg()
    {
        if (!_sfxEnabled || clipWinObjectBg == null) return;
        // PlaySfxLoop, not PlayLoop: this is an effect, not a music bed. PlayLoop stamps the source
        // with the *music* volume and StopSource never restores it, so every later UI sound on
        // uiSource kept playing at music level until something touched a volume slider.
        PlaySfxLoop(uiSource, clipWinObjectBg);
    }

    internal void StopWinObjectBg()
    {
        if (uiSource != null && uiSource.clip == clipWinObjectBg)
        {
            StopSource(uiSource);
        }
        if (reserveSource != null && reserveSource.clip == clipWinObjectBg)
        {
            StopSource(reserveSource);
        }
    }

    // 6. Stop / Take / AutoplayStop / WheelStart Btn Sound
    internal void PlayPrimaryActionButton()
    {
        PlayUISound(clipPrimaryActionButton != null ? clipPrimaryActionButton : clipGeneralButtonClick);
    }

    // Spin is a *duration* sound, not a button click: it loops for as long as the reels turn and is
    // cut by StopSpinLoop at landing. Played as a one-shot it ran on past the landing (the clip is
    // several seconds long) and stacked a fresh copy on every autoplay spin, since PlayOneShot never
    // cancels the previous one.
    internal void PlaySpinStart()
    {
        if (!_sfxEnabled) return;

        if (clipSpinStart != null)
        {
            PlaySfxLoop(spinLoopSource, clipSpinStart);
        }
        else
        {
            // No spin clip assigned: fall back to the shared primary-action click as a one-shot.
            // Looping a button click would be worse than the missing sound it stands in for.
            PlayUISound(clipPrimaryActionButton);
        }
    }

    // Safe to call when nothing is playing, which is what lets the two call sites in the reel-stop
    // path both fire without coordinating.
    internal void StopSpinLoop()
    {
        StopSource(spinLoopSource);
    }

    internal void PlaySpinStop()     => PlayPrimaryActionButton();
    internal void PlayTakeButton()   => PlayPrimaryActionButton();
    internal void PlayAutoplayStop() => PlayPrimaryActionButton();
    internal void PlayWheelStart()   => PlayPrimaryActionButton();

    // 7. General Button Click
    internal void PlayButton()
    {
        PlayUISound(clipGeneralButtonClick);
    }

    internal void PlayGeneralButtonClick() => PlayButton();

    // 8. Popup Open Close Sound
    internal void PlayPopupOpenClose()
    {
        PlayUISound(clipPopupOpenClose != null ? clipPopupOpenClose : clipGeneralButtonClick);
    }

    internal void PlayPopupClose() => PlayPopupOpenClose();
    internal void PlayPopupOpen()  => PlayPopupOpenClose();

    // 9. Autoplay Panel Open Sound
    internal void PlayAutoplayPanelOpen()
    {
        PlayUISound(clipAutoplayPanelOpen != null ? clipAutoplayPanelOpen : clipPopupOpenClose);
    }

    // 10. Bonus Wheel & MoneyBag Feature Open Sound (loop until feature enabled)
    internal void PlayFeatureOpenLoop()
    {
        if (clipFeatureOpenLoop == null) return;
        PlayLoop(bgMusicSource, clipFeatureOpenLoop);
    }

    internal void StopFeatureOpenLoop()
    {
        if (bgMusicSource != null && bgMusicSource.clip == clipFeatureOpenLoop)
        {
            StopBgMusic();
            PlayBgMusic(); // Resume main BG
        }
    }

    // 11. FreeSpin BG (loop while free spin)
    internal void PlayFreeSpinBg()
    {
        if (clipFreeSpinBg == null) return;
        PlayLoop(bgMusicSource, clipFreeSpinBg);
    }

    // 13. Win Line Phase 1 Start
    internal void PlayWinLinePhase1Start()
    {
        PlayUISound(clipWinLinePhase1Start);
    }

    // 14. Slot Reel Column Stop Sound
    internal void PlayReelStop()
    {
        if (!_sfxEnabled || clipReelStop == null) return;

        if (wheelSegmentSource != null)
            wheelSegmentSource.PlayOneShot(clipReelStop);
        else
            PlayUISound(clipReelStop);
    }

    // 15. Special symbol landings — one shot per reel that contains at least one, not per symbol.
    internal void PlayWildLand()  => PlayUISound(clipWildLand);
    internal void PlayBonusLand() => PlayUISound(clipBonusLand);

    // 16. Turbo / spin-speed toggle
    internal void PlayTurboButton() => PlayUISound(clipTurboButton);

    // 17. Free games presentation cues
    internal void PlayFreeGamesAwarded() => PlayUISound(clipFreeGamesAwarded);
    internal void PlayChooseFreeGames()  => PlayUISound(clipChooseFreeGames);
    internal void PlayCardReveal()       => PlayUISound(clipCardReveal);

    private bool isForceMuted = false;

    internal void SetMuteAll(bool forceMute)
    {
        if (forceMute == isForceMuted) return;
        isForceMuted = forceMute;

        AudioListener.volume = forceMute ? 0f : 1f;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        SetMuteAll(!hasFocus);
    }

    private void OnApplicationPause(bool isPaused)
    {
        SetMuteAll(isPaused);
    }
}
