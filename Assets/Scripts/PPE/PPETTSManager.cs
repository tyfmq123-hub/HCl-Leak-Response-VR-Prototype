using System;
using System.Collections;
using System.Collections.Generic;
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

    [SerializeField] List<CueClip> clips = new List<CueClip>();
    [SerializeField] [Range(0f, 1f)] float volume = 1f;
    [SerializeField] bool playIntroTutorialOnStart = true;
    [SerializeField] [Min(0f)] float introStartDelay = 0.35f;
    [SerializeField] bool autoHookManagers = true;

    AudioSource audioSource;
    readonly Queue<PPETTSCue> queue = new Queue<PPETTSCue>();
    readonly Dictionary<PPETTSCue, AudioClip> lookup = new Dictionary<PPETTSCue, AudioClip>();
    Coroutine playRoutine;
    bool hooked;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[PPE TTS] Multiple PPETTSManager instances. Keeping the first.", this);
            enabled = false;
            return;
        }

        Instance = this;
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = volume;
        RebuildLookup();
    }

    void OnEnable()
    {
        if (autoHookManagers)
            TryHook(true);
    }

    void Start()
    {
        if (autoHookManagers)
            TryHook(true);

        if (playIntroTutorialOnStart)
            StartCoroutine(PlayIntroAfterDelay());
    }

    void OnDisable()
    {
        TryHook(false);
    }

    void OnDestroy()
    {
        TryHook(false);
        if (Instance == this)
            Instance = null;
    }

    void RebuildLookup()
    {
        lookup.Clear();
        if (clips == null)
            return;

        for (var i = 0; i < clips.Count; i++)
        {
            var entry = clips[i];
            if (entry == null || entry.clip == null)
                continue;
            lookup[entry.cue] = entry.clip;
        }
    }

    IEnumerator PlayIntroAfterDelay()
    {
        if (introStartDelay > 0f)
            yield return new WaitForSeconds(introStartDelay);

        // Opening briefing + interaction tutorial, then current-step prompt.
        Enqueue(
            PPETTSCue.EmergencyIntro,
            PPETTSCue.WearEssentialPPE,
            PPETTSCue.WearInOrder,
            PPETTSCue.PointWithRay,
            PPETTSCue.OutlineShowsSelectable,
            PPETTSCue.TriggerSelectThenEquip,
            PPETTSCue.BButtonCancel);

        var sequence = PPESequenceManager.Resolve();
        EnqueuePromptForStep(sequence != null ? sequence.CurrentRequiredStep : null);
        KickPlay();
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
        if (playRoutine != null)
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
                Debug.LogWarning($"[PPE TTS] Missing clip for cue {cue} ({(int)cue}).", this);
                continue;
            }

            audioSource.clip = clip;
            audioSource.volume = volume;
            audioSource.Play();
            Debug.Log($"[PPE TTS] Play {(int)cue:00}_{cue}", this);

            while (audioSource != null && audioSource.isPlaying)
                yield return null;
        }

        playRoutine = null;
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

    void OnSequenceCompleted()
    {
        Enqueue(
            PPETTSCue.CheckingPrepStatus,
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
        switch (step)
        {
            case PPESequenceStep.InnerGloves:
                Enqueue(PPETTSCue.InnerGlovesEquipped);
                break;
            case PPESequenceStep.ProtectiveSuit:
                Enqueue(PPETTSCue.ChemicalSuitEquipped);
                break;
            case PPESequenceStep.ChemicalBoots:
                Enqueue(PPETTSCue.ChemicalBootsEquipped);
                break;
            case PPESequenceStep.Respirator:
                Enqueue(PPETTSCue.RespiratorEquipped);
                break;
            case PPESequenceStep.ResponseBag:
                Enqueue(PPETTSCue.ResponseBagReady);
                break;
            case PPESequenceStep.FaceProtection:
                Enqueue(PPETTSCue.FaceProtectionEquipped);
                break;
            case PPESequenceStep.ProtectiveGloves:
                Enqueue(PPETTSCue.OuterGlovesEquipped);
                Enqueue(PPETTSCue.EssentialPPEComplete);
                break;
            case PPESequenceStep.WasteContainer:
                Enqueue(PPETTSCue.WasteContainerReady);
                break;
        }

        KickPlay();
    }

#if UNITY_EDITOR
    public void EditorSetClips(List<CueClip> assigned)
    {
        clips = assigned ?? new List<CueClip>();
        RebuildLookup();
    }
#endif
}
