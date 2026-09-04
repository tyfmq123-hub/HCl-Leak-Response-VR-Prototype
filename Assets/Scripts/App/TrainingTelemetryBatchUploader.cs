using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace HClTraining.App
{
    [DefaultExecutionOrder(-9600)]
    [DisallowMultipleComponent]
    public sealed class TrainingTelemetryBatchUploader : MonoBehaviour
    {
        const string DefaultEndpoint = "https://feedback.sumpyo.kr/api/v1/telemetry/batches";
        const string KeyEnvironmentVariable = "HCL_VR_TELEMETRY_KEY";
        const string EndpointEnvironmentVariable = "HCL_VR_TELEMETRY_ENDPOINT";
        const int MaximumEventsPerBatch = 200;
        const int MaximumRequestBytes = 240 * 1024;
        const float IdleScanSeconds = 2f;
        const float FailureRetrySeconds = 15f;

        readonly List<TrainingTelemetryRecord> pending = new List<TrainingTelemetryRecord>(MaximumEventsPerBatch);
        readonly HashSet<string> acknowledged = new HashSet<string>(StringComparer.Ordinal);

        TrainingTelemetryService telemetry;
        TelemetryLocalSettings settings;
        bool initialized;
        bool pumpRunning;
        bool scanRequested;
        bool missingKeyReported;
        float nextSettingsReloadAt;

        string TelemetryDirectory => Path.Combine(Application.persistentDataPath, "TrainingTelemetry");
        string UploadStateDirectory => Path.Combine(TelemetryDirectory, "UploadState");
        public static string ConfigurationPath => Path.Combine(
            Application.persistentDataPath,
            "HClTelemetry",
            "telemetry-config.json");

        public void Initialize(TrainingTelemetryService service)
        {
            if (initialized)
                return;
            initialized = true;
            telemetry = service;
            if (telemetry != null)
                telemetry.RecordPersisted += OnRecordPersisted;
            EnsureConfigurationTemplate();
            ReloadSettings();
            scanRequested = true;
            StartCoroutine(UploadPump());
        }

        void OnRecordPersisted(TrainingTelemetryRecord record, bool immediateFlush)
        {
            scanRequested = true;
            if (immediateFlush && !pumpRunning)
                StartCoroutine(UploadPump());
        }

        IEnumerator UploadPump()
        {
            if (pumpRunning)
                yield break;
            pumpRunning = true;
            try
            {
                while (initialized)
                {
                    if (Time.unscaledTime >= nextSettingsReloadAt)
                        ReloadSettings();
                    if (!IsConfigured())
                    {
                        if (!missingKeyReported)
                        {
                            missingKeyReported = true;
                            Debug.LogWarning(
                                "[Training Telemetry] 수집 키가 없어 서버 전송을 보류합니다. " +
                                "로컬 JSONL 기록은 계속됩니다. 설정 파일: " + ConfigurationPath,
                                this);
                        }
                        yield return new WaitForSecondsRealtime(FailureRetrySeconds);
                        continue;
                    }

                    missingKeyReported = false;
                    if (!scanRequested && !HasPendingFiles())
                    {
                        yield return new WaitForSecondsRealtime(IdleScanSeconds);
                        continue;
                    }
                    scanRequested = false;

                    if (!TryLoadNextBatch(out var sessionId))
                    {
                        yield return new WaitForSecondsRealtime(IdleScanSeconds);
                        continue;
                    }

                    var sent = false;
                    yield return SendBatch(sessionId, pending, success => sent = success);
                    if (!sent)
                    {
                        scanRequested = true;
                        yield return new WaitForSecondsRealtime(FailureRetrySeconds);
                    }
                    else
                    {
                        scanRequested = true;
                        yield return null;
                    }
                }
            }
            finally
            {
                pumpRunning = false;
            }
        }

        bool TryLoadNextBatch(out string sessionId)
        {
            sessionId = string.Empty;
            pending.Clear();
            if (!Directory.Exists(TelemetryDirectory))
                return false;

            string[] files;
            try
            {
                files = Directory.GetFiles(TelemetryDirectory, "*.jsonl", SearchOption.TopDirectoryOnly);
                Array.Sort(files, StringComparer.Ordinal);
            }
            catch
            {
                return false;
            }

            for (var fileIndex = 0; fileIndex < files.Length; fileIndex++)
            {
                var candidateSessionId = Path.GetFileNameWithoutExtension(files[fileIndex]);
                LoadAcknowledged(candidateSessionId);
                try
                {
                    foreach (var line in File.ReadLines(files[fileIndex], Encoding.UTF8))
                    {
                        if (pending.Count >= MaximumEventsPerBatch)
                            break;
                        if (!TrainingTelemetryJson.TryDeserializeRecord(line, out var record) ||
                            acknowledged.Contains(record.event_id))
                        {
                            continue;
                        }
                        pending.Add(record);
                    }
                }
                catch
                {
                    pending.Clear();
                    continue;
                }

                if (pending.Count == 0)
                    continue;
                sessionId = candidateSessionId;
                TrimBatchToRequestLimit(sessionId);
                return pending.Count > 0;
            }
            return false;
        }

        void TrimBatchToRequestLimit(string sessionId)
        {
            while (pending.Count > 1)
            {
                var probe = TrainingTelemetryJson.SerializeBatch(
                    Guid.NewGuid().ToString("N"),
                    sessionId,
                    UtcNow(),
                    pending);
                if (Encoding.UTF8.GetByteCount(probe) <= MaximumRequestBytes)
                    return;
                pending.RemoveAt(pending.Count - 1);
            }
        }

        IEnumerator SendBatch(
            string sessionId,
            IReadOnlyList<TrainingTelemetryRecord> records,
            Action<bool> completed)
        {
            var batchId = Guid.NewGuid().ToString("N");
            var body = TrainingTelemetryJson.SerializeBatch(batchId, sessionId, UtcNow(), records);
            using var request = new UnityWebRequest(settings.endpoint, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
            request.SetRequestHeader("X-HCL-Telemetry-Key", settings.telemetry_key);
            request.timeout = Mathf.Clamp(settings.timeout_seconds, 3, 60);
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
            {
                Debug.LogWarning(
                    "[Training Telemetry] Batch upload deferred. HTTP " + request.responseCode + 
                    ". Local JSONL remains queued.",
                    this);
                completed?.Invoke(false);
                yield break;
            }

            TelemetryBatchResponse response;
            try
            {
                response = JsonUtility.FromJson<TelemetryBatchResponse>(request.downloadHandler.text);
            }
            catch
            {
                response = null;
            }
            if (response == null || response.rejected == null ||
                response.accepted + response.duplicates + response.rejected.Length != records.Count)
            {
                Debug.LogWarning(
                    "[Training Telemetry] Batch response was incomplete; events remain queued for an idempotent retry.",
                    this);
                completed?.Invoke(false);
                yield break;
            }

            for (var i = 0; i < response.rejected.Length; i++)
            {
                var rejected = response.rejected[i];
                if (rejected == null || string.IsNullOrWhiteSpace(rejected.event_id))
                    continue;
                SaveRejected(sessionId, rejected);
            }

            for (var i = 0; i < records.Count; i++)
                Acknowledge(sessionId, records[i].event_id);
            completed?.Invoke(true);
        }

        void LoadAcknowledged(string sessionId)
        {
            acknowledged.Clear();
            var path = AcknowledgedPath(sessionId);
            if (!File.Exists(path))
                return;
            try
            {
                foreach (var line in File.ReadLines(path, Encoding.UTF8))
                    if (!string.IsNullOrWhiteSpace(line)) acknowledged.Add(line.Trim());
            }
            catch
            {
                acknowledged.Clear();
            }
        }

        void Acknowledge(string sessionId, string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId) || acknowledged.Contains(eventId))
                return;
            try
            {
                Directory.CreateDirectory(UploadStateDirectory);
                File.AppendAllText(AcknowledgedPath(sessionId), eventId + Environment.NewLine, new UTF8Encoding(false));
                acknowledged.Add(eventId);
            }
            catch
            {
                // A missing acknowledgement only causes an idempotent resend.
            }
        }

        void SaveRejected(string sessionId, TelemetryRejectedEvent rejected)
        {
            try
            {
                Directory.CreateDirectory(UploadStateDirectory);
                var builder = new StringBuilder();
                builder.Append('{');
                TrainingTelemetryJson.AppendName(builder, "event_id");
                TrainingTelemetryJson.AppendQuoted(builder, rejected.event_id);
                builder.Append(',');
                TrainingTelemetryJson.AppendName(builder, "reason");
                TrainingTelemetryJson.AppendQuoted(builder, rejected.reason ?? string.Empty);
                builder.Append(',');
                TrainingTelemetryJson.AppendName(builder, "recorded_at_utc");
                TrainingTelemetryJson.AppendQuoted(builder, UtcNow());
                builder.Append('}').Append(Environment.NewLine);
                File.AppendAllText(
                    Path.Combine(UploadStateDirectory, sessionId + ".rejected.jsonl"),
                    builder.ToString(),
                    new UTF8Encoding(false));
            }
            catch
            {
                // The source JSONL remains available for manual diagnosis.
            }
        }

        bool HasPendingFiles()
        {
            try
            {
                return Directory.Exists(TelemetryDirectory) &&
                       Directory.GetFiles(TelemetryDirectory, "*.jsonl", SearchOption.TopDirectoryOnly).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        string AcknowledgedPath(string sessionId) => Path.Combine(UploadStateDirectory, sessionId + ".acked");

        bool IsConfigured() => settings != null && settings.enabled &&
                               !string.IsNullOrWhiteSpace(settings.endpoint) &&
                               !string.IsNullOrWhiteSpace(settings.telemetry_key);

        void ReloadSettings()
        {
            nextSettingsReloadAt = Time.unscaledTime + FailureRetrySeconds;
            settings = TelemetryLocalSettings.Load(ConfigurationPath, DefaultEndpoint);
        }

        static void EnsureConfigurationTemplate()
        {
            if (File.Exists(ConfigurationPath))
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigurationPath));
                var template = new TelemetryLocalSettings
                {
                    enabled = true,
                    endpoint = DefaultEndpoint,
                    telemetry_key = string.Empty,
                    timeout_seconds = 12,
                };
                File.WriteAllText(ConfigurationPath, JsonUtility.ToJson(template, true), new UTF8Encoding(false));
            }
            catch
            {
                // Missing configuration never blocks local telemetry or training.
            }
        }

        static string UtcNow() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

        void OnDestroy()
        {
            initialized = false;
            if (telemetry != null)
                telemetry.RecordPersisted -= OnRecordPersisted;
            telemetry = null;
        }

        [Serializable]
        sealed class TelemetryLocalSettings
        {
            public bool enabled = true;
            public string endpoint = DefaultEndpoint;
            public string telemetry_key = string.Empty;
            public int timeout_seconds = 12;

            public static TelemetryLocalSettings Load(string path, string fallbackEndpoint)
            {
                TelemetryLocalSettings loaded = null;
                try
                {
                    if (File.Exists(path))
                        loaded = JsonUtility.FromJson<TelemetryLocalSettings>(File.ReadAllText(path, Encoding.UTF8));
                }
                catch
                {
                    loaded = null;
                }
                if (loaded == null)
                    loaded = new TelemetryLocalSettings();

                var endpointOverride = Environment.GetEnvironmentVariable(EndpointEnvironmentVariable);
                var keyOverride = Environment.GetEnvironmentVariable(KeyEnvironmentVariable);
                loaded.endpoint = NormalizeEndpoint(
                    !string.IsNullOrWhiteSpace(endpointOverride) ? endpointOverride : loaded.endpoint,
                    fallbackEndpoint);
                if (!string.IsNullOrWhiteSpace(keyOverride))
                    loaded.telemetry_key = keyOverride.Trim();
                loaded.timeout_seconds = Mathf.Clamp(loaded.timeout_seconds, 3, 60);
                return loaded;
            }

            static string NormalizeEndpoint(string value, string fallback)
            {
                var endpoint = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
                return endpoint.TrimEnd('/');
            }
        }

        [Serializable]
        sealed class TelemetryBatchResponse
        {
            public int accepted;
            public int duplicates;
            public TelemetryRejectedEvent[] rejected;
        }

        [Serializable]
        sealed class TelemetryRejectedEvent
        {
            public string event_id;
            public string reason;
        }
    }
}
