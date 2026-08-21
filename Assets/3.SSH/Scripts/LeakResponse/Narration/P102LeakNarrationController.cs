using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HClTraining.App;
using HClTraining.SharedXR;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
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
    /// Queues the authored P-102 narration clips, displays their matching subtitles,
    /// and observes only public leak-response state/events. It does not change valve,
    /// installation, absorption, grab, or recovery behaviour.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class P102LeakNarrationController : MonoBehaviour
    {
        const string ResourceFolder = "LeakTTS/";
        const string CsvResourcePath = "P102/Unity_TTS_Map";
        const int RequiredCueCount = 26;
        public const float RuntimeSubtitleFontSizeMin = 48f;
        public const float RuntimeSubtitleFontSizeMax = 48f;
        public static readonly Vector2 RuntimeSubtitlePanelAnchorMin = Vector2.zero;
        public static readonly Vector2 RuntimeSubtitlePanelAnchorMax = Vector2.one;

        [Header("Narration Output")]
        [SerializeField] AudioSource narrationSource;
        [SerializeField] RectTransform subtitleRoot;
        [SerializeField] TMP_Text subtitleText;
        [SerializeField] CanvasGroup subtitleCanvasGroup;

        [Header("Subtitle HUD")]
        [SerializeField] bool followCamera = true;
        [SerializeField] Transform subtitleCamera;
        [SerializeField, Min(0.01f)] float subtitleDistance = 0.9f;
        [SerializeField] float subtitleVerticalOffset = -0.37f;
        [SerializeField, Min(0.00001f)] float subtitleScale = 0.0006f;
        [Tooltip("Runtime subtitle HUD local position. X = left/right, Y = up/down, Z = eye distance.")]
        [SerializeField] Vector3 subtitleHudLocalPosition = new Vector3(0f, -0.37f, 0.9f);

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

        readonly Queue<P102LeakNarrationCue> pendingCues =
            new Queue<P102LeakNarrationCue>();
        readonly HashSet<P102LeakNarrationCue> queuedCues =
            new HashSet<P102LeakNarrationCue>();
        readonly HashSet<P102LeakNarrationCue> playedOrScheduledCues =
            new HashSet<P102LeakNarrationCue>();

        Coroutine queueRoutine;
        P102LeakNarrationCue? currentCue;
        bool leakWasStopped;
        bool absorptionWasComplete;
        bool bothValvesClosedHandled;
        bool stageActivated;

        public event Action<P102LeakNarrationCue> CueStarted;
        public event Action<P102LeakNarrationCue> CueCompleted;

        sealed class CueDefinition
        {
            public readonly string ResourceName;
            public readonly string Subtitle;
            public readonly float FallbackDuration;
            public readonly AudioClip Clip;

            public CueDefinition(string resourceName, string subtitle, float fallbackDuration,
                AudioClip clip)
            {
                ResourceName = resourceName;
                Subtitle = subtitle;
                FallbackDuration = fallbackDuration;
                Clip = clip;
            }
        }

        readonly Dictionary<P102LeakNarrationCue, CueDefinition> runtimeCueDefinitions =
            new Dictionary<P102LeakNarrationCue, CueDefinition>();
        bool runtimeDataLoadAttempted;
        bool runtimeDataReady;
        int loadedAudioClipCount;

        public bool IsPlaying => currentCue.HasValue;
        public int PendingCount => pendingCues.Count;
        public P102LeakNarrationCue? CurrentCue => currentCue;
        public AudioSource NarrationSource => narrationSource;
        public RectTransform SubtitleRoot => subtitleRoot;
        public TMP_Text SubtitleText => subtitleText;
        public CanvasGroup SubtitleCanvasGroup => subtitleCanvasGroup;
        public bool RuntimeDataReady => runtimeDataReady;
        public int LoadedCueCount => runtimeCueDefinitions.Count;
        public int LoadedAudioClipCount => loadedAudioClipCount;
        public Vector3 SubtitleHudLocalPosition
        {
            get => subtitleHudLocalPosition;
            set => subtitleHudLocalPosition = value;
        }

        public void ConfigureRuntime(
            AudioSource newNarrationSource,
            RectTransform newSubtitleRoot,
            TMP_Text newSubtitleText,
            CanvasGroup newSubtitleCanvasGroup,
            Transform newSubtitleCamera,
            ValveWheelClosureController newSuctionValve,
            ValveWheelClosureController newDischargeValve,
            ValveWheelClosureController newLeakStateProvider,
            PreplacedGhostInstallGroup newSockInstallGroup,
            PreplacedGhostInstallGroup newPadInstallGroup,
            HClAbsorptionVisualController newAbsorptionController)
        {
            UnsubscribeFromSceneEvents();
            LoadRuntimeCueData();
            narrationSource = newNarrationSource != null ? newNarrationSource : GetComponent<AudioSource>();
            subtitleRoot = newSubtitleRoot;
            subtitleText = newSubtitleText;
            subtitleCanvasGroup = newSubtitleCanvasGroup;
            subtitleCamera = newSubtitleCamera;
            suctionValve = newSuctionValve;
            dischargeValve = newDischargeValve;
            leakStateProvider = newLeakStateProvider;
            sockInstallGroup = newSockInstallGroup;
            padInstallGroup = newPadInstallGroup;
            absorptionController = newAbsorptionController;
            ConfigureNarrationSource();
            ConfigureSubtitleHudLayout();
            SetSubtitleVisible(false);

            if (stageActivated && isActiveAndEnabled)
                SubscribeToSceneEvents();
        }

        void Reset()
        {
            narrationSource = GetComponent<AudioSource>();
            ConfigureNarrationSource();
        }

        void Awake()
        {
            stageActivated = P102LeakStageActivation.ShouldActivate(gameObject.scene);
            LoadRuntimeCueData();
            if (narrationSource == null)
                narrationSource = GetComponent<AudioSource>();

            ConfigureNarrationSource();
            ConfigureSubtitleHudLayout();
            SetSubtitleVisible(false);
        }

        void OnEnable()
        {
            if (!stageActivated)
                return;
            SubscribeToSceneEvents();
            leakWasStopped = IsLeakStopped();
            absorptionWasComplete = IsAbsorptionComplete();
            if (queueRoutine == null && pendingCues.Count > 0)
                queueRoutine = StartCoroutine(ProcessQueue());
        }

        IEnumerator Start()
        {
            while (!stageActivated)
                yield return null;

            if (!runtimeDataReady)
            {
                Debug.LogError(
                    $"[P102 Flow] Narration start is blocked because CSV/WAV resources are incomplete " +
                    $"(CSV {runtimeCueDefinitions.Count}/26, WAV {loadedAudioClipCount}/26).", this);
                yield break;
            }

            if (followCamera)
                yield return AttachSubtitleHudToCamera();

            if (!playArrivalOnStart)
                yield break;

            yield return new WaitForSeconds(1.25f);

            EnqueueOnce(P102LeakNarrationCue.LeakSceneArrival);
        }

        void Update()
        {
            if (!stageActivated)
                return;

            if (followCamera && subtitleRoot != null)
            {
                var cachedCamera = subtitleCamera != null ? subtitleCamera.GetComponent<Camera>() : null;
                var headCamera = XRSubtitleHudLayout.ResolveActiveCamera(cachedCamera);
                if (headCamera != null)
                {
                    subtitleCamera = headCamera.transform;
                    XRSubtitleHudLayout.Apply(
                        subtitleRoot.GetComponent<Canvas>(), subtitleRoot, headCamera);
                }
            }

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

        public void StopAll()
        {
            if (queueRoutine != null)
            {
                StopCoroutine(queueRoutine);
                queueRoutine = null;
            }

            if (narrationSource != null)
            {
                narrationSource.Stop();
                narrationSource.clip = null;
            }

            currentCue = null;
            pendingCues.Clear();
            queuedCues.Clear();
            SetSubtitleVisible(false);
        }

        public bool IsStageActivated => stageActivated;

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
            leakWasStopped = IsLeakStopped();
            absorptionWasComplete = IsAbsorptionComplete();
            if (queueRoutine == null && pendingCues.Count > 0)
                queueRoutine = StartCoroutine(ProcessQueue());
        }

        public void PlayByNumber(int cueNumber)
        {
            if (!Enum.IsDefined(typeof(P102LeakNarrationCue), cueNumber))
            {
                Debug.LogWarning($"[P-102 Narration] Cue number {cueNumber} is outside 1-26.", this);
                return;
            }

            EnqueueOnce((P102LeakNarrationCue)cueNumber);
        }

        public void Play(P102LeakNarrationCue cue)
        {
            EnqueueOnce(cue);
        }

        public bool EnqueueOnce(P102LeakNarrationCue cue)
        {
            if (playedOrScheduledCues.Contains(cue))
                return false;

            if (!EnqueueInternal(cue))
                return false;

            playedOrScheduledCues.Add(cue);
            return true;
        }

        public bool EnqueueRepeatable(P102LeakNarrationCue cue)
        {
            return EnqueueInternal(cue);
        }

        public void ResetOneShotHistory()
        {
            playedOrScheduledCues.Clear();
            if (currentCue.HasValue)
                playedOrScheduledCues.Add(currentCue.Value);

            foreach (P102LeakNarrationCue queuedCue in pendingCues)
                playedOrScheduledCues.Add(queuedCue);
        }

        bool EnqueueInternal(P102LeakNarrationCue cue)
        {
            if (!stageActivated)
                return false;

            if (!runtimeDataReady || !runtimeCueDefinitions.ContainsKey(cue))
            {
                Debug.LogError(
                    $"[P-102 Narration] Cue '{cue}' is unavailable because the CSV/WAV runtime map is incomplete.",
                    this);
                return false;
            }

            if (currentCue == cue || queuedCues.Contains(cue))
                return false;

            pendingCues.Enqueue(cue);
            queuedCues.Add(cue);

            if (cue == P102LeakNarrationCue.LeakReductionStarted)
            {
                CueDefinition definition = runtimeCueDefinitions[cue];
                Debug.Log(
                    $"[P102 Flow] Enqueued TTS key=06 ({cue}), " +
                    $"AudioClip=Resources/{ResourceFolder}{definition.ResourceName}.wav", this);
            }

            if (queueRoutine == null && isActiveAndEnabled)
                queueRoutine = StartCoroutine(ProcessQueue());

            return true;
        }

        IEnumerator ProcessQueue()
        {
            // Ensure StartCoroutine has assigned queueRoutine before any failure path can clear it.
            yield return null;
            while (pendingCues.Count > 0)
            {
                P102LeakNarrationCue cue = pendingCues.Dequeue();
                queuedCues.Remove(cue);
                currentCue = cue;

                CueDefinition definition = runtimeCueDefinitions[cue];
                AudioClip clip = definition.Clip;
                ShowSubtitle(definition.Subtitle);
                CueStarted?.Invoke(cue);
                if (clip == null)
                {
                    Debug.LogError(
                        $"[P-102 Narration] Missing Resources audio clip " +
                        $"'{ResourceFolder}{definition.ResourceName}'. The cue remains incomplete.",
                        this);
                    AbortCurrentCueWithoutCompletion(cue);
                    yield break;
                }

                if (narrationSource == null || !narrationSource.enabled ||
                    !narrationSource.gameObject.activeInHierarchy)
                {
                    Debug.LogError(
                        $"[P-102 Narration] AudioSource is unavailable for TTS {(int)cue:00}; " +
                        "the cue remains incomplete.", this);
                    AbortCurrentCueWithoutCompletion(cue);
                    yield break;
                }

                narrationSource.clip = clip;
                narrationSource.time = 0f;
                narrationSource.Play();
                Debug.Log(
                    $"[P102 Flow] AudioSource playback started: TTS {(int)cue:00}, " +
                    $"clip=Resources/{ResourceFolder}{definition.ResourceName}.wav, " +
                    $"source={P102RuntimeUtility.GetHierarchyPath(narrationSource.transform)}", this);
                yield return null;

                if (!narrationSource.isPlaying || narrationSource.clip != clip)
                {
                    Debug.LogError(
                        $"[P-102 Narration] AudioSource failed to enter playback for TTS {(int)cue:00}; " +
                        "the cue remains incomplete.", this);
                    AbortCurrentCueWithoutCompletion(cue);
                    yield break;
                }

                while (narrationSource.isPlaying && narrationSource.clip == clip)
                    yield return null;

                if (narrationSource.clip != clip)
                {
                    Debug.LogError(
                        $"[P-102 Narration] AudioSource clip changed before TTS {(int)cue:00} finished; " +
                        "the cue remains incomplete.", this);
                    AbortCurrentCueWithoutCompletion(cue);
                    yield break;
                }

                Debug.Log(
                    $"[P102 Flow] AudioSource playback finished: TTS {(int)cue:00}, " +
                    $"clip=Resources/{ResourceFolder}{definition.ResourceName}.wav", this);

                SetSubtitleVisible(false);
                currentCue = null;
                CueCompleted?.Invoke(cue);
                AdvanceValveStageAfterCue(cue);
                Debug.Log($"[P102 Flow] TTS {(int)cue:00} completed", this);
            }

            queueRoutine = null;
        }

        void AdvanceValveStageAfterCue(P102LeakNarrationCue completedCue)
        {
            if (completedCue == P102LeakNarrationCue.LeakSceneArrival &&
                suctionValve != null && !suctionValve.IsFullyClosed)
            {
                EnqueueOnce(P102LeakNarrationCue.SuctionValveActivated);
            }
        }

        void AbortCurrentCueWithoutCompletion(P102LeakNarrationCue cue)
        {
            SetSubtitleVisible(false);
            currentCue = null;
            queueRoutine = null;
            playedOrScheduledCues.Remove(cue);
            foreach (P102LeakNarrationCue pendingCue in pendingCues)
                playedOrScheduledCues.Remove(pendingCue);
            pendingCues.Clear();
            queuedCues.Clear();
            Debug.LogError(
                $"[P102 Flow] TTS {(int)cue:00} aborted; CueCompleted was not dispatched.", this);
        }

        void SubscribeToSceneEvents()
        {
            if (suctionValve != null)
                suctionValve.OnFullyClosed.AddListener(OnSuctionValveClosed);
            if (dischargeValve != null)
                dischargeValve.OnFullyClosed.AddListener(OnAnyValveFullyClosed);
            if (sockInstallGroup != null)
                sockInstallGroup.Installed += OnSockInstalled;
            if (padInstallGroup != null)
                padInstallGroup.Installed += OnPadInstalled;
        }

        void UnsubscribeFromSceneEvents()
        {
            if (suctionValve != null)
                suctionValve.OnFullyClosed.RemoveListener(OnSuctionValveClosed);
            if (dischargeValve != null)
                dischargeValve.OnFullyClosed.RemoveListener(OnAnyValveFullyClosed);
            if (sockInstallGroup != null)
                sockInstallGroup.Installed -= OnSockInstalled;
            if (padInstallGroup != null)
                padInstallGroup.Installed -= OnPadInstalled;
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
            Debug.Log("[P102 Flow] DIS fully-closed event received exactly once.", this);
            EnqueueOnce(P102LeakNarrationCue.BothValvesClosed);
            EnqueueOnce(P102LeakNarrationCue.LeakReductionStarted);
            QueueLeakStoppedGuidanceIfReady();
        }

        void QueueLeakStoppedGuidanceIfReady()
        {
            if (!AreBothValvesClosed() || !IsLeakStopped())
                return;

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

        bool AreBothValvesClosed()
        {
            return suctionValve != null && suctionValve.IsFullyClosed &&
                   dischargeValve != null && dischargeValve.IsFullyClosed;
        }

        bool IsLeakStopped()
        {
            return leakStateProvider != null &&
                   leakStateProvider.LeakIntensity01 <= leakStoppedThreshold;
        }

        bool IsAbsorptionComplete()
        {
            return absorptionController != null && absorptionController.IsAbsorptionComplete;
        }

        void LoadRuntimeCueData()
        {
            if (runtimeDataLoadAttempted)
                return;

            runtimeDataLoadAttempted = true;
            runtimeCueDefinitions.Clear();
            loadedAudioClipCount = 0;
            TextAsset csvAsset = Resources.Load<TextAsset>(CsvResourcePath);
            if (csvAsset == null)
            {
                Debug.LogError(
                    "[P102 Flow] Required narration CSV 'Assets/3.SSH/Resources/P102/Unity_TTS_Map.csv' " +
                    "could not be loaded from Resources path 'P102/Unity_TTS_Map'.", this);
                return;
            }

            List<string[]> rows = ParseCsv(csvAsset.text);
            if (rows.Count < 2)
            {
                Debug.LogError("[P102 Flow] Narration CSV has no data rows.", csvAsset);
                return;
            }

            string[] header = rows[0];
            int numberColumn = FindColumn(header, "no");
            int eventColumn = FindColumn(header, "event_enum");
            int fileColumn = FindColumn(header, "file_name");
            int durationColumn = FindColumn(header, "duration_sec");
            int subtitleColumn = FindColumn(header, "subtitle_text");
            if (numberColumn < 0 || eventColumn < 0 || fileColumn < 0 ||
                durationColumn < 0 || subtitleColumn < 0)
            {
                Debug.LogError(
                    "[P102 Flow] Narration CSV is missing one of: no, event_enum, file_name, duration_sec, subtitle_text.",
                    csvAsset);
                return;
            }

            for (int rowIndex = 1; rowIndex < rows.Count; ++rowIndex)
            {
                string[] row = rows[rowIndex];
                int requiredColumn = Mathf.Max(
                    Mathf.Max(numberColumn, eventColumn),
                    Mathf.Max(fileColumn, Mathf.Max(durationColumn, subtitleColumn)));
                if (row.Length <= requiredColumn || string.IsNullOrWhiteSpace(row[numberColumn]))
                    continue;

                if (!int.TryParse(row[numberColumn], NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out int cueNumber) ||
                    !Enum.TryParse(row[eventColumn], true, out P102LeakNarrationCue cue) ||
                    !Enum.IsDefined(typeof(P102LeakNarrationCue), cue) ||
                    cueNumber != (int)cue)
                {
                    Debug.LogError(
                        $"[P102 Flow] Narration CSV row {rowIndex + 1} has an invalid cue number/event mapping.",
                        csvAsset);
                    continue;
                }

                if (runtimeCueDefinitions.ContainsKey(cue))
                {
                    Debug.LogError($"[P102 Flow] Narration CSV contains duplicate cue '{cue}'.", csvAsset);
                    continue;
                }

                if (!float.TryParse(row[durationColumn], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float duration) || duration <= 0f)
                {
                    Debug.LogError(
                        $"[P102 Flow] Narration CSV cue {(int)cue:00} has invalid duration '{row[durationColumn]}'.",
                        csvAsset);
                    continue;
                }

                string fileName = row[fileColumn].Trim();
                string resourceName = fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                    ? fileName.Substring(0, fileName.Length - 4)
                    : fileName;
                AudioClip clip = Resources.Load<AudioClip>(ResourceFolder + resourceName);
                if (clip == null)
                {
                    Debug.LogError(
                        $"[P102 Flow] Required WAV for cue {(int)cue:00} is missing at " +
                        $"Resources path '{ResourceFolder}{resourceName}'.", csvAsset);
                }
                else
                {
                    ++loadedAudioClipCount;
                }

                string subtitle = row[subtitleColumn].Replace("\\n", "\n");
                runtimeCueDefinitions.Add(cue,
                    new CueDefinition(resourceName, subtitle, duration, clip));
            }

            runtimeDataReady = runtimeCueDefinitions.Count == RequiredCueCount &&
                               loadedAudioClipCount == RequiredCueCount;
            if (runtimeDataReady)
            {
                Debug.Log("[P102 Flow] Narration resources ready: CSV 26/26, WAV 26/26.", this);
            }
            else
            {
                Debug.LogError(
                    $"[P102 Flow] Narration resource validation failed: CSV {runtimeCueDefinitions.Count}/26, " +
                    $"WAV {loadedAudioClipCount}/26.", this);
            }
        }

        static int FindColumn(string[] header, string name)
        {
            if (header == null)
                return -1;
            for (int i = 0; i < header.Length; ++i)
            {
                if (string.Equals(header[i].Trim().TrimStart('\ufeff'), name,
                        StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        static List<string[]> ParseCsv(string csv)
        {
            var rows = new List<string[]>();
            if (string.IsNullOrEmpty(csv))
                return rows;

            var row = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < csv.Length; ++i)
            {
                char character = csv[i];
                if (quoted)
                {
                    if (character == '"')
                    {
                        if (i + 1 < csv.Length && csv[i + 1] == '"')
                        {
                            field.Append('"');
                            ++i;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        field.Append(character);
                    }
                    continue;
                }

                if (character == '"')
                    quoted = true;
                else if (character == ',')
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if (character == '\n')
                {
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row.ToArray());
                    row.Clear();
                }
                else if (character != '\r')
                {
                    field.Append(character);
                }
            }

            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row.ToArray());
            }

            return rows;
        }

        void ConfigureNarrationSource()
        {
            if (narrationSource == null)
                return;

            narrationSource.enabled = true;
            narrationSource.playOnAwake = false;
            narrationSource.loop = false;
            narrationSource.spatialBlend = 0f;
            narrationSource.mute = false;
            if (narrationSource.volume <= 0.001f)
                narrationSource.volume = 1f;
        }

        IEnumerator AttachSubtitleHudToCamera()
        {
            // The runtime bootstrap repairs/creates the HUD one frame after Scene Start.
            // Give that existing setup path time to finish instead of emitting a false
            // missing-reference warning on the first frame.
            const int maxHudFrameRetries = 120;
            for (int retry = 0; subtitleRoot == null && retry < maxHudFrameRetries; ++retry)
                yield return null;

            if (subtitleRoot == null)
            {
                Debug.LogWarning(
                    "[P-102 Narration] Subtitle Root was not created within two seconds; " +
                    "the subtitle HUD cannot follow the camera.",
                    this);
                yield break;
            }

            // XR can create or activate its tagged camera after this component starts.
            // Retry at a low frequency, then keep the resolved transform instead of
            // searching for Camera.main every frame.
            WaitForSecondsRealtime cameraRetryDelay = new WaitForSecondsRealtime(0.25f);
            while (subtitleCamera == null)
            {
                Camera mainCamera = Camera.main;
                if (mainCamera != null)
                {
                    subtitleCamera = mainCamera.transform;
                    break;
                }

                yield return cameraRetryDelay;
            }

            ConfigureSubtitleHudLayout();
        }

        void ConfigureSubtitleHudLayout()
        {
            if (subtitleRoot != null)
            {
                Canvas subtitleCanvas = subtitleRoot.GetComponent<Canvas>();
                var cachedCamera = subtitleCamera != null
                    ? subtitleCamera.GetComponent<Camera>()
                    : null;
                var headCamera = XRSubtitleHudLayout.ResolveActiveCamera(cachedCamera);
                if (headCamera != null)
                    subtitleCamera = headCamera.transform;
                XRSubtitleHudLayout.Apply(subtitleCanvas, subtitleRoot, headCamera);

                GraphicRaycaster[] raycasters =
                    subtitleRoot.GetComponentsInChildren<GraphicRaycaster>(true);
                for (int i = 0; i < raycasters.Length; ++i)
                    raycasters[i].enabled = false;

                Image panel = subtitleRoot.GetComponentInChildren<Image>(true);
                if (panel != null)
                {
                    panel.color = new Color(0f, 0f, 0f, 0.72f);
                    panel.sprite = P102RuntimeUtility.GetRoundedPanelSprite();
                    panel.type = Image.Type.Sliced;
                    panel.raycastTarget = false;
                    RectTransform imagePanelRect = panel.rectTransform;
                    imagePanelRect.anchorMin = RuntimeSubtitlePanelAnchorMin;
                    imagePanelRect.anchorMax = RuntimeSubtitlePanelAnchorMax;
                    imagePanelRect.pivot = new Vector2(0.5f, 0.5f);
                    imagePanelRect.offsetMin = Vector2.zero;
                    imagePanelRect.offsetMax = Vector2.zero;
                }
            }

            if (subtitleCanvasGroup != null &&
                subtitleCanvasGroup.transform is RectTransform panelRect &&
                panelRect != subtitleRoot && panelRect.GetComponent<Image>() == null)
            {
                SetStretchRect(panelRect, Vector2.zero, Vector2.zero);
            }

            if (subtitleText != null)
            {
                TMP_FontAsset runtimeSubtitleFont = P102RuntimeUtility.LoadRuntimeFont();
                if (runtimeSubtitleFont != null)
                    subtitleText.font = runtimeSubtitleFont;

                if (subtitleText.rectTransform != subtitleRoot)
                {
                    SetStretchRect(
                        subtitleText.rectTransform,
                        new Vector2(36f, 14f),
                        new Vector2(-36f, -14f));
                }

                subtitleText.alignment = TextAlignmentOptions.Center;
                subtitleText.textWrappingMode = TextWrappingModes.Normal;
                subtitleText.overflowMode = TextOverflowModes.Overflow;
                subtitleText.enableAutoSizing = true;
                subtitleText.fontSizeMin = RuntimeSubtitleFontSizeMin;
                subtitleText.fontSizeMax = RuntimeSubtitleFontSizeMax;
                subtitleText.fontSize = RuntimeSubtitleFontSizeMax;
                subtitleText.outlineColor = new Color32(0, 0, 0, 230);
                subtitleText.outlineWidth = 0.16f;
                subtitleText.raycastTarget = false;
            }

            if (subtitleRoot != null)
            {
                P102RuntimeUtility.DisableGraphicRaycasts(subtitleRoot.gameObject);
                P102RuntimeUtility.SetIgnoreRaycastRecursively(subtitleRoot.gameObject);
            }
        }

        static void SetStretchRect(
            RectTransform rectTransform,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = offsetMin;
            rectTransform.offsetMax = offsetMax;
        }

        void ShowSubtitle(string subtitle)
        {
            if (subtitleText != null)
                subtitleText.text = subtitle;

            SetSubtitleVisible(true);
        }

        void SetSubtitleVisible(bool visible)
        {
            if (subtitleCanvasGroup != null)
            {
                subtitleCanvasGroup.alpha = visible ? 1f : 0f;
                subtitleCanvasGroup.interactable = false;
                subtitleCanvasGroup.blocksRaycasts = false;
            }

            if (subtitleText != null)
            {
                subtitleText.enabled = visible;
                if (!visible)
                    subtitleText.text = string.Empty;
            }
        }

    }
}
