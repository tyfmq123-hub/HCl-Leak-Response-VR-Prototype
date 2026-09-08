# HCl VR Telemetry v1 runtime setup

The Unity client always writes contract v1 events to:

`Application.persistentDataPath/TrainingTelemetry/{session_id}.jsonl`

Server delivery is enabled by a device-local file that is created automatically on
the first run:

`Application.persistentDataPath/HClTelemetry/telemetry-config.json`

```json
{
  "enabled": true,
  "endpoint": "https://feedback.sumpyo.kr/api/v1/telemetry/batches",
  "telemetry_key": "PUT_THE_TELEMETRY_WRITE_KEY_ON_THE_DEVICE_ONLY",
  "timeout_seconds": 12
}
```

Do not commit a populated copy of this file. The telemetry write key is not an
administrator credential and must not be printed in logs. For local PC development,
`HCL_VR_TELEMETRY_KEY` and `HCL_VR_TELEMETRY_ENDPOINT` environment variables may
override the file. `HCL_VR_BUILD_ID` may override the automatic Unity build GUID.

The sender posts at most 200 events per request. Accepted and duplicate event IDs are
recorded under `TrainingTelemetry/UploadState`. Rejected events are preserved in a
diagnostic JSONL sidecar. The original session JSONL is retained, so network or server
failure cannot stop training and unacknowledged events are retried on the next run.

## Contextual wrong-input payload

Wrong input analytics are scoped to the active interaction window. Stick axis/click,
menu, UI, tracking, gaze, movement, automatic completion, and paused/inactive periods
are excluded. `PrimaryButton` and `SecondaryButton` keep their contract control names,
while the payload also exposes Quest-facing `expected_key` / `actual_key` values
(`A`, `B`, `X`, `Y`).

Each unique wrong hand/key pair emits at most one rejected `InteractionAttempted`
event per interaction. The `InteractionCompleted.payload` is the authoritative
analytics unit and includes:

- `input_analytics_version: 2` (separates contextual summaries from legacy rows)
- `expected_hand`, `expected_control`, `expected_key`
- `had_wrong_input`
- `wrong_input_press_count`, `wrong_input_distinct_count`, `wrong_input_repeat_count`
- `wrong_input_events_emitted`, `wrong_input_unemitted_distinct_count`
- `first_wrong_*`, `last_wrong_*`, and the ordered `wrong_inputs` array
- `wrong_input_pattern` and `wrong_input_signal_quality`

Rapid five-or-more wrong presses in a two-second active-time window are marked as
rapid input. Dashboard aggregation must count a session/interaction once instead of
summing raw presses, and should isolate low-quality rapid multi-button exploration
from likely control-mapping confusion.
