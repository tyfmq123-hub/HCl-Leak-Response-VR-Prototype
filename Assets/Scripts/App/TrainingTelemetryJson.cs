using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace HClTraining.App
{
    /// <summary>
    /// Minimal JSON writer used to keep the append-only local record and the server
    /// event byte-for-byte compatible without adding a runtime JSON dependency.
    /// </summary>
    public static class TrainingTelemetryJson
    {
        public static string SerializeRecord(TrainingTelemetryRecord record)
        {
            if (record == null)
                return string.Empty;

            var json = JsonUtility.ToJson(record);
            var payload = string.IsNullOrWhiteSpace(record.payload_json)
                ? "{}"
                : record.payload_json;
            if (json.Length == 0 || json[json.Length - 1] != '}')
                return json;

            return json.Substring(0, json.Length - 1) + ",\"payload\":" + payload + "}";
        }

        public static string SerializeBatch(
            string batchId,
            string sessionId,
            string sentAtUtc,
            IReadOnlyList<TrainingTelemetryRecord> records)
        {
            var builder = new StringBuilder(1024);
            builder.Append('{');
            AppendName(builder, "schema_version");
            AppendQuoted(builder, TrainingTelemetryService.ContractSchemaVersion);
            builder.Append(',');
            AppendName(builder, "batch_id");
            AppendQuoted(builder, batchId);
            builder.Append(',');
            AppendName(builder, "session_id");
            AppendQuoted(builder, sessionId);
            builder.Append(',');
            AppendName(builder, "sent_at_utc");
            AppendQuoted(builder, sentAtUtc);
            builder.Append(',');
            AppendName(builder, "events");
            builder.Append('[');
            if (records != null)
            {
                for (var i = 0; i < records.Count; i++)
                {
                    if (i > 0)
                        builder.Append(',');
                    builder.Append(SerializeRecord(records[i]));
                }
            }
            builder.Append("]}");
            return builder.ToString();
        }

        public static bool TryDeserializeRecord(string json, out TrainingTelemetryRecord record)
        {
            record = null;
            if (string.IsNullOrWhiteSpace(json) ||
                json.IndexOf("\"schema_version\":\"1.0\"", StringComparison.Ordinal) < 0 ||
                json.IndexOf("\"event_id\":", StringComparison.Ordinal) < 0)
            {
                return false;
            }

            try
            {
                record = JsonUtility.FromJson<TrainingTelemetryRecord>(json);
                return record != null &&
                       string.Equals(record.schema_version, TrainingTelemetryService.ContractSchemaVersion, StringComparison.Ordinal) &&
                       !string.IsNullOrWhiteSpace(record.event_id) &&
                       !string.IsNullOrWhiteSpace(record.session_id);
            }
            catch
            {
                record = null;
                return false;
            }
        }

        internal static void AppendName(StringBuilder builder, string name)
        {
            AppendQuoted(builder, name);
            builder.Append(':');
        }

        internal static void AppendQuoted(StringBuilder builder, string value)
        {
            builder.Append('"');
            if (value != null)
            {
                for (var i = 0; i < value.Length; i++)
                {
                    var character = value[i];
                    switch (character)
                    {
                        case '"': builder.Append("\\\""); break;
                        case '\\': builder.Append("\\\\"); break;
                        case '\b': builder.Append("\\b"); break;
                        case '\f': builder.Append("\\f"); break;
                        case '\n': builder.Append("\\n"); break;
                        case '\r': builder.Append("\\r"); break;
                        case '\t': builder.Append("\\t"); break;
                        default:
                            if (character < 32)
                                builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                            else
                                builder.Append(character);
                            break;
                    }
                }
            }
            builder.Append('"');
        }
    }

    internal sealed class TelemetryPayloadBuilder
    {
        readonly StringBuilder builder = new StringBuilder(256);
        bool hasValue;

        public TelemetryPayloadBuilder()
        {
            builder.Append('{');
        }

        public TelemetryPayloadBuilder Add(string name, string value, bool includeEmpty = false)
        {
            if (value == null || (!includeEmpty && value.Length == 0))
                return this;
            BeginValue(name);
            TrainingTelemetryJson.AppendQuoted(builder, value);
            return this;
        }

        public TelemetryPayloadBuilder Add(string name, int value)
        {
            BeginValue(name);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public TelemetryPayloadBuilder Add(string name, long value)
        {
            BeginValue(name);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public TelemetryPayloadBuilder Add(string name, bool value)
        {
            BeginValue(name);
            builder.Append(value ? "true" : "false");
            return this;
        }

        public TelemetryPayloadBuilder AddNull(string name)
        {
            BeginValue(name);
            builder.Append("null");
            return this;
        }

        public TelemetryPayloadBuilder AddRaw(string name, string rawJson)
        {
            BeginValue(name);
            builder.Append(string.IsNullOrWhiteSpace(rawJson) ? "null" : rawJson);
            return this;
        }

        public override string ToString()
        {
            return builder.ToString() + "}";
        }

        void BeginValue(string name)
        {
            if (hasValue)
                builder.Append(',');
            hasValue = true;
            TrainingTelemetryJson.AppendName(builder, name);
        }
    }
}
