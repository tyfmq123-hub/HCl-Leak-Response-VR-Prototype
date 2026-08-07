using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
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
    /// Queues the authored P-102 narration clips, displays their matching subtitles,
    /// and observes only public leak-response state/events. It does not change valve,
    /// installation, absorption, grab, or recovery behaviour.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class P102LeakNarrationController : MonoBehaviour
    {
        const string ResourceFolder = "LeakTTS/";

        [Header("Narration Output")]
        [SerializeField] AudioSource narrationSource;
        [SerializeField] RectTransform subtitleRoot;
        [SerializeField] TMP_Text subtitleText;
        [SerializeField] CanvasGroup subtitleCanvasGroup;

        [Header("Subtitle HUD")]
        [SerializeField] bool followCamera = true;
        [SerializeField] Transform subtitleCamera;
        [SerializeField, Min(0.01f)] float subtitleDistance = 1f;
        [SerializeField] float subtitleVerticalOffset = -0.24f;
        [SerializeField, Min(0.00001f)] float subtitleScale = 0.0006f;

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

        sealed class CueDefinition
        {
            public readonly string ResourceName;
            public readonly string Subtitle;

            public CueDefinition(string resourceName, string subtitle)
            {
                ResourceName = resourceName;
                Subtitle = subtitle;
            }
        }

        static readonly IReadOnlyDictionary<P102LeakNarrationCue, CueDefinition> CueDefinitions =
            new Dictionary<P102LeakNarrationCue, CueDefinition>
            {
                { P102LeakNarrationCue.LeakSceneArrival, new CueDefinition("01_leak_scene_arrival", "주의, P-102 연결 배관의 플랜지 접합부에서 염산이 누출되고 있습니다.\n누출 구간을 격리하기 위해 P-102 흡입측과 토출측 차단밸브를 폐쇄하십시오.") },
                { P102LeakNarrationCue.SuctionValveActivated, new CueDefinition("02_suction_valve_close_prompt", "먼저 외곽선으로 표시된 P-102 흡입측 차단밸브를 폐쇄하십시오.\n밸브 핸들을 반복해서 잡아 시계 방향으로 돌리고, 폐쇄 게이지를 끝까지 채우십시오.") },
                { P102LeakNarrationCue.SuctionValveClosed, new CueDefinition("03_suction_valve_closed", "P-102 흡입측 차단밸브 폐쇄를 확인했습니다.\n누출 구간의 격리는 아직 완료되지 않았습니다.") },
                { P102LeakNarrationCue.DischargeValveActivated, new CueDefinition("04_discharge_valve_close_prompt", "계속해서 외곽선으로 표시된 P-102 토출측 차단밸브를 닫으십시오.\n밸브 핸들을 반복해서 잡고 시계 방향으로 돌린 다음, 폐쇄 게이지를 끝까지 채우십시오.") },
                { P102LeakNarrationCue.BothValvesClosed, new CueDefinition("05_both_valves_closed", "P-102 흡입측과 토출측 차단밸브가 모두 닫혔습니다.\n누출 구간의 격리가 완료되었습니다.") },
                { P102LeakNarrationCue.LeakReductionStarted, new CueDefinition("06_leak_reduction_observe", "플랜지 접합부를 확인하고, 누출량의 변화를 관찰하십시오.") },
                { P102LeakNarrationCue.LeakReductionCompleted, new CueDefinition("07_leak_reduction_complete", "플랜지 접합부의 누출량 감소를 확인했습니다.\n이제 바닥에 누출된 염산의 확산을 차단하십시오.") },
                { P102LeakNarrationCue.AbsorbentSockPlacementActivated, new CueDefinition("08_absorbent_sock_place_prompt", "누출된 염산의 확산을 막기 위해 화학물질용 흡착삭을 배치하십시오.\n준비된 흡착삭을 잡아 바닥에 반투명하게 표시된 위치에 놓으십시오.") },
                { P102LeakNarrationCue.AllAbsorbentSocksPlaced, new CueDefinition("09_absorbent_sock_placed", "흡착삭 배치가 완료되었습니다.\n누출액의 확산 경로가 차단되었습니다.") },
                { P102LeakNarrationCue.AbsorbentPadPlacementActivated, new CueDefinition("10_absorbent_pad_place_prompt", "흡착삭 안쪽에 남아 있는 염산을 흡수하십시오.\n준비된 흡착패드를 잡아 반투명하게 표시된 위치에 놓으십시오.") },
                { P102LeakNarrationCue.AllAbsorbentPadsPlaced, new CueDefinition("11_absorbent_pad_placed", "흡착패드 배치가 완료되었습니다.\n염산 흡수가 진행 중입니다.") },
                { P102LeakNarrationCue.AbsorptionCompleted, new CueDefinition("12_absorption_complete", "잔류 염산의 흡수가 완료되었습니다.\n이제 오염된 흡착재를 회수하십시오.") },
                { P102LeakNarrationCue.ContainerAndLidGhostActivated, new CueDefinition("13_container_lid_open_prompt", "오염된 흡착재를 회수하기 위해 수거용기의 뚜껑을 여십시오.\n뚜껑을 들어 올린 뒤, 수거용기 옆에 반투명하게 표시된 거치 위치에 놓으십시오.") },
                { P102LeakNarrationCue.ContainerLidStowed, new CueDefinition("14_container_lid_stowed", "수거용기 뚜껑이 거치되었습니다.\n수거용기 옆에 비치된 회수용 집게를 잡으십시오.") },
                { P102LeakNarrationCue.RecoveryTongsPickedUp, new CueDefinition("15_pad_pickup_prompt", "먼저 흡착패드를 회수하십시오.\n트리거를 눌러 흡착패드를 집고, 수거용기 안으로 옮기십시오.\n흡착패드가 수거용기 안에 들어오면 트리거를 놓으십시오.") },
                { P102LeakNarrationCue.PadRecoveryIdle, new CueDefinition("16_pad_pickup_idle_reminder", "남아 있는 흡착패드를 계속 회수하십시오.") },
                { P102LeakNarrationCue.AllPadsRecovered, new CueDefinition("17_all_pads_collected", "모든 흡착패드의 회수를 확인했습니다.\n이제 확산 경계를 유지하고 있는 흡착삭을 회수하십시오.") },
                { P102LeakNarrationCue.SockRecoveryStarted, new CueDefinition("18_sock_pickup_prompt", "트리거를 눌러 흡착삭을 집고, 수거용기 안으로 옮기십시오.\n흡착삭이 수거용기 안으로 들어오면 트리거를 놓으십시오.") },
                { P102LeakNarrationCue.SockRecoveryIdle, new CueDefinition("19_sock_pickup_idle_reminder", "남아 있는 흡착삭을 계속 회수하십시오.") },
                { P102LeakNarrationCue.AllSocksRecovered, new CueDefinition("20_all_absorbents_collected", "모든 오염 흡착재의 회수를 확인했습니다.\n회수용 집게를 수거용기 옆 거치대에 반납하십시오.") },
                { P102LeakNarrationCue.RecoveryTongsReturned, new CueDefinition("21_tongs_returned", "회수용 집게 반납을 확인했습니다.\n수거용기의 뚜껑을 닫으십시오.") },
                { P102LeakNarrationCue.ContainerClosureGhostActivated, new CueDefinition("22_container_lid_close_prompt", "수거용기 옆에 거치된 뚜껑을 잡으십시오.\n뚜껑을 수거용기 위에 반투명하게 표시된 폐쇄 위치에 놓으십시오.") },
                { P102LeakNarrationCue.ContainerClosedTaskCompleted, new CueDefinition("23_task_complete", "수거용기 뚜껑의 폐쇄 상태를 확인했습니다.\nP-102 누출 구간 차단 및 오염 흡착재 회수 작업을 완료했습니다.") },
                { P102LeakNarrationCue.DirectContactAttempted, new CueDefinition("24_warning_direct_contact", "오염된 흡착재에 직접 접촉하지 마십시오.\n회수용 집게를 사용하십시오.") },
                { P102LeakNarrationCue.SockRecoveryBeforePadsAttempted, new CueDefinition("25_warning_sock_before_pads", "흡착패드를 먼저 회수하십시오.\n흡착삭은 모든 흡착패드를 회수한 후 마지막으로 회수합니다.") },
                { P102LeakNarrationCue.ContaminatedAbsorbentDroppedOutside, new CueDefinition("26_warning_dropped_outside", "수거용기 밖에 놓인 오염 흡착재를 집게로 다시 수거하십시오.") }
            };

        public bool IsPlaying => currentCue.HasValue;
        public int PendingCount => pendingCues.Count;

        void Reset()
        {
            narrationSource = GetComponent<AudioSource>();
            ConfigureNarrationSource();
        }

        void Awake()
        {
            if (narrationSource == null)
                narrationSource = GetComponent<AudioSource>();

            ConfigureNarrationSource();
            ConfigureSubtitleHudLayout();
            SetSubtitleVisible(false);
        }

        void OnEnable()
        {
            SubscribeToSceneEvents();
            leakWasStopped = IsLeakStopped();
            absorptionWasComplete = IsAbsorptionComplete();
        }

        IEnumerator Start()
        {
            if (followCamera)
                yield return AttachSubtitleHudToCamera();

            if (!playArrivalOnStart)
                yield break;

            EnqueueOnce(P102LeakNarrationCue.LeakSceneArrival);

            // Valve state is initialized in its Start(), so defer the initial-state check
            // until every scene component has completed its first-frame initialization.
            yield return null;

            if (suctionValve != null && !suctionValve.IsFullyClosed)
                EnqueueOnce(P102LeakNarrationCue.SuctionValveActivated);
        }

        void Update()
        {
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

            if (queueRoutine != null)
            {
                StopCoroutine(queueRoutine);
                queueRoutine = null;
            }

            if (narrationSource != null)
                narrationSource.Stop();

            currentCue = null;
            pendingCues.Clear();
            queuedCues.Clear();
            SetSubtitleVisible(false);
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

            playedOrScheduledCues.Add(cue);
            return EnqueueInternal(cue);
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
            if (!CueDefinitions.ContainsKey(cue))
            {
                Debug.LogWarning($"[P-102 Narration] Cue '{cue}' has no authored definition.", this);
                return false;
            }

            if (currentCue == cue || queuedCues.Contains(cue))
                return false;

            pendingCues.Enqueue(cue);
            queuedCues.Add(cue);

            if (queueRoutine == null && isActiveAndEnabled)
                queueRoutine = StartCoroutine(ProcessQueue());

            return true;
        }

        IEnumerator ProcessQueue()
        {
            while (pendingCues.Count > 0)
            {
                P102LeakNarrationCue cue = pendingCues.Dequeue();
                queuedCues.Remove(cue);
                currentCue = cue;

                CueDefinition definition = CueDefinitions[cue];
                AudioClip clip = Resources.Load<AudioClip>(ResourceFolder + definition.ResourceName);
                if (clip == null)
                {
                    Debug.LogError(
                        $"[P-102 Narration] Missing Resources audio clip '{ResourceFolder}{definition.ResourceName}'.",
                        this);
                    currentCue = null;
                    continue;
                }

                ShowSubtitle(definition.Subtitle);
                narrationSource.clip = clip;
                narrationSource.Play();

                while (narrationSource.isPlaying && narrationSource.clip == clip)
                    yield return null;

                SetSubtitleVisible(false);
                currentCue = null;
            }

            queueRoutine = null;
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
            if (!AreBothValvesClosed())
                return;

            EnqueueOnce(P102LeakNarrationCue.BothValvesClosed);
            EnqueueOnce(P102LeakNarrationCue.LeakReductionStarted);
            QueueLeakStoppedGuidanceIfReady();
        }

        void QueueLeakStoppedGuidanceIfReady()
        {
            if (!AreBothValvesClosed() || !IsLeakStopped())
                return;

            EnqueueOnce(P102LeakNarrationCue.LeakReductionCompleted);
            EnqueueOnce(P102LeakNarrationCue.AbsorbentSockPlacementActivated);
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

        void ConfigureNarrationSource()
        {
            if (narrationSource == null)
                return;

            narrationSource.playOnAwake = false;
            narrationSource.loop = false;
            narrationSource.spatialBlend = 0f;
        }

        IEnumerator AttachSubtitleHudToCamera()
        {
            if (subtitleRoot == null)
            {
                Debug.LogWarning(
                    "[P-102 Narration] Subtitle Root is not assigned; the subtitle HUD cannot follow the camera.",
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

            subtitleRoot.SetParent(subtitleCamera, false);
            subtitleRoot.localPosition = new Vector3(
                0f,
                subtitleVerticalOffset,
                subtitleDistance);
            subtitleRoot.localRotation = Quaternion.identity;
            subtitleRoot.localScale = Vector3.one * subtitleScale;
        }

        void ConfigureSubtitleHudLayout()
        {
            if (subtitleRoot != null)
            {
                Canvas subtitleCanvas = subtitleRoot.GetComponent<Canvas>();
                if (subtitleCanvas != null)
                    subtitleCanvas.renderMode = RenderMode.WorldSpace;

                subtitleRoot.sizeDelta = new Vector2(1100f, 200f);
                subtitleRoot.localScale = Vector3.one * subtitleScale;
            }

            if (subtitleCanvasGroup != null &&
                subtitleCanvasGroup.transform is RectTransform panelRect &&
                panelRect != subtitleRoot)
            {
                SetStretchRect(panelRect, Vector2.zero, Vector2.zero);
            }

            if (subtitleText != null)
            {
                if (subtitleText.rectTransform != subtitleRoot)
                {
                    SetStretchRect(
                        subtitleText.rectTransform,
                        new Vector2(45f, 20f),
                        new Vector2(-45f, -20f));
                }

                subtitleText.alignment = TextAlignmentOptions.Center;
                subtitleText.textWrappingMode = TextWrappingModes.Normal;
                subtitleText.enableAutoSizing = true;
                subtitleText.fontSizeMin = 30f;
                subtitleText.fontSizeMax = 46f;
                subtitleText.fontSize = 46f;
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
