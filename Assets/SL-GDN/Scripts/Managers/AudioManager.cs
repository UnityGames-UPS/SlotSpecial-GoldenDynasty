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
    [SerializeField] private AudioClip clipScatterTrigger;
    [SerializeField] private AudioClip clipBigWin;
    [Tooltip("Spin button only. Stop / Take / AutoplayStop share clipPrimaryActionButton below.")]
    [SerializeField] private AudioClip clipSpinStart;
    [SerializeField] private AudioClip clipPrimaryActionButton;
    [SerializeField] private AudioClip clipGeneralButtonClick;
    [SerializeField] private AudioClip clipPopupOpenClose;
    [SerializeField] private AudioClip clipAutoplayPanelOpen;
    [SerializeField] private AudioClip clipFreeSpinBg;
    [SerializeField] private AudioClip clipWinPresentationStart;
    [SerializeField] private AudioClip clipReelStop;
    [Tooltip("One shot per reel that lands at least one Scatter. Currently UNASSIGNED and silent - kept because the call site is guarded and may be wanted again.")]
    [SerializeField] private AudioClip clipScatterLand;
    [SerializeField] private AudioClip clipTurboButton;

    [Header("Audio Clips - Golden Dynasty")]
    [Tooltip("The Mystery reveal. ONE shot for the whole reveal, not one per door — a spin can reveal up to 15 cells on the same frame.")]
    [SerializeField] private AudioClip clipMysteryDoorOpen;

    [Tooltip("Plays as the Winner graphic starts animating and the red holder takes over, at the end of a Hold & Spin round.")]
    [SerializeField] private AudioClip clipWinnerAnimation;

    [Tooltip("The Free Games round running out. Plays alone — the congratulations cue and its panel wait for this one to finish.")]
    [SerializeField] private AudioClip clipFreeGamesComplete;

    [Tooltip("Plays with the CongratulationsPanel, after clipFreeGamesComplete has finished.")]
    [SerializeField] private AudioClip clipCongratulations;

    [Tooltip("Phase 2 of the win presentation moving to the next win line. Fires on every change, and Phase 2 cycles until the player spins.")]
    [SerializeField] private AudioClip clipWinLineChange;

    [Tooltip("An Orb landing on a BASE-GAME reel. One shot per reel that contains at least one.")]
    [SerializeField] private AudioClip clipOrbLand;

    [Tooltip("An Orb landing during a Hold & Spin round. The feature's own counterpart to clipOrbLand.")]
    [SerializeField] private AudioClip clipOrbLandFeature;

    [Tooltip("6+ Orbs — the Hold & Spin trigger. Plays after every Orb has landed and BEFORE the full-screen intro.")]
    [SerializeField] private AudioClip clipHoldAndSpinTrigger;

    [Tooltip("Plays with the Hold & Spin full-screen intro animation, straight after clipHoldAndSpinTrigger.")]
    [SerializeField] private AudioClip clipHoldAndSpinIntro;

    [Tooltip("A Wild ANIMATING as part of a win — once per spin, in Phase 1 only. Wild landings have no cue.")]
    [SerializeField] private AudioClip clipWildAnimate;

    [Tooltip("One dragon leaving its Orb during the Hold & Spin payout walk. Fires per dragon.")]
    [SerializeField] private AudioClip clipDragonLeaveOrb;

    [Tooltip("The win amount counting up in the universal win popup.")]
    [SerializeField] private AudioClip clipWinCountUp;

    private bool _musicEnabled = true;
    private bool _sfxEnabled   = true;
    private float _musicVolume = 0.5f;
    private float _sfxVolume   = 1.0f;

    internal float MusicVolume => _musicVolume;
    internal float SfxVolume   => _sfxVolume;

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

    // 3. Max Bet Reached
    internal void PlayMaxBetReached()
    {
        PlayUISound(clipMaxBetReached);
    }

    // 4. Bonus-trigger stinger — a one-shot, despite the CNY-era "Loop" in the name. It used to go
    // through PlayLoop, which sets loop = true, and the matching Stop method had no callers — so the
    // clip repeated for the rest of the session from the moment free games triggered. PlayUISound
    // already null-guards and honours _sfxEnabled, so no guard is needed here.
    internal void PlayScatterTrigger()
    {
        PlayUISound(clipScatterTrigger);
    }

    // 5. Win Object BG (Play at Open)
    internal void PlayBigWin()
    {
        if (!_sfxEnabled || clipBigWin == null) return;
        // PlaySfxLoop, not PlayLoop: this is an effect, not a music bed. PlayLoop stamps the source
        // with the *music* volume and StopSource never restores it, so every later UI sound on
        // uiSource kept playing at music level until something touched a volume slider.
        PlaySfxLoop(uiSource, clipBigWin);
    }

    internal void StopBigWin()
    {
        if (uiSource != null && uiSource.clip == clipBigWin)
        {
            StopSource(uiSource);
        }
        if (reserveSource != null && reserveSource.clip == clipBigWin)
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

    // 7. General Button Click
    internal void PlayButton()
    {
        PlayUISound(clipGeneralButtonClick);
    }

    // 8. Popup Open Close Sound
    internal void PlayPopupOpenClose()
    {
        PlayUISound(clipPopupOpenClose != null ? clipPopupOpenClose : clipGeneralButtonClick);
    }

    internal void PlayPopupClose() => PlayPopupOpenClose();

    // 9. Autoplay Panel Open Sound
    internal void PlayAutoplayPanelOpen()
    {
        PlayUISound(clipAutoplayPanelOpen != null ? clipAutoplayPanelOpen : clipPopupOpenClose);
    }

    // 10. FreeSpin BG (loop while free spin)
    internal void PlayFreeSpinBg()
    {
        if (clipFreeSpinBg == null) return;
        PlayLoop(bgMusicSource, clipFreeSpinBg);
    }

    // 13. Win Line Phase 1 Start
    internal void PlayWinPresentationStart()
    {
        PlayUISound(clipWinPresentationStart);
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

    // 15. Scatter landing — one shot per reel that contains at least one, not per symbol. Wild
    // landings deliberately have NO cue in this game: the Wild is announced when it animates.
    internal void PlayScatterLand() => PlayUISound(clipScatterLand);

    // 16. Turbo / spin-speed toggle
    internal void PlayTurboButton() => PlayUISound(clipTurboButton);

    // 17. Golden Dynasty cues.
    internal void PlayWinLineChange()      => PlayUISound(clipWinLineChange);
    internal void PlayOrbLand()            => PlayUISound(clipOrbLand);
    internal void PlayOrbLandFeature()     => PlayUISound(clipOrbLandFeature);
    internal void PlayHoldAndSpinTrigger() => PlayUISound(clipHoldAndSpinTrigger);
    internal void PlayHoldAndSpinIntro()   => PlayUISound(clipHoldAndSpinIntro);
    internal void PlayWildAnimate()        => PlayUISound(clipWildAnimate);
    internal void PlayDragonLeaveOrb()     => PlayUISound(clipDragonLeaveOrb);
    internal void PlayWinCountUp()         => PlayUISound(clipWinCountUp);
    internal void PlayMysteryDoorOpen() => PlayUISound(clipMysteryDoorOpen);
    internal void PlayWinnerAnimation() => PlayUISound(clipWinnerAnimation);
    internal void PlayCongratulations() => PlayUISound(clipCongratulations);

    /// <summary>
    /// The Free Games completion cue. Returns how long it runs, so the caller can hold the
    /// congratulations panel until this has finished rather than hardcoding a duration that would
    /// silently stop matching if the clip were replaced.
    ///
    /// The length comes back whether or not the clip was audible: with sfx muted the outro should
    /// still be paced the same, not suddenly three seconds quicker.
    /// </summary>
    internal float PlayFreeGamesComplete()
    {
        PlayUISound(clipFreeGamesComplete);
        return clipFreeGamesComplete != null ? clipFreeGamesComplete.length : 0f;
    }

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
