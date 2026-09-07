using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VRProject.ObjectInstallation;
using VRProject.Valves;

namespace VRProject.LeakResponse.Narration
{
    public enum P102LeakNarrationCue
    {
        LeakSceneArrival = 1,
        SuctionValveActivated = 2,
        SuctionValveClosed = 3,
        DischargeValveActivated = 4,
        BothValvesClosed = 5,
        LeakReductionStarted = 6,
        LeakReductionCompleted = 7,
        AbsorbentSockPlacementActivated = 8,
        AllAbsorbentSocksPlaced = 9,
        AbsorbentPadPlacementActivated = 10,
        AllAbsorbentPadsPlaced = 11,
        AbsorptionCompleted = 12,
        ContainerAndLidGhostActivated = 13,
        ContainerLidStowed = 14,
        RecoveryTongsPickedUp = 15,
        PadRecoveryIdle = 16,
        AllPadsRecovered = 17,
        SockRecoveryStarted = 18,
        SockRecoveryIdle = 19,
        AllSocksRecovered = 20,
        RecoveryTongsReturned = 21,
        ContainerClosureGhostActivated = 22,
        ContainerClosedTaskCompleted = 23,
        DirectContactAttempted = 24,
        SockRecoveryBeforePadsAttempted = 25,
        ContaminatedAbsorbentDroppedOutside = 26
    }

    /// <summary>
    /// Plays the final-course LeakResponse narration in event order.
    /// CueStarted/CueCompleted remain the Stage/App progression contract, so interaction
    /// gates open only after the corresponding instruction has actually finished.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class P102LeakNarrationController : MonoBehaviour
    {
        [Header("Existing Scene Events")]
        [SerializeField] ValveWheelClosureController suctionValve;
        [SerializeField] ValveWheelClosureController dischargeValve;
        [SerializeField] ValveWheelClosureController leakStateProvider;
        [SerializeField] PreplacedGhostInstallGroup sockInstallGroup;
        [SerializeField] PreplacedGhostInstallGroup padInstallGroup;
        [SerializeField] HClAbsorptionVisualController absorptionController;

        [Header("Start / Threshold")]
        [SerializeField] bool playArrivalOnStart = true;
        [SerializeField, Range(0f, 0.1f)] float leakStoppedThreshold = 0.001f;

        [Header("Final Course Narration")]
        [SerializeField] AudioSource narrationSource;
        [SerializeField, Range(0f, 1f)] float narrationVolume = 0.82f;
        [SerializeField] string narrationResourcePath = "TrainingNarration/LeakResponse";

        readonly HashSet<P102LeakNarrationCue> dispatchedOneShotCues =
            new HashSet<P102LeakNarrationCue>();
        readonly Queue<P102LeakNarrationCue> pendingCues =
            new Queue<P102LeakNarrationCue>();
        readonly Dictionary<P102LeakNarrationCue, AudioClip> clipLookup =
            new Dictionary<P102LeakNarrationCue, AudioClip>();

        P102LeakNarrationCue? currentCue;
        Coroutine playbackRoutine;
        bool leakWasStopped;
        bool absorptionWasComplete;
        bool bothValvesClosedHandled;
        bool stageActivated;
        bool stageFlowStarted;
        bool subscribed;

        public event Action<P102LeakNarrationCue> CueStarted;
        public event Action<P102LeakNarrationCue> CueCompleted;

        public bool IsPlaying => currentCue.HasValue || playbackRoutine != null;
        public int PendingCount => pendingCues.Count;
        public P102LeakNarrationCue? CurrentCue => currentCue;
        public bool IsStageActivated => stageActivated;

        public void ConfigureRuntime(
            ValveWheelClosureController newSuctionValve,
            ValveWheelClosureController newDischargeValve,
            ValveWheelClosureController newLeakStateProvider,
            PreplacedGhostInstallGroup newSockInstallGroup,
            PreplacedGhostInstallGroup newPadInstallGroup,
            HClAbsorptionVisualController newAbsorptionController)
        {
            UnsubscribeFromSceneEvents();
            suctionValve = newSuctionValve;
            dischargeValve = newDischargeValve;
            leakStateProvider = newLeakStateProvider;
            sockInstallGroup = newSockInstallGroup;
            padInstallGroup = newPadInstallGroup;
            absorptionController = newAbsorptionController;

            if (stageActivated && isActiveAndEnabled)
                SubscribeToSceneEvents();
        }

        void Awake()
        {
            stageActivated = P102LeakStageActivation.ShouldActivate(gameObject.scene);
            EnsureNarrationSource();
            RebuildClipLookup();
        }

        void OnEnable()
        {
            if (!stageActivated)
                return;

            SubscribeToSceneEvents();
            CaptureObservedState();
        }

        void Update()
        {
            if (!stageActivated)
                return;

            bool leakIsStopped = IsLeakStopped();
            if (!leakWasStopped && leakIsStopped)
                QueueLeakStoppedGuidanceIfReady();
            leakWasStopped = leakIsStopped;

            bool absorptionIsComplete = IsAbsorptionComplete();
            if (!absorptionWasComplete && absorptionIsComplete)
                EnqueueOnce(P102LeakNarrationCue.AbsorptionCompleted);
            absorptionWasComplete = absorptionIsComplete;
        }

        void OnDisable()
        {
            UnsubscribeFromSceneEvents();
            StopAll();
        }

        public void BeginStageFlow()
        {
            if (!stageActivated || stageFlowStarted)
                return;

            stageFlowStarted = true;
            if (playArrivalOnStart)
                EnqueueOnce(P102LeakNarrationCue.LeakSceneArrival);
            else
                AdvanceFromCurrentValveState();
        }

        public void StopAll()
        {
            pendingCues.Clear();
            if (playbackRoutine != null)
            {
                StopCoroutine(playbackRoutine);
                playbackRoutine = null;
            }

            if (narrationSource != null)
            {
                narrationSource.Stop();
                narrationSource.clip = null;
            }

            currentCue = null;
        }

        public void SetStageActivated(bool active)
        {
            if (stageActivated == active)
                return;

            stageActivated = active;
            if (!active)
            {
                UnsubscribeFromSceneEvents();
                StopAll();
                return;
            }

            if (!isActiveAndEnabled)
                return;

            SubscribeToSceneEvents();
            CaptureObservedState();
        }

        public void PlayByNumber(int cueNumber)
        {
            if (!Enum.IsDefined(typeof(P102LeakNarrationCue), cueNumber))
            {
                Debug.LogWarning($"[P102 Flow] Cue number {cueNumber} is outside 1-26.", this);
                return;
            }

            EnqueueOnce((P102LeakNarrationCue)cueNumber);
        }

        public void Play(P102LeakNarrationCue cue) => EnqueueOnce(cue);

        public bool EnqueueOnce(P102LeakNarrationCue cue)
        {
            if (!stageActivated || !dispatchedOneShotCues.Add(cue))
                return false;

            pendingCues.Enqueue(cue);
            KickPlayback();
            return true;
        }

        public bool EnqueueRepeatable(P102LeakNarrationCue cue)
        {
            if (!stageActivated || currentCue == cue || pendingCues.Contains(cue))
                return false;

            pendingCues.Enqueue(cue);
            KickPlayback();
            return true;
        }

        public bool HasDispatched(P102LeakNarrationCue cue) =>
            dispatchedOneShotCues.Contains(cue);

        public void ResetOneShotHistory()
        {
            dispatchedOneShotCues.Clear();
            if (currentCue.HasValue)
                dispatchedOneShotCues.Add(currentCue.Value);
            foreach (var cue in pendingCues)
                dispatchedOneShotCues.Add(cue);
        }

        void KickPlayback()
        {
            if (playbackRoutine == null && pendingCues.Count > 0 && isActiveAndEnabled)
                playbackRoutine = StartCoroutine(PlayQueue());
        }

        IEnumerator PlayQueue()
        {
            while (stageActivated && pendingCues.Count > 0)
            {
                var cue = pendingCues.Dequeue();
                currentCue = cue;
                CueStarted?.Invoke(cue);

                EnsureNarrationSource();
                if (!clipLookup.TryGetValue(cue, out var clip) || clip == null)
                {
                    Debug.LogWarning($"[LeakResponse TTS] Missing clip for cue {(int)cue:00}_{cue}.", this);
                    yield return null;
                }
                else
                {
                    if (clip.loadState == AudioDataLoadState.Unloaded)
                        clip.LoadAudioData();
                    float loadingStartedAt = Time.realtimeSinceStartup;
                    while (clip.loadState == AudioDataLoadState.Loading &&
                           Time.realtimeSinceStartup - loadingStartedAt < 8f)
                        yield return null;

                    narrationSource.Stop();
                    narrationSource.clip = clip;
                    narrationSource.volume = narrationVolume;
                    narrationSource.Play();
                    Debug.Log($"[LeakResponse TTS] Playing {(int)cue:00}_{cue} clip='{clip.name}'.", this);

                    yield return null;
                    while (stageActivated && narrationSource != null &&
                           (narrationSource.isPlaying || AudioListener.pause))
                        yield return null;
                }

                currentCue = null;
                CueCompleted?.Invoke(cue);
                AdvanceValveStageAfterCue(cue);
            }

            currentCue = null;
            playbackRoutine = null;
        }

        void EnsureNarrationSource()
        {
            if (narrationSource == null)
                narrationSource = GetComponent<AudioSource>();
            if (narrationSource == null)
                narrationSource = gameObject.AddComponent<AudioSource>();

            narrationSource.playOnAwake = false;
            narrationSource.loop = false;
            narrationSource.spatialBlend = 0f;
            narrationSource.spatialize = false;
            narrationSource.ignoreListenerPause = false;
            narrationSource.priority = 0;
            narrationSource.volume = narrationVolume;
        }

        void RebuildClipLookup()
        {
            clipLookup.Clear();
            var clips = Resources.LoadAll<AudioClip>(narrationResourcePath);
            if (clips == null)
                return;

            for (var i = 0; i < clips.Length; i++)
            {
                var clip = clips[i];
                if (clip == null || string.IsNullOrEmpty(clip.name))
                    continue;

                var underscore = clip.name.IndexOf('_');
                if (underscore < 2 ||
                    !int.TryParse(clip.name.Substring(0, underscore), out var cueNumber) ||
                    !Enum.IsDefined(typeof(P102LeakNarrationCue), cueNumber))
                    continue;

                clipLookup[(P102LeakNarrationCue)cueNumber] = clip;
            }

            Debug.Log($"[LeakResponse TTS] Loaded {clipLookup.Count}/26 final-course clips.", this);
        }

        void AdvanceValveStageAfterCue(P102LeakNarrationCue completedCue)
        {
            if (completedCue == P102LeakNarrationCue.LeakSceneArrival)
                AdvanceFromCurrentValveState();
        }

        void AdvanceFromCurrentValveState()
        {
            if (suctionValve != null && !suctionValve.IsFullyClosed)
            {
                EnqueueOnce(P102LeakNarrationCue.SuctionValveActivated);
                return;
            }

            if (dischargeValve != null && !dischargeValve.IsFullyClosed)
            {
                EnqueueOnce(P102LeakNarrationCue.DischargeValveActivated);
                return;
            }

            OnAnyValveFullyClosed();
        }

        void SubscribeToSceneEvents()
        {
            if (subscribed)
                return;

            if (suctionValve != null)
                suctionValve.OnFullyClosed.AddListener(OnSuctionValveClosed);
            if (dischargeValve != null)
                dischargeValve.OnFullyClosed.AddListener(OnAnyValveFullyClosed);
            if (sockInstallGroup != null)
                sockInstallGroup.Installed += OnSockInstalled;
            if (padInstallGroup != null)
                padInstallGroup.Installed += OnPadInstalled;
            subscribed = true;
        }

        void UnsubscribeFromSceneEvents()
        {
            if (!subscribed)
                return;

            if (suctionValve != null)
                suctionValve.OnFullyClosed.RemoveListener(OnSuctionValveClosed);
            if (dischargeValve != null)
                dischargeValve.OnFullyClosed.RemoveListener(OnAnyValveFullyClosed);
            if (sockInstallGroup != null)
                sockInstallGroup.Installed -= OnSockInstalled;
            if (padInstallGroup != null)
                padInstallGroup.Installed -= OnPadInstalled;
            subscribed = false;
        }

        void CaptureObservedState()
        {
            leakWasStopped = IsLeakStopped();
            absorptionWasComplete = IsAbsorptionComplete();
        }

        void OnSuctionValveClosed()
        {
            EnqueueOnce(P102LeakNarrationCue.SuctionValveClosed);
            if (dischargeValve != null && !dischargeValve.IsFullyClosed)
                EnqueueOnce(P102LeakNarrationCue.DischargeValveActivated);

            OnAnyValveFullyClosed();
        }

        void OnAnyValveFullyClosed()
        {
            if (!AreBothValvesClosed() || bothValvesClosedHandled)
                return;

            bothValvesClosedHandled = true;
            EnqueueOnce(P102LeakNarrationCue.BothValvesClosed);
            EnqueueOnce(P102LeakNarrationCue.LeakReductionStarted);
            QueueLeakStoppedGuidanceIfReady();
        }

        void QueueLeakStoppedGuidanceIfReady()
        {
            if (AreBothValvesClosed() && IsLeakStopped())
                EnqueueOnce(P102LeakNarrationCue.LeakReductionStarted);
        }

        void OnSockInstalled(PreplacedGhostInstallGroup installedGroup)
        {
            if (installedGroup != sockInstallGroup)
                return;

            EnqueueOnce(P102LeakNarrationCue.AllAbsorbentSocksPlaced);
            if (padInstallGroup == null || !padInstallGroup.IsInstalled)
                EnqueueOnce(P102LeakNarrationCue.AbsorbentPadPlacementActivated);
        }

        void OnPadInstalled(PreplacedGhostInstallGroup installedGroup)
        {
            if (installedGroup == padInstallGroup)
                EnqueueOnce(P102LeakNarrationCue.AllAbsorbentPadsPlaced);
        }

        bool AreBothValvesClosed() =>
            suctionValve != null && suctionValve.IsFullyClosed &&
            dischargeValve != null && dischargeValve.IsFullyClosed;

        bool IsLeakStopped() =>
            leakStateProvider != null &&
            leakStateProvider.LeakIntensity01 <= leakStoppedThreshold;

        bool IsAbsorptionComplete() =>
            absorptionController != null && absorptionController.IsAbsorptionComplete;
    }
}
