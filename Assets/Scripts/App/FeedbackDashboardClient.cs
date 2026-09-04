using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace HClTraining.App
{
    [DefaultExecutionOrder(-9500)]
    [DisallowMultipleComponent]
    public sealed class FeedbackDashboardClient : MonoBehaviour
    {
        const string EndpointPreferenceKey = "HClTraining.FeedbackEndpoint";
        const string DefaultEndpointUrl =
            "https://feedback.sumpyo.kr/api/v1/feedback";
        const string OutboxDirectoryName = "FeedbackOutbox";

        static readonly Dictionary<string, string> CategoryByQuickFeedbackId =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "interaction_difficult", "controls" },
                { "guidance_insufficient", "guidance" },
                { "equipment_confusing", "equipment" },
                { "movement_uncomfortable", "movement" },
                { "sequence_unclear", "sequence" },
                { "runtime_error", "bug" },
                { "no_problem", "positive" },
            };

        static readonly Dictionary<string, string> LabelByQuickFeedbackId =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "interaction_difficult", "조작이 어려웠어요" },
                { "guidance_insufficient", "안내가 부족했어요" },
                { "equipment_confusing", "장비 사용이 헷갈렸어요" },
                { "movement_uncomfortable", "이동이 불편했어요" },
                { "sequence_unclear", "진행 순서를 모르겠어요" },
                { "runtime_error", "오류가 발생했어요" },
                { "no_problem", "특별한 문제 없었어요" },
            };

        [SerializeField] string endpointUrl = DefaultEndpointUrl;
        [SerializeField, Min(3)] int timeoutSeconds = 12;

        bool initialized;
        bool flushingOutbox;

        string ActiveEndpoint
        {
            get
            {
                var overrideUrl = PlayerPrefs.GetString(EndpointPreferenceKey, string.Empty);
                return NormalizeEndpoint(string.IsNullOrWhiteSpace(overrideUrl) ? endpointUrl : overrideUrl);
            }
        }

        string OutboxPath => Path.Combine(Application.persistentDataPath, OutboxDirectoryName);

        public bool IsConfigured => !string.IsNullOrEmpty(ActiveEndpoint);

        public void Initialize()
        {
            if (initialized)
                return;

            initialized = true;
            if (IsConfigured)
                StartCoroutine(FlushOutbox());
        }

        public static void SetEndpointOverride(string url)
        {
            var normalized = NormalizeEndpoint(url);
            if (string.IsNullOrEmpty(normalized))
                PlayerPrefs.DeleteKey(EndpointPreferenceKey);
            else
                PlayerPrefs.SetString(EndpointPreferenceKey, normalized);
            PlayerPrefs.Save();
        }

        public void SubmitQuickFeedback(
            TrainingStage targetStage,
            TrainingFeedbackRecord feedback,
            Action<bool, string> completed = null)
        {
            if (feedback == null)
            {
                completed?.Invoke(false, "저장할 피드백 정보가 없습니다.");
                return;
            }

            var payload = CreatePayload(targetStage, feedback);
            if (payload == null)
            {
                completed?.Invoke(false, "선택한 피드백 항목을 변환하지 못했습니다.");
                return;
            }

            var json = JsonUtility.ToJson(payload);
            if (!IsConfigured)
            {
                SaveToOutbox(json, feedback.SessionId);
                completed?.Invoke(false, "서버 주소가 없어 기기에 안전하게 보관했습니다.");
                return;
            }

            StartCoroutine(Send(json, feedback.SessionId, true, completed));
        }

        DashboardFeedbackPayload CreatePayload(
            TrainingStage targetStage,
            TrainingFeedbackRecord feedback)
        {
            var categories = new List<string>();
            var labels = new List<string>();
            var blocking = false;

            if (feedback.QuickFeedbackIds != null)
            {
                for (var i = 0; i < feedback.QuickFeedbackIds.Count; i++)
                {
                    var id = feedback.QuickFeedbackIds[i];
                    if (!CategoryByQuickFeedbackId.TryGetValue(id, out var category))
                        continue;
                    if (!categories.Contains(category))
                        categories.Add(category);
                    if (LabelByQuickFeedbackId.TryGetValue(id, out var label))
                        labels.Add(label);
                    if (id == "runtime_error")
                        blocking = true;
                }
            }

            if (categories.Count == 0)
                return null;

            var telemetry = AppBootstrap.Instance != null ? AppBootstrap.Instance.Telemetry : null;
            var interactionId = telemetry != null && !string.IsNullOrWhiteSpace(telemetry.ActiveInteractionId)
                ? telemetry.ActiveInteractionId
                : telemetry != null ? telemetry.LastCompletedInteractionId : string.Empty;

            return new DashboardFeedbackPayload
            {
                submissionId = Guid.NewGuid().ToString("N"),
                stage = ToDashboardStage(targetStage),
                categories = categories.ToArray(),
                severity = blocking ? "blocking" : "normal",
                message = "VR 완료 화면 빠른 피드백: " + string.Join(", ", labels),
                source = "unity_vr",
                sessionId = feedback.SessionId,
                interactionId = interactionId,
                appVersion = Application.version,
                buildId = TrainingTelemetryService.CurrentBuildId,
                platform = Application.platform.ToString(),
                deviceModel = SystemInfo.deviceModel,
                lastEventSequence = telemetry != null ? telemetry.LastEventSequence : 0,
                submittedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
            };
        }

        IEnumerator Send(
            string json,
            string sessionId,
            bool saveOnFailure,
            Action<bool, string> completed)
        {
            using var request = new UnityWebRequest(ActiveEndpoint, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            request.timeout = Mathf.Max(3, timeoutSeconds);

            yield return request.SendWebRequest();

            var success = request.result == UnityWebRequest.Result.Success &&
                request.responseCode >= 200 && request.responseCode < 300;
            if (success)
            {
                completed?.Invoke(true, "피드백이 대시보드로 전송되었습니다.");
                yield break;
            }

            if (saveOnFailure)
                SaveToOutbox(json, sessionId);

            Debug.LogWarning(
                $"[Feedback Dashboard] 전송 실패. HTTP {request.responseCode}: {request.error}",
                this);
            completed?.Invoke(false, "연결할 수 없어 기기에 보관했습니다. 다음 실행 때 다시 전송합니다.");
        }

        IEnumerator FlushOutbox()
        {
            if (flushingOutbox || !IsConfigured || !Directory.Exists(OutboxPath))
                yield break;

            flushingOutbox = true;
            var files = Directory.GetFiles(OutboxPath, "*.json");
            Array.Sort(files, StringComparer.Ordinal);

            for (var i = 0; i < files.Length; i++)
            {
                string json;
                try
                {
                    json = File.ReadAllText(files[i], Encoding.UTF8);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Feedback Dashboard] 보관 파일 읽기 실패: {exception.Message}", this);
                    continue;
                }

                var sent = false;
                yield return Send(json, string.Empty, false, (success, _) => sent = success);
                if (!sent)
                    break;

                try
                {
                    File.Delete(files[i]);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Feedback Dashboard] 보관 파일 삭제 실패: {exception.Message}", this);
                }
            }

            flushingOutbox = false;
        }

        void SaveToOutbox(string json, string sessionId)
        {
            try
            {
                Directory.CreateDirectory(OutboxPath);
                var safeSessionId = string.IsNullOrWhiteSpace(sessionId) ? "session" : sessionId;
                var fileName = $"{DateTime.UtcNow:yyyyMMddTHHmmssfff}_{safeSessionId}.json";
                File.WriteAllText(Path.Combine(OutboxPath, fileName), json, Encoding.UTF8);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Feedback Dashboard] 피드백 보관 실패: {exception.Message}", this);
            }
        }

        static string ToDashboardStage(TrainingStage stage)
        {
            return stage == TrainingStage.PPE
                ? "ppe"
                : stage == TrainingStage.PreEntry
                    ? "preentry"
                    : stage == TrainingStage.LeakResponse ? "leakresponse" : "full_course";
        }

        static string NormalizeEndpoint(string url)
        {
            return string.IsNullOrWhiteSpace(url) ? string.Empty : url.Trim().TrimEnd('/');
        }

        [Serializable]
        sealed class DashboardFeedbackPayload
        {
            public string submissionId;
            public string stage;
            public string[] categories;
            public string severity;
            public string message;
            public string source;
            public string sessionId;
            public string interactionId;
            public string appVersion;
            public string buildId;
            public string platform;
            public string deviceModel;
            public int lastEventSequence;
            public string submittedAtUtc;
        }
    }
}
