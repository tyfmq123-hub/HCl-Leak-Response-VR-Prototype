# Integration VR 회귀 수정 — 2026-09-07

> 후속 정정: 이 문서의 `XRGameViewFullFrame` 미러 확장은 사용자의 빈 Game View 보고로 철회·삭제했다. Unity 기본 XR mirror를 복구했으며, 상세 변경과 현재 검증 범위는 [미러/씬 전환 후속 보고서](XR_MIRROR_TRANSITIONS_2026-09-07.md)를 따른다. 아래 160개 테스트 결과는 삭제 전의 역사적 결과이며 실제 Quest mirror 검증이 아니었다.

## 적용 범위와 보존

- 검증 브랜치: `integration/vr-regression-20260907`, 기준 `origin/develop`의 `cc7ccb732ce4064e11ae6580bae5561486602ba1`.
- 실제 적용 대상: `E:\WorkSpace\HCl-Leak-Response-VR-Prototype-main`.
- 사용자가 열어 둔 원본 Unity 대신 독립 Git worktree에서 컴파일·렌더·Play Mode 검증을 수행했다. 원본의 수정 대상 102개 파일을 먼저 기준 버전과 대조했고, 추가 문서/신규 파일을 포함한 최종 120개 파일도 적용 직전 충돌 검사했다.
- 원본 반영 완료 후 120개 파일의 내용이 검증본과 일치함을 재확인했다. 작성된 Stage Scene 3개와 Prefab 207개는 검증 기준과 동일하며 변경하지 않았다.
- `04_PPE`, `05_PreEntry`, `06_LeakResponse` Scene과 Prefab, 풍향계 피벗/위치, 뚜껑/고스트 작성 위치, 컨트롤러 추적, HMD FOV/projection은 변경하지 않는다.
- 서버 주소·키·피드백 UI 동작·전송·완료 이벤트 계약은 변경하지 않는다. 기존 파일이나 브랜치 전체를 되돌리는 작업은 하지 않는다.

## 수정 내용

| 증상 | 확인 및 수정 |
|---|---|
| 집게가 버튼 입력에 떨어짐 | `P102CarryInputLock`이 집게를 잡은 interactor의 Select만 유지한다. 실제 Trigger는 흡착재 집기/놓기에 그대로 전달한다. 마지막 반납 안내 완료 + 표시된 고스트 30cm 이내 + Grip을 한 번 놓은 뒤 새로 누른 경우만 반납한다. 입력 설정은 반납/해제/파괴 때 복구한다. |
| 반납 표시가 안내보다 늦음 | 반납 고스트는 `AllSocksRecovered` 안내 시작에 표시한다. 반납 동작은 안내 완료 후 활성화된다. |
| 뚜껑이 용기 안에 남음 | 씬 작성 닫힘 Pose를 보존한다. 선택 취소 때도 닫힘/보관 Pose로 복구하고, 일반 release 후에는 XRI의 오래된 물리 target 대신 실제 손에서 놓은 Pose로 고스트 근접을 판정한다. 선택 중에는 안정된 손 anchor를 따른다. |
| 고스트 크기 차이 | 부모에서 분리하여 만드는 runtime ghost에는 원본의 world scale을 사용한다. 작성된 ghost transform/material은 건드리지 않는다. |
| 미스트만 나오고 물방울 없음 | `FallingDrops`가 초기 intensity=0으로 바뀐 뒤 재연결되면서 0이 원래 방출량으로 재저장되던 경로를 수정했다. 동일 ParticleSystem의 원래 방출량은 한 번만 캡처한다. 실제 App runtime에서 rate=7과 살아 있는 물방울을 확인했다. |
| 플랜지 `3초`가 뒤집힘 | App의 stage 이동 전 카메라 위치로 한 번만 정렬하던 방식을 수정했다. UI 위치는 플랜지에 고정하고, 표시 중 방향만 실제 카메라를 향하도록 갱신한다. 렌더 캡처에서 `3초` 정방향을 확인했다. |
| 풍향계 시선 시작/유지 불안정 | 기존 작은 collider를 정확히 맞춰야 하는 조건 대신 HMD 중심 ray와 대상 bounds에 대한 획득 7°/유지 10° 범위를 적용했다. 잠깐 이탈은 0.45초 허용한다. 음성 안내 중에도 진행률이 쌓이고 완료 전이는 안내 종료 후 한다. 패널 gaze 범위는 변경하지 않았다. |
| PreEntry 수납 순서/간격 | 재측정 → 낮은 농도 확인 → 왼손 Grip 수납 → 오른손 A로 패널 이동 순서를 유지했다. 결과 최소 표시 시간과 실제 음성 재생을 동시에 기다리므로 음성이 끝난 뒤 추가 1초를 기다리지 않는다. 수납 안내 중에도 수납 가능하다. 텔레메트리 입력창도 실제 허용 시점에 맞췄다. |
| PPE 및 전체 TTS 불일치 | 현재 Grip 잡기/착용 위치에서 놓기, 장비 3종 수납 후 가방 챙기기, 수거 절차/집게 반납을 기준으로 대본을 검토했다. 잘못된 Y/B 안내를 제거하고 canonical MP3 83개를 재생성했다. 개별 파일 앞뒤 무음만 약 0.06초로 정리했다. 문장 중간 무음은 제거하지 않는다. |
| TTS 사이 지연/보드 타이밍 | PPE의 별도 훈련 시계 대기와 실제 AudioSource 재생 대기가 중복되지 않도록 수정했다. PreEntry intro/outro도 실제 재생 종료에 맞춘다. PPE 화이트보드는 고정 타이머 대신 실제 CueStarted를 따른다. 일시정지 시 음성도 멈춘다. |
| 화면 자글거림 | 공용 App camera clip 범위를 0.01–1000m에서 0.05–200m로 줄여 깊이 정밀도를 개선하고 UI 패널·버튼·글자의 겹치는 깊이를 분리했다. PC URP MSAA 4→8, Mobile은 성능을 고려해 기존 4 유지, 이방성 필터링을 강제로 활성화했다. 흐림 후처리는 추가하지 않는다. |
| 집게 사용 중 반복 경고 | kinematic Rigidbody의 속도를 매 프레임 설정해 발생하던 두 개의 물리 경고를 제거했다. 필요할 때만 dynamic Rigidbody 속도를 초기화하며 경고 자체를 숨기지 않는다. |
| Unity Game 화면이 Quest보다 좁음 | Editor 전용 `XRGameViewFullFrame`이 제공된 왼쪽 눈 texture 전체를 화면 비율에 맞춰 여백을 넣어 표시한다. HMD FOV, 렌더 해상도, projection을 변경하거나 별도 관전자 camera를 추가하지 않는다. `Window > HCl VR > Full Eye in Game View`로 원래 미러로 돌아갈 수 있다. 지원되지 않는 미해결 MSAA source는 기본 미러를 유지한다. |

## 주요 변경 파일

- PPE: `Assets/1.JSR/Scripts/PPETTSManager.cs`, `Whiteboard/PPEProcessDiagramController.cs`.
- PreEntry: `Assets/2.YJH/Scripts/Scenario/EntrySafetyScenarioController.cs`, `EntrySafetyCameraGazeAdapter.cs`, `Gaze/EntrySafetyGazeCoordinator.cs`, `Guidance/EntrySafetyGoal01TtsPlayer.cs`, `EntrySafetyGoal02TtsPlayer.cs`.
- LeakResponse: `Assets/3.SSH/Scripts/LeakResponse/HClAbsorptionVisualController.cs`, `Narration/P102CarryInputLock.cs`, `P102RecoveryTongsRuntime.cs`, `P102WasteContainerRuntime.cs`, `P102RuntimeUtility.cs`, `P102FlangeGazeRuntime.cs`, `P102LeakNarrationController.cs`.
- Integration: `Assets/Scripts/App/AppUI.cs`, `StageLifecycleAdapter.cs`, `Assets/Scenes/01_App.unity`, `Assets/Settings/PC_RPAsset.asset`, `ProjectSettings/QualitySettings.asset`.
- Editor: `Assets/Editor/XRGameViewFullFrame.cs`와 shader, `IntegrationVisualAudit.cs`, `IntegrationVrPlayValidation.cs`, `Tests/IntegrationVrRegressionTests.cs` (신규 파일은 `.meta` 포함).
- 음성: `Assets/Resources/TrainingNarration/{PPE,PreEntry,LeakResponse}/*.mp3` 83개. 기존 `.meta` GUID 유지.
- 대본/재생성: `docs/audio/FULL_COURSE_TTS_SCRIPT.md`, `narration-manifest.json`, `scripts/audio/generate_training_narration.py`.

## 검증

- Edit Mode: **160/160 통과**, 실패/건너뜀 0. 새 테스트는 gaze 범위/유지/음성 gating, 집게 입력 bypass 복구, 물방울 재연결, canonical 음성 83개의 SHA256·대본·Unity AudioClip 로딩, 2D/texture-array 미러 shader GPU 복사와 검은 여백을 검사한다.
- Unity C# 컴파일 오류: Edit Mode 및 최종 두 Play Mode 실행에서 **0개**.
- App → LeakResponse 실제 로딩 후 저장 Pose 유지, 놓친 뚜껑 복귀, 선택 취소 복귀, 집게 Grip/Trigger 20회 반복 유지, 마지막 고스트 전/외부 반납 차단, 고스트에서 반납, 원래 입력 복구, 플랜지 방향 검증 통과. 테스트 시작 안내와 완료 cue는 주입하고 XRI 선택/해제 콜백은 실제 사용한다.
- 렌더 증거: `Logs/VRRegression/01-runtime-closed.png`, `02-after-cancel.png`, `03-flange-countdown.png`; 결과 `runtime-validation.txt`.
- 최종 물리 Grip edge 입력을 다음 Unity frame에 주입해 `UpdateReturnInput`에서 실제 반납되는 경로까지 **PASS**. 직접 반납 메서드만 호출해서 통과시킨 결과와 구분한다. 최종 실행의 집게/뚜껑 kinematic 속도 경고 **0건**.
- 기존 `AppPlayModeSmokeRunner`: **33개 검사 PASS**. 전체 PPE→PreEntry→LeakResponse→Complete, 반복 시작/처음으로, PPE/PreEntry/LeakResponse 단독 실행을 검사했다. 공용 XR/카메라/가방/AudioListener/EventSystem과 PreEntry 측정기 표시·잡기 회귀도 포함한다. 단계 완료 자체는 자동 검사에서 강제로 진행하므로 사람이 실제 모든 동작을 한 E2E 테스트는 아니다.
- 검증 전용 Unity의 Search index 초기화에서 UnityEditor 내부 `ArgumentOutOfRangeException`이 한 번 출력되는 환경 문제가 있다. 프로젝트 C# 컴파일 실패/훈련 runtime exception과 구분한다. 테스트 목적과 무관한 Search 설정/패키지는 수정하지 않았다.

## 아직 직접 확인하지 않은 것

- Quest를 착용한 실제 양손 추적, 고스트 위치의 체감, 연속 훈련 전체 음성 청취, 장시간 프레임 유지.
- 실제 연결 중인 XR provider가 노출하는 eye texture에 대한 Game View 미러 결과. GPU 복사/비율은 테스트했지만 실제 Quest 전체 시야와 일치한다고 단정하지 않는다. 한쪽 눈과 두 눈 합산 시야의 범위는 같지 않다.
- 얇은 철망 등 서브픽셀 패턴, Link/Air Link 압축, 재투영에 의한 잔떨림이 모두 사라진다는 보장은 없다. 화질 변경 후 HMD 성능과 눈의 피로도는 별도 확인이 필요하다. Android MSAA를 무조건 8로 올려 성능을 낮추지는 않았다.
- 음성은 실제 파일 생성/해시/디코딩 검증을 했지만 사람이 들어서 발음·억양을 검수한 것은 아니다.

## Inspector / 수동 작업

- 기존 작성된 Scene/Prefab을 다시 생성하거나 setup menu를 실행하지 않는다.
- 풍향계 허용각은 `EntrySafetyCameraGazeAdapter`의 `_windsockAcquireAngle`, `_windsockRetainAngle`로 조정할 수 있다. 기본 7°/10°.
- 최종 Quest 확인 순서: PPE 보드/장비 수납 → PreEntry 시선 흔들림/재측정/수납 → Leak 물방울/밸브/3초 UI/뚜껑/집게 반복 버튼/반납 → 완료.
- 새 음성은 같은 canonical resource 경로에 유지한다. 과거 Scene WAV 또는 과거 생성기로 덮어쓰지 않는다. 재생성 후 manifest와 Edit Mode 검사를 실행한다.

## 참고한 공식 구현/문서

- 설치된 Unity 6000.4.8f1의 XRI `XRInputButtonReader`, `XRBaseInputInteractor`, `XRInteractionManager`와 URP/XR mirror 구현을 확인했다.
- [Unity URP Asset 설정](https://docs.unity3d.com/ja/6000.0/Manual/urp/universalrp-asset.html)
- [Unity XRDisplaySubsystem 소스](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Modules/XR/Subsystems/Display/XRDisplaySubsystem.bindings.cs)

## Integration 요청

추가 서버/관리자 권한은 필요 없다. 기존 원본 작업 폴더의 무관한 미커밋 변경은 보존한다. 원격 main/develop에 이번 변경을 자동 push하지 않는다.
