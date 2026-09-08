using System;
using System.Collections;
using System.Collections.Generic;
using HClTraining.SharedXR;
using UnityEngine;

/// <summary>
/// Plays PPE room TTS cues in order. Auto-hooks sequence / equip / blocked events.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class PPETTSManager : MonoBehaviour
{
    [Serializable]
    public class CueClip
    {
        public PPETTSCue cue;
        public AudioClip clip;
    }

    public static PPETTSManager Instance { get; private set; }
    public event Action<PPETTSCue> CueStarted;
    public bool PlaysIntroOnStart => playIntroTutorialOnStart;

    [SerializeField] List<CueClip> clips = new List<CueClip>();
    [SerializeField] [Range(0f, 1f)] float volume = 1f;
    [SerializeField] bool playIntroTutorialOnStart = true;
    [SerializeField] [Min(0f)] float introStartDelay = 0.35f;
    [SerializeField] bool autoHookManagers = true;
    [SerializeField]
    [Tooltip("Show Korean subtitles on PPEEquipFeedbackUI while each TTS cue plays.")]
    bool showSubtitles;

    [SerializeField]
    PPEEquipFeedbackUI subtitleFeedback;

    AudioSource audioSource;
    readonly Queue<PPETTSCue> queue = new Queue<PPETTSCue>();
    readonly Dictionary<PPETTSCue, AudioClip> lookup = new Dictionary<PPETTSCue, AudioClip>();
    Coroutine playRoutine;
    Coroutine introRoutine;
    bool hooked;
    bool initialBriefingPending;

    public bool IsBusy => initialBriefingPending || playRoutine != null || queue.Count > 0 ||
                          (audioSource != null && audioSource.isPlaying);

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[PPE TTS] Multiple PPETTSManager instances. Keeping the first.", this);
            enabled = false;
            return;
        }

        Instance = this;
        initialBriefingPending = playIntroTutorialOnStart;
        audioSource = GetComponent<AudioSource>();
        ConfigureAudioSource(audioSource);
        RebuildLookup();
        Debug.Log($"[PPE TTS] Ready. assignedClips={lookup.Count}/37 playIntro={playIntroTutorialOnStart}", this);
    }

    void OnEnable()
    {
        if (autoHookManagers)
            TryHook(true);
    }

    void Start()
    {
        EnsureCameraAudioSource();
        ConfigureAudioSource(audioSource);
        RebuildLookup();
        PreloadAllClips();

        if (autoHookManagers)
            TryHook(true);

        if (playIntroTutorialOnStart)
            introRoutine = StartCoroutine(PlayIntroAfterDelay());
    }

    void EnsureCameraAudioSource()
    {
        // Prefer the XR Main Camera listener — PPE_TTS lives under PPE_System in the scene
        // (world root), which is the wrong place for VO if spatialBlend ever drifts above 0.
        AudioListener listener = null;
        if (Camera.main != null)
            listener = Camera.main.GetComponent<AudioListener>();
        if (listener == null)
            listener = FindAnyObjectByType<AudioListener>(FindObjectsInactive.Exclude);
        if (listener == null)
            listener = FindAnyObjectByType<AudioListener>(FindObjectsInactive.Include);

        if (listener == null)
        {
            Debug.LogWarning("[PPE TTS] No AudioListener found — VO may be silent on device.", this);
        }
        // This is a 2D source, so it does not need to be parented to the listener. Keeping the
        // manager in the PPE scene guarantees that scene unload also ends its audio on Quest.

        // Always play through THIS object's AudioSource — never steal/hijack the camera's.
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        ConfigureAudioSource(audioSource);
        audioSource.volume = Mathf.Clamp01(volume <= 0f ? 1f : volume);

        AudioListener.pause = false;
        AudioListener.volume = 1f;

        var listenerCount = FindObjectsByType<AudioListener>(FindObjectsInactive.Include).Length;
        Debug.Log(
            $"[PPE TTS] AudioSource on '{gameObject.name}' enabled={audioSource.enabled} vol={audioSource.volume} mute={audioSource.mute} spatial={audioSource.spatialBlend} parent='{(transform.parent != null ? transform.parent.name : "null")}' listeners={listenerCount}.",
            this);
    }

    /// <summary>
    /// Re-open Unity's WASAPI stream against the current Windows default device
    /// (Quest Link → Oculus Virtual Audio Device). Mono→Stereo forces a full driver reopen
    /// (known workaround for silent VR audio after XR SetOutput failure).
    /// </summary>
    static void RebindAudioOutputToCurrentDevice()
    {
        var cfg = AudioSettings.GetConfiguration();
        cfg.speakerMode = AudioSpeakerMode.Mono;
        var okMono = AudioSettings.Reset(cfg);

        cfg = AudioSettings.GetConfiguration();
        cfg.speakerMode = AudioSpeakerMode.Stereo;
        var okStereo = AudioSettings.Reset(cfg);

        AudioListener.pause = false;
        AudioListener.volume = 1f;
        Debug.Log(
            $"[PPE TTS] Audio rebind Mono→Stereo ok={okMono}/{okStereo} " +
            $"xrActive={UnityEngine.XR.XRSettings.isDeviceActive} " +
            $"outputRate={AudioSettings.outputSampleRate} speakerMode={AudioSettings.GetConfiguration().speakerMode} " +
            $"dsp={AudioSettings.GetConfiguration().dspBufferSize}.");
    }

    void RebuildLookup()
    {
        lookup.Clear();
        var fromFinalCourseResources = 0;
        var fromInspector = 0;
        var fromLegacyResources = 0;

        // 1) The final-course narration is authored centrally so the additive App flow and
        // standalone PPE scene say exactly the same thing without Scene reference rewrites.
        var finalCourseClips = AddNumberedResourceClips(
            "TrainingNarration/PPE",
            overwriteExisting: true,
            ref fromFinalCourseResources);

        // 2) Keep Inspector / scene-assigned PCM clips as a compatibility fallback.
        if (clips != null)
        {
            for (var i = 0; i < clips.Count; i++)
            {
                var entry = clips[i];
                if (entry == null || entry.clip == null || entry.clip.samples <= 0)
                    continue;
                if (lookup.ContainsKey(entry.cue))
                    continue;
                lookup[entry.cue] = entry.clip;
                fromInspector++;
            }
        }

        // 3) Preserve the older Resources path as the final fallback.
        var legacyResourceClips = AddNumberedResourceClips(
            "PPE/TTS",
            overwriteExisting: false,
            ref fromLegacyResources);

        Debug.Log(
            $"[PPE TTS] Lookup built: {lookup.Count}/37 final={fromFinalCourseResources} " +
            $"inspectorFallback={fromInspector} legacyFallback={fromLegacyResources} " +
            $"finalFiles={(finalCourseClips != null ? finalCourseClips.Length : 0)} " +
            $"legacyFiles={(legacyResourceClips != null ? legacyResourceClips.Length : 0)}.",
            this);
    }

    AudioClip[] AddNumberedResourceClips(string path, bool overwriteExisting, ref int addedCount)
    {
        var resourceClips = Resources.LoadAll<AudioClip>(path);
        if (resourceClips == null)
            return null;

        for (var i = 0; i < resourceClips.Length; i++)
        {
            var clip = resourceClips[i];
            if (clip == null || string.IsNullOrEmpty(clip.name))
                continue;

            // Expect names like 01_EmergencyIntro (no extension in Resources path).
            var underscore = clip.name.IndexOf('_');
            if (underscore < 2)
                continue;
            if (!int.TryParse(clip.name.Substring(0, underscore), out var index))
                continue;
            if (index < 1 || index > 37)
                continue;

            var cue = (PPETTSCue)index;
            if (!overwriteExisting && lookup.ContainsKey(cue))
                continue;
            if (clip.samples <= 0)
                continue;

            lookup[cue] = clip;
            addedCount++;
        }

        return resourceClips;
    }

    static void ConfigureAudioSource(AudioSource source)
    {
        if (source == null)
            return;

        source.enabled = true;
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f; // 2D narration — never positional
        source.spatialize = false;
        source.mute = false;
        source.volume = 1f;
        source.ignoreListenerPause = false;
        source.bypassListenerEffects = true;
        source.priority = 0;
        source.dopplerLevel = 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 1f;
        source.maxDistance = 500f;
        source.outputAudioMixerGroup = null;
    }

    void PreloadAllClips()
    {
        var ok = 0;
        var bad = 0;
        foreach (var pair in lookup)
        {
            var clip = pair.Value;
            if (clip == null)
            {
                bad++;
                continue;
            }

            if (clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();

            if (clip.samples <= 0 || clip.length <= 0.01f)
            {
                Debug.LogError(
                    $"[PPE TTS] Clip '{clip.name}' for {pair.Key} looks empty (samples={clip.samples}, len={clip.length}). Reimport as PCM 16-bit.",
                    clip);
                bad++;
            }
            else
            {
                ok++;
            }
        }

        Debug.Log($"[PPE TTS] Preload check ok={ok} bad={bad}.", this);
    }

    void OnDisable()
    {
        TryHook(false);
        StopAll();
    }

    void OnDestroy()
    {
        TryHook(false);
        StopAll();
        if (Instance == this)
            Instance = null;
    }

    IEnumerator PlayIntroAfterDelay()
    {
        // OpenXR+Link often fails "XR: Error setting active audio output driver" and leaves
        // Unity DSP running (isPlaying / OnAudioFilterRead OK) while WASAPI to Oculus VAD
        // stays silent (Volume Mixer flat). Wait for XR, then force Mono→Stereo rebind.
        var wait = 0f;
        while (wait < 2.5f && !UnityEngine.XR.XRSettings.isDeviceActive)
        {
            wait += SharedTrainingPause.UnscaledDeltaTime;
            yield return null;
        }

        yield return null;
        RebindAudioOutputToCurrentDevice();
        yield return null;

        if (introStartDelay > 0f)
            yield return new WaitForSeconds(introStartDelay);

        // The legacy Grip-button equip instruction is intentionally omitted.
        // Placement guides now teach the interaction while the current-step prompt names the PPE.
        Enqueue(
            PPETTSCue.EmergencyIntro,
            PPETTSCue.WearEssentialPPE,
            PPETTSCue.WearInOrder);

        var sequence = PPESequenceManager.Resolve();
        EnqueuePromptForStep(sequence != null ? sequence.CurrentRequiredStep : null);
        KickPlay();
        introRoutine = null;
    }

    public void Play(PPETTSCue cue)
    {
        Enqueue(cue);
        KickPlay();
    }

    public void PlayNow(PPETTSCue cue)
    {
        queue.Clear();
        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
            playRoutine = null;
        }

        if (audioSource != null && audioSource.isPlaying)
            audioSource.Stop();

        Enqueue(cue);
        KickPlay();
    }

    public void Enqueue(params PPETTSCue[] cues)
    {
        if (cues == null)
            return;
        for (var i = 0; i < cues.Length; i++)
            queue.Enqueue(cues[i]);
    }

    public void PlayMovingToFieldEntrance()
    {
        Play(PPETTSCue.MovingToFieldEntrance);
    }

    void KickPlay()
    {
        if (playRoutine != null || queue.Count == 0)
            return;
        playRoutine = StartCoroutine(PlayQueueRoutine());
    }

    IEnumerator PlayQueueRoutine()
    {
        while (queue.Count > 0)
        {
            var cue = queue.Dequeue();
            if (!lookup.TryGetValue(cue, out var clip) || clip == null)
            {
                Debug.LogError(
                    $"[PPE TTS] ERROR AudioClip is null for requested TTS ID {(int)cue:00}_{cue}. Subtitle may still show if another path drives UI.",
                    this);
                // Keep subtitle timing for this cue even when audio is missing.
                ShowSubtitleForCue(cue, 2.5f);
                continue;
            }

            // Clips imported with preloadAudioData=0 never become isPlaying unless loaded first —
            // the old wait loop then skipped the entire intro queue silently.
            if (clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();

            var loadWait = 0f;
            while (clip.loadState == AudioDataLoadState.Loading && loadWait < 8f)
            {
                loadWait += SharedTrainingPause.UnscaledDeltaTime;
                yield return null;
            }

            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                Debug.LogError(
                    $"[PPE TTS] ERROR clip not loaded for TTS ID {(int)cue:00}_{cue} name='{clip.name}' state={clip.loadState}.",
                    clip);
                ShowSubtitleForCue(cue, Mathf.Max(2.5f, clip.length));
                continue;
            }

            if (clip.samples <= 0)
            {
                Debug.LogError($"[PPE TTS] ERROR empty clip for TTS ID {(int)cue:00}_{cue} name='{clip.name}'.", clip);
                ShowSubtitleForCue(cue, 2.5f);
                continue;
            }

            if (audioSource == null || audioSource.gameObject != gameObject)
                EnsureCameraAudioSource();

            if (audioSource == null)
            {
                Debug.LogError($"[PPE TTS] ERROR AudioSource is null for TTS ID {(int)cue:00}_{cue}.", this);
                ShowSubtitleForCue(cue, clip.length);
                continue;
            }

            if (!gameObject.activeInHierarchy || !audioSource.enabled)
            {
                Debug.LogError(
                    $"[PPE TTS] ERROR cannot play TTS ID {(int)cue:00}_{cue}: goActive={gameObject.activeInHierarchy} sourceEnabled={audioSource.enabled}.",
                    this);
            }

            ConfigureAudioSource(audioSource);
            audioSource.mute = false;
            audioSource.volume = Mathf.Clamp01(volume <= 0f ? 1f : volume);
            audioSource.spatialBlend = 0f;
            audioSource.Stop();
            audioSource.clip = clip;
            // Play() (not PlayOneShot) so isPlaying / Stop stay reliable on Quest.
            audioSource.Play();
            CueStarted?.Invoke(cue);

            LogPlaybackDiagnostics(cue, clip, "Play");

            // One-frame later: confirm something actually started (or was immediately stopped).
            yield return null;
            LogPlaybackDiagnostics(cue, clip, "Play+1f");
            if (audioSource != null && !audioSource.isPlaying)
            {
                Debug.LogError(
                    $"[PPE TTS] ERROR TTS ID {(int)cue:00}_{cue} isPlaying=false one frame after Play(). Clip may be unloadable or another script Stop()'d it.",
                    this);
            }

            var wait = Mathf.Max(0.05f, clip.length / Mathf.Max(0.01f, audioSource != null ? audioSource.pitch : 1f));
            ShowSubtitleForCue(cue, wait);

            // Audio playback is the clock. A second training-time countdown can lag
            // behind the audible clip after focus/presence changes and insert silence.
            while (audioSource != null && audioSource.clip == clip &&
                   (audioSource.isPlaying || SharedTrainingPause.IsPaused || AudioListener.pause))
                yield return null;
        }

        playRoutine = null;
        initialBriefingPending = false;
    }

    public void StopAll()
    {
        queue.Clear();
        initialBriefingPending = false;

        if (introRoutine != null)
        {
            StopCoroutine(introRoutine);
            introRoutine = null;
        }

        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
            playRoutine = null;
        }

        if (audioSource != null)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }

        if (subtitleFeedback == null)
            subtitleFeedback = FindAnyObjectByType<PPEEquipFeedbackUI>(FindObjectsInactive.Include);
        subtitleFeedback?.HideImmediate();
    }

    void LogPlaybackDiagnostics(PPETTSCue cue, AudioClip clip, string phase)
    {
        var listener = FindAnyObjectByType<AudioListener>(FindObjectsInactive.Exclude);
        Debug.Log(
            $"[PPE TTS][{phase}] id={(int)cue:00}_{cue}" +
            $" clip={(clip != null ? clip.name : "NULL")}" +
            $" clipRef={(clip != null ? "ok" : "null")}" +
            $" samples={(clip != null ? clip.samples : -1)}" +
            $" load={(clip != null ? clip.loadState.ToString() : "n/a")}" +
            $" src={(audioSource != null ? audioSource.gameObject.name : "NULL")}" +
            $" srcEnabled={(audioSource != null && audioSource.enabled)}" +
            $" goActive={gameObject.activeInHierarchy}" +
            $" vol={(audioSource != null ? audioSource.volume : -1f)}" +
            $" mute={(audioSource != null && audioSource.mute)}" +
            $" spatial={(audioSource != null ? audioSource.spatialBlend : -1f)}" +
            $" isPlaying={(audioSource != null && audioSource.isPlaying)}" +
            $" listener={(listener != null ? listener.name : "NONE")}" +
            $" listenerOn={(listener != null && listener.enabled)}" +
            $" parent={(transform.parent != null ? transform.parent.name : "null")}",
            this);
    }

    /// <summary>
    /// Editor / Play Mode: right-click PPETTSManager → Test TTS Audio.
    /// Isolates A) clip import  B) AudioSource config  C) event/mapping.
    /// </summary>
    [ContextMenu("Test TTS Audio")]
    public void TestTTSAudio()
    {
        EnsureCameraAudioSource();
        RebuildLookup();

        AudioClip clip = null;
        if (lookup.TryGetValue(PPETTSCue.EmergencyIntro, out var mapped))
            clip = mapped;

        if (clip == null && clips != null)
        {
            for (var i = 0; i < clips.Count; i++)
            {
                if (clips[i] != null && clips[i].cue == PPETTSCue.EmergencyIntro && clips[i].clip != null)
                {
                    clip = clips[i].clip;
                    break;
                }
            }
        }

        if (clip == null)
            clip = Resources.Load<AudioClip>("PPE/TTS/01_EmergencyIntro");

        if (clip == null)
        {
            Debug.LogError("[PPE TTS] TestTTSAudio FAILED: AudioClip null for 01_EmergencyIntro (Inspector + Resources).", this);
            return;
        }

        if (audioSource == null)
        {
            Debug.LogError("[PPE TTS] TestTTSAudio FAILED: AudioSource is null.", this);
            return;
        }

        if (clip.loadState == AudioDataLoadState.Unloaded)
            clip.LoadAudioData();

        ConfigureAudioSource(audioSource);
        audioSource.mute = false;
        audioSource.volume = 1f;
        audioSource.spatialBlend = 0f;
        audioSource.Stop();
        audioSource.clip = clip;
        audioSource.Play();

        LogPlaybackDiagnostics(PPETTSCue.EmergencyIntro, clip, "TestTTSAudio");
        Debug.Log(
            $"[PPE TTS] TestTTSAudio started. If you hear this, clips+AudioSource are OK (problem is event/mapping). If silent, check import/listener/output.",
            this);
    }

    void ShowSubtitleForCue(PPETTSCue cue, float durationSeconds)
    {
        if (!showSubtitles)
            return;

        var text = PPETTSSubtitles.Get(cue);
        if (string.IsNullOrEmpty(text))
            return;

        if (subtitleFeedback == null)
            subtitleFeedback = FindAnyObjectByType<PPEEquipFeedbackUI>(FindObjectsInactive.Include);

        if (subtitleFeedback == null)
        {
            Debug.LogWarning("[PPE TTS] PPEEquipFeedbackUI missing — subtitle skipped.", this);
            return;
        }

        // Hold a bit past clip end so the last words remain readable.
        subtitleFeedback.ShowMessage(text, durationSeconds + 0.35f);
    }

    void TryHook(bool subscribe)
    {
        var sequence = PPESequenceManager.Resolve();

        if (subscribe)
        {
            if (hooked)
                return;

            if (sequence != null)
            {
                sequence.OnRequiredStepChanged += OnRequiredStepChanged;
                sequence.OnPhaseChanged += OnPhaseChanged;
                sequence.OnPPESequenceCompleted += OnSequenceCompleted;
            }

            hooked = sequence != null;
            return;
        }

        if (!hooked)
            return;

        if (sequence != null)
        {
            sequence.OnRequiredStepChanged -= OnRequiredStepChanged;
            sequence.OnPhaseChanged -= OnPhaseChanged;
            sequence.OnPPESequenceCompleted -= OnSequenceCompleted;
        }

        hooked = false;
    }

    void OnRequiredStepChanged(PPESequenceStep? step)
    {
        // Skip the very first prompt during intro tutorial — intro coroutine already queues it.
        if (playIntroTutorialOnStart && Time.timeSinceLevelLoad < introStartDelay + 0.5f)
            return;

        EnqueuePromptForStep(step);
        KickPlay();
    }

    void OnPhaseChanged(PPETrainingPhase previous, PPETrainingPhase next)
    {
        if (previous == PPETrainingPhase.Donning && next == PPETrainingPhase.EquipmentPreparation)
            Enqueue(PPETTSCue.EssentialPPEComplete);
        KickPlay();
    }

    void OnSequenceCompleted()
    {
        Enqueue(
            PPETTSCue.AllFieldPrepComplete,
            PPETTSCue.GoToFieldEntrance);
        KickPlay();
    }

    /// <summary>Called from sequence gate when equip is blocked.</summary>
    public void NotifyEquipBlocked(string itemId)
    {
        var sequence = PPESequenceManager.Resolve();
        var required = sequence != null ? sequence.CurrentRequiredStep : null;

        if (required == PPESequenceStep.InnerGloves)
        {
            PlayNow(PPETTSCue.WearGlovesFirst);
            return;
        }

        if (required == PPESequenceStep.ResponseBag
            && string.Equals(itemId, PPEEquipmentManager.IdResponseBag, StringComparison.Ordinal)
            && sequence != null && !sequence.BagContentsChecked)
        {
            PlayNow(PPETTSCue.PrepareResponseBagFirst);
            return;
        }

        if (string.Equals(itemId, PPEEquipmentManager.IdChemicalGloves, StringComparison.Ordinal)
            && required != null
            && required != PPESequenceStep.ProtectiveGloves)
        {
            PlayNow(PPETTSCue.OuterGlovesAfterRespirator);
            return;
        }

        if (required == PPESequenceStep.ResponseBag
            || required == PPESequenceStep.WasteContainer)
        {
            if (!IsPrepItem(itemId))
            {
                PlayNow(PPETTSCue.CompleteEssentialPPEFirst);
                return;
            }
        }

        PlayNow(PPETTSCue.WrongPPEOrder);
    }

    public void NotifyAlreadyEquipped()
    {
        PlayNow(PPETTSCue.AlreadyEquipped);
    }

    static bool IsPrepItem(string itemId)
    {
        return string.Equals(itemId, PPEEquipmentManager.IdResponseBag, StringComparison.Ordinal)
               || string.Equals(itemId, PPEEquipmentManager.IdWasteContainer, StringComparison.Ordinal);
    }

    void EnqueuePromptForStep(PPESequenceStep? step)
    {
        if (step == null)
            return;

        switch (step.Value)
        {
            case PPESequenceStep.InnerGloves:
                Enqueue(PPETTSCue.WearInnerGloves);
                break;
            case PPESequenceStep.ProtectiveSuit:
                Enqueue(PPETTSCue.WearChemicalSuit);
                break;
            case PPESequenceStep.ChemicalBoots:
                Enqueue(PPETTSCue.WearChemicalBoots);
                break;
            case PPESequenceStep.Respirator:
                Enqueue(PPETTSCue.WearRespirator);
                break;
            case PPESequenceStep.ResponseBag:
                Enqueue(PPETTSCue.PrepareResponseBag);
                break;
            case PPESequenceStep.FaceProtection:
                Enqueue(PPETTSCue.WearFaceProtection);
                break;
            case PPESequenceStep.ProtectiveGloves:
                Enqueue(PPETTSCue.WearOuterGloves);
                break;
            case PPESequenceStep.WasteContainer:
                Enqueue(PPETTSCue.PrepareWasteContainer);
                break;
        }
    }

    public void NotifyStepCompleted(PPESequenceStep step)
    {
        PPETTSCue completionCue;
        switch (step)
        {
            case PPESequenceStep.InnerGloves:
                completionCue = PPETTSCue.InnerGlovesEquipped;
                break;
            case PPESequenceStep.ProtectiveSuit:
                completionCue = PPETTSCue.ChemicalSuitEquipped;
                break;
            case PPESequenceStep.ChemicalBoots:
                completionCue = PPETTSCue.ChemicalBootsEquipped;
                break;
            case PPESequenceStep.Respirator:
                completionCue = PPETTSCue.RespiratorEquipped;
                break;
            case PPESequenceStep.FaceProtection:
                completionCue = PPETTSCue.FaceProtectionEquipped;
                break;
            case PPESequenceStep.ProtectiveGloves:
                completionCue = PPETTSCue.OuterGlovesEquipped;
                break;
            case PPESequenceStep.ResponseBag:
                completionCue = PPETTSCue.ResponseBagReady;
                break;
            case PPESequenceStep.WasteContainer:
                completionCue = PPETTSCue.WasteContainerReady;
                break;
            default:
                return;
        }

        // A completed action must acknowledge immediately. Leaving the old step prompt
        // in the queue made the confirmation arrive several seconds late and delayed the
        // following ResponseBag -> WasteContainer prompt.
        PlayNow(completionCue);
    }

#if UNITY_EDITOR
    public void EditorSetClips(List<CueClip> assigned)
    {
        clips = assigned ?? new List<CueClip>();
        RebuildLookup();
    }
#endif
}
