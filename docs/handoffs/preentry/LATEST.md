# PreEntry → Integration 인수인계

## 2026-09-08 PreEntry — 풍향 변환 후 확인 게이지 추적

- 역할: PreEntry
- 브랜치: `feature/preentry` (`origin/develop` `5770f85`에 fast-forward한 뒤 게이지 수정을 다시 적용)
- commit: 이 작업에서 `feature/preentry`에 커밋·푸시 (main 직접 반영 없음)
- 작성자/작성일: PreEntry Codex / 2026-09-08

### 변경 목적과 동작

- `WaitWindChange`에서 `WindPivot`이 회전하면 풍향계 메시는 새 위치로 가지만, `EntrySafetyWindsockGazeProgress`는 Runtime Root 아래 초기 world 좌표에 고정되어 있었다.
- 게이지는 화살표(`EntrySafetyStepVisuals`)와 같이 `Windsock_UnityReady` 렌더러 bounds 중심을 따라간다. 초기 배치에서 bounds 중심까지의 world 오프셋을 한 번 저장하고, 변환 중에도 숨긴 채로 그 오프셋을 유지한다.
- 풍향계를 가리키는 노란 화살표는 기존 `EntrySafetyStepVisuals` 동작이다. 이번 작업에서 화살표 코드/씬 배치는 수정하지 않았다.
- 풍향계 피벗/위치, gaze 타깃 Collider, 상태 머신, TTS는 변경하지 않았다. Scene/Prefab 수동 작업 없음.

### 변경 파일

- `Assets/2.YJH/Scripts/Scenario/Gaze/EntrySafetyGazeProgressVisual.cs`
- `Assets/2.YJH/Scripts/Scenario/EntrySafetyScenarioController.cs`
- `docs/handoffs/preentry/LATEST.md`

### 검증

- 수행한 테스트: 코드 정적 확인. Unity PlayMode/Quest 실기기 미검증
- Console compile error: 미검증 / Missing Script: 미검증
- 허용 범위 밖 변경: 없음

### 알려진 문제와 위험

- PlayMode에서 `CheckChangedWind` 게이지가 회전한 풍향계 메시(화살표가 가리키는 위치) 옆에 붙는지 확인이 필요하다.
- 게이지는 bounds 중심의 world 이동만 따라간다. 화살표처럼 메시 바로 위로 붙이는 보정은 넣지 않았다.

### Integration 요청

- 없음

## 2026-09-07 Integration — Game View/씬 전환 후속 정정

- 빈 Game View를 만드는 위험이 있는 `XRGameViewFullFrame` 별도 덮어쓰기와 shader를 삭제하고 Unity 기본 XR mirror로 복구. 전체 눈 시야 확장 기능은 철회했다.
- App `SceneFlowController`와 새 `SceneLoadingBudget`에서 전환 작업 분산/Player 로딩 예산 제한과 복구를 적용. Stage Scene/Prefab/동작/TTS/작성 pose는 변경하지 않았다.
- Edit Mode 162/162, App smoke 33개, 뚜껑·집게·물방울·카운트 회귀 PASS. 실제 Quest mirror/시스템 모래시계 소멸은 미검증. 잠깐의 전환 fade와 초기 활성화의 긴 프레임은 남을 수 있다.
- 수동 작업: Play 중지→컴파일 완료→App 재실행만 필요. setup/Scene 재생성 불필요.
- 파일·테스트·알려진 문제·검증 제한은 [후속 보고서](../../integration/XR_MIRROR_TRANSITIONS_2026-09-07.md) 참고. 추가 Integration 요청 없음.

## 2026-09-07 Integration — 풍향계 gaze / 측정기 수납 음성

- `EntrySafetyCameraGazeAdapter`에서 HMD 중심 시선 기준 7° 획득/10° 유지, `EntrySafetyGazeCoordinator`에서 풍향계만 0.45초 이탈 허용. 풍향계 위치/피벗 및 패널 gaze는 보존했다.
- `EntrySafetyScenarioController`: gaze 진행률은 음성 중에도 시작하고 완료는 음성 종료 후 처리. 결과 표시와 음성을 동시에 기다려 재측정 결과 후 추가 지연 제거. 수납 안내 중 왼손 Grip 허용. App `StageLifecycleAdapter`의 입력 telemetry창도 동일 시점에 맞춘다.
- Goal01/02 TTS player는 실제 AudioSource 상태와 Pause를 사용. canonical PreEntry MP3 20개 재생성. 순서는 재측정 결과 → Grip 수납 → 오른손 A 패널 이동이며 상태 머신 전이표는 변경하지 않았다.
- `05_PreEntry.unity`/Prefab/측정기·ghost pose는 변경하지 않았다. 수동 setup 불필요. 허용각은 adapter Inspector에서 조절 가능하다.
- Edit Mode 160/160 통과. Quest 시선/손 조작·전체 음성 청취는 미검증. 전체 파일·검증·알려진 제한: [Integration 보고서](../../integration/VR_REGRESSION_2026-09-07.md). 추가 Integration 요청 없음.

## 2026-09-04 Integration correction — Interaction별 오입력 요약

- 오입력은 활성 `InteractionPrompted`부터 `InteractionCompleted` 사이에서만 수집한다. Gaze/Automatic/Movement 단계, Pause·비활성 시간, 스틱 축·스틱 클릭·메뉴·UI 입력은 제외한다.
- 각 완료 이벤트에 요구 손·요구 control·Quest 버튼명, 첫/마지막 오입력, 총 물리 오입력 횟수, 서로 다른 오입력 목록, 반복 횟수와 입력 패턴을 함께 저장한다. Primary/Secondary는 손에 따라 A/X와 B/Y로 구분한다.
- 같은 Interaction에서 같은 잘못된 손·키를 반복해도 `InteractionAttempted`는 한 번만 발행한다. 빠르게 여러 버튼을 누른 경우 `RapidMultiButtonInput`과 `Low` 신뢰도로 표시해 한 사용자의 연타가 전체 키 통계를 지배하지 않게 했다.
- 성공 입력이 명시되지 않은 완료 콜백에서 마지막 오입력을 성공 키로 재사용하지 않는다. 마지막 관측 입력이 요구 손·키와 일치할 때만 사용하고, 아니면 요구 입력으로 완료를 기록한다.
- 회귀 검사 4/4 통과: 기존 v1 JSON/Batch, 아날로그 눌림 중복 제거, A/B/X/Y 문맥 요약·스틱 제외, 마지막 오입력의 성공 키 오인 방지. Scene/Prefab/훈련 진행은 변경하지 않았다.

## 2026-09-04 Integration correction — 입력 통계 중복 방지

- `SharedXRInputContract`의 Grip/Trigger 값이 한 번 누른 상태에서 미세하게 변하며 `performed`를 여러 번 발생시켜, 같은 물리 입력이 잘못된 입력 여러 건으로 부풀려지던 문제를 수정했다.
- `TrainingTelemetryRuntimeMonitor`가 버튼을 `performed -> canceled` 한 주기로 추적한다. 스틱 축과 스틱 클릭은 서비스에서 오입력 수집 대상에서 제외하며, 훈련 Pause와 앱 Pause에서는 눌림 상태를 안전하게 초기화한다.
- 로컬 JSONL의 `PreEntry_StoreDetector=Right/SecondaryButton` 기록은 2026-09-03 17:35 세션 당시 계약이며, 현재 소스는 이미 `Left/Grip`으로 수정되어 있다. `PrimaryButton`은 `MoveToPT`와 `MoveToNextArea`에서 사용하는 오른손 A 버튼이라 현재 프로젝트에 실제로 존재한다.
- 새 입력 회귀 검사들을 포함한 집중 검사 4/4 통과. 임시 테스트 실행기는 제거했으며 최종 Unity 도메인 리로드의 C# 컴파일 오류는 0개다.
- 변경 파일: `Assets/Scripts/App/TrainingTelemetryService.cs`, `Assets/Scripts/App/TrainingTelemetryRuntimeMonitor.cs`, `Assets/Editor/Tests/AppFlowStateTests.cs`, 세 Stage 인수인계 문서. Scene/Prefab/훈련 진행은 변경하지 않았다.
- 서버에 이미 저장된 과거 이벤트는 보존하며, 요구 키별 한 줄 집계와 사용자용 버튼 명칭은 서버 대시보드 소스에서 별도로 처리해야 한다.

## 2026-09-03 Integration correction — 측정기 수납 순서와 Grip 입력

- 재측정 결과 확인 후 안내 순서를 `측정기 수납 -> 패널 이동 -> 패널 확인`으로 명확히 분리했다. 설비 패널 안내가 측정기 수납보다 먼저 나오지 않는다.
- 측정기 수납 입력은 오른손 `B`에서, 측정기를 들고 있는 왼손의 `Grip`으로 변경했다. 물리 입력, Shared XR 입력, Editor simulator 입력, 텔레메트리 기대 손/버튼을 모두 같은 계약으로 맞췄다.
- 변경된 `09_ConfirmReducedHcl`, `10_StoreDetector`, `11_MoveToPanel` MP3를 새 문구로 다시 생성했고 화면 안내도 동일한 의미로 갱신했다.
- 변경 파일: `EntrySafetyInputAdapter.cs`, `EntrySafetyInstructionCatalog.cs`, `StageLifecycleAdapter.cs`, PreEntry MP3 3개, 공용 TTS 대본 문서.
- 왼손 Grip이 측정기 꺼내기와 수납 명령에 각각 동일하게 전달되는 회귀 검사까지 통과했다. Unity 6000.4.8f1 최종 컴파일 오류 0개이며, 수납 순서는 상태 머신의 기존 `ConfirmReducedHcl -> StoreDetector -> MoveToPT`를 그대로 유지하고 Scene/Prefab은 수정하지 않았다.
- Quest에서는 재측정 직후 왼손 Grip 1회로 수납되고, 그 뒤에만 오른손 A 패널 이동 안내가 나오는지 최종 확인한다.
- 커밋·푸시는 하지 않았으며 현재 Integration 브랜치는 `integration-windsock-pivot`이다.

> PreEntry 담당 Codex는 작업 종료 전에 아래 내용을 실제 결과로 교체한다. 빈 항목을 삭제하지 말고 `없음` 또는 `미검증`으로 표시한다.

## 작업 식별

- 브랜치: `feature/preentry`
- commit: PreEntry 씬용 EV 환경 자산을 `Assets/2.YJH`로 복사 (푸시 예정)
- 기준 develop commit: `d445493` feat: decouple stage equipment inventory and bags
- 작성자/작성일: PreEntry Codex / 2026-08-27

## 변경 목적과 동작

- 기존 공용 이동 기반은 그대로 두고, PreEntry 로컬 Collider로 허용 구역 밖 이동을 막는다. 남·서·동에 더해 북쪽 `NorthWall`을 항상 유지한다. 접근 금지 overlay/Barricade는 감압 후 시각적으로 해제하지만, A 핸드오프 전에도 북쪽으로 누출 구역에 걸어 들어가지 못한다.
- P/T 패널은 로코모션이 본체를 뚫지 못하도록 솔리드 `PtPanelLocomotionBlocker` BoxCollider를 런타임에 붙인다. STOP 버튼/응시 Collider는 트리거로 유지해 정지 판정은 손 위치 기반으로 그대로 동작한다.
- STOP 판정은 `P102StopZoneAnchor` 위치 + Inspector 반경 `0.18m`를 쓴다. 앵커 Scale은 반경에 쓰지 않는다. 최소 반경 클램프는 `0.04m`이다.
- 측정기 화면 응시 임계값을 `0.75`(약 41°)로 완화하고, `Camera.main` 대신 live XR 카메라를 사용한다.
- 측정기 꺼내기 실패 시 자막 힌트를 보여 준다. 가방 존 미스와 Inventory에 `GasDetector`가 없는 경우를 구분한다.
- 측정기 장착 중 왼손 Grip Animator를 live `Left Controller`에서 찾아 잠근다.
- overlay 복귀 경계 Box는 메시 크기(약 5.5m×5m)에 맞춰 폭 `6m`, 두께 `0.2m`로 줄였다. 북쪽 전체 차단은 `NorthWall`이 담당한다.
- `05_PreEntry`가 쓰던 `Assets/3.JH` EV 프리팹/머티리얼/메시는 다른 Stage 폴더 충돌을 피하려고 PreEntry 소유 경로로 복사했다. 씬 GUID는 `Assets/2.YJH/Prefabs/EV`, `Art/Models/EV`, `Art/Materials/EV`, `Art/Textures/EV`를 가리킨다. 셰이더는 기존 `Assets/2.YJH/Art/Shaders/S_JH_BlenderCel_01.shader`를 재사용한다. `Assets/3.JH`는 커밋하지 않는다.
- `05_PreEntry`의 `PreEntry_ResponseBag`을 Stage 가방으로 사용한다. App 공용 가방을 복제하거나 수정하지 않는다.
- 누출 구역 앞에서 3D 누출 앰비언트를 Stage 시작부터 재생한다.
- `SharedTrainingTelemetry`로 PreEntry 진입/완료, 패널 시작·완료·P-102 STOP 실패, 측정기 가방 사용만 전달한다. 윈드삭은 구현하지 않았다.

## 변경 파일

- `Assets/2.YJH/Scripts/Scenario/EntrySafetyAppXrResolve.cs`
- `Assets/2.YJH/Scripts/Scenario/EntrySafetyGuidanceView.cs`
- `Assets/2.YJH/Scripts/Scenario/EntrySafetyHclDetectorController.cs`
- `Assets/2.YJH/Scripts/Scenario/EntrySafetyHclGripLock.cs`
- `Assets/2.YJH/Scripts/Scenario/EntrySafetyLeakAmbientPlayer.cs`
- `Assets/2.YJH/Scripts/Scenario/EntrySafetyMovementBounds.cs`
- `Assets/2.YJH/Scripts/Scenario/EntrySafetyNoAccessOverlayController.cs`
- `Assets/2.YJH/Scripts/Scenario/EntrySafetyScenarioController.cs`
- `Assets/2.YJH/Scripts/Scenario/Gaze/EntrySafetyGazeProgressVisual.cs`
- `Assets/2.YJH/Scripts/Scenario/Goal02_PtPanel/EntrySafetyGoal2PtPanelController.cs`
- `Assets/2.YJH/Scripts/Scenario/Goal02_PtPanel/EntrySafetyPtPanelLocomotionBlocker.cs`
- `Assets/2.YJH/Scripts/Scenario/Guidance/EntrySafetyInstructionCatalog.cs`
- `Assets/2.YJH/Prefabs/EV/`
- `Assets/2.YJH/Art/Models/EV/`
- `Assets/2.YJH/Art/Materials/EV/`
- `Assets/2.YJH/Art/Textures/EV/`
- `Assets/Scenes/05_PreEntry.unity`
- `docs/handoffs/preentry/LATEST.md`

## Scene·Prefab·Inspector 수동 변경

- `Assets/Scenes/05_PreEntry.unity`에 `PreEntry_ResponseBag` 계층이 있다. 위치 `(0.12, 0.92, -16.20)`. 위치·크기·Collider는 PreEntry Scene에서 직접 수정한다.
- `EntrySafetyHclDetectorController._minimumDisplayGazeDot` = `0.75`.
- STOP: `_useAnchorScaleAsRadius` = 0, `_stopZoneRadiusMeters` = `0.18`. 위치만 `P102StopZoneAnchor`로 조정한다.
- `EntrySafetyNoAccessOverlay` 복귀 경계: width 6, height 5, depth 0.2.
- 패널 4열은 환경으로 유지하지만 훈련 로직은 P-102 Inspector 참조와 P-102 이름 fallback만 사용한다.
- `EntrySafetyLeakAmbientPlayer`, `EntrySafetyMovementBounds`는 `EntrySafetyRuntimeRoot`에 런타임 AddComponent 한다. Inspector 수동 할당은 없다.
- `05_PreEntry`의 `Barricade`를 ScenarioController `_barricade`에 연결했다. 접근 금지 해제 단계에서 비활성, LeakResponse 핸드오프 시 복귀 경계로 다시 활성한다.
- `05_PreEntry`의 EV 환경 배치는 `Assets/2.YJH` 복사본을 참조한다. `Assets/3.JH` EV 원본은 PreEntry가 커밋하지 않는다.

## 공용 계약 사용

- 사용한 입력/장비/상태/텔레메트리 계약:
  - `SessionEquipmentInventory.Ensure()` — 단독 시드 `GasDetector`, 꺼내기 `TryCheckOut`, 보관 `ReturnCheckedOut`
  - P-102 정지 시 `SharedIncidentState.Current.SetFacilityRunState(Stopped)`
  - `SharedTrainingTelemetry.Current` (`StepEntered`/`StepCompleted`/`ContentViewed`/`BagItemRequested`/`EquipmentUsed`)
  - `SharedEquipmentIds.GasDetector`
  - 기존 `SharedXRInputContract` Left Y / Right Trigger / Right A·B
- 계약의 사용 위치:
  - `EntrySafetyGoal2PtPanelController` — P-102 행·STOP·운전 상태 기록
  - `EntrySafetyScenarioController` — 텔레메트리, 패널 실패, Inventory 체크아웃/반환, 꺼내기 실패 자막
  - `EntrySafetyEquipmentTransferZone` — `PreEntry_BagRetrievalZone` 로컬 존
  - Scene `PreEntry_ResponseBag` — 위치·Visual·Zone·Anchor

## 검증

- 수행한 테스트: 코드 정적 확인. Unity PlayMode/단독 Scene 실행은 수행하지 않음.
- Unity 스크립트 컴파일: 이번 결함 완화 이후 미검증
- 결과: 런타임 미검증
- Quest 실기기 확인: 미검증
- Console compile error: 0개 / Missing Script: 런타임 미검증
- 완료 확인 항목 1~6은 PlayMode 재검증 필요. 항목 7(허용 범위 밖 변경 0)은 로컬 diff 기준으로 충족.

## 알려진 문제와 위험

- `SharedIncidentState`에서 `FacilityId`/설비 선택이 제거되어, PreEntry는 P-102 고정 행만 절차에 연결한다. 이전 `EntrySafetyFacilityCatalog`는 병합 시 삭제했다.
- 단독 `05_PreEntry`의 Stage XR Origin ContinuousMove/Teleport Provider는 기존처럼 비활성이다. 자유 이동은 App 공용 이동 기반에 의존한다.
- `SharedIncidentState` Null 구현은 `SetFacilityRunState`가 no-op이다. App 미등록 단독 실행에서는 정지 상태가 세션에 남지 않는다.
- 패널 응시 단계는 자동 완료라 Editor 시뮬레이터의 패널 Gaze Confirm은 Stop 이전 단계에서 쓰이지 않는다.
- 윈드삭 신규 구현 없음. 기존 Goal 1 풍향 응시/회전은 그대로 둔다.
- PPE/Inventory의 `GasDetector` 기록이 어긋나도 PreEntry의 필수 측정기 제시와 진행은 차단하지 않는다. 체크아웃은 가능한 경우에만 장비 수량 기록으로 유지한다.
- 작업 중 `ProjectSettings/Packages/com.unity.probuilder/Settings.json`과 폰트 자산이 git status에 dirty로 보일 수 있다. PreEntry가 내용을 수정하지 않았고 restore/reset하지 않았다.

## Integration 요청

- App 공용 가방을 PreEntry Scene에서 수정할 필요는 없다. `05_PreEntry`의 `PreEntry_ResponseBag` / `ResponseBag_HardShell_Visual` / `PreEntry_BagRetrievalZone` / `PreEntry_ItemRetrievalAnchor`가 PreEntry 소유다.
- 단독 Scene 자유 이동이 필요하면 Stage XR Origin의 기존 ContinuousMoveProvider 활성화를 Integration이 공용 이동 정책과 맞춰 달라. PreEntry는 이동 Provider를 추가/활성화하지 않았다.
- 공용 CharacterController가 PreEntry 로컬 Default-layer BoxCollider와 충돌하지 않으면, Collision Matrix 또는 Common XR CharacterController 설정을 확인해 달라. 북쪽 `NorthWall`이 추가되었으므로 같은 확인이 필요하다.
- Full 모드에서 PPE가 `GasDetector`를 정상적으로 `Store`하는지는 장비 이력 정확성을 위해 별도로 확인한다. 이 기록 누락은 더 이상 PreEntry 진행을 차단하지 않는다.
- `ProjectSettings/Packages/com.unity.probuilder/Settings.json`과 TMP 폰트 바이너리는 이 작업에서 stage/commit하지 않는다.
- 기존 App 빈 셸 이슈(`Assets/2.YJH/Docs/PREENTRY_APP_INTEGRATION_REQUESTS.md` 1~5: Detector 셸, EquipmentTransferZone 셸, PreEntry XR Profile, Right Controller 경로)는 이번 작업에서 재현하지 못했으나 요청은 유지한다.

## 권장 병합·재검증 순서

1. 변경 파일이 PreEntry 허용 경로만인지 확인
2. PreEntry 단독 실행: 이동 경계(북쪽 포함), PT_Panel을 몸으로 뚫지 못함, STOP 손은 그대로, 진입 전 누출음, 가방 존 Y 측정기 실패 안내, 패널 도착 3초, STOP, 감압 후 Barricade/overlay 시각 해제, A 이동
3. App Full 모드에서 측정기 응시 측정, P-102 패널 행/정지 상태/`SharedIncidentState` 전달 확인
4. `PreEntry_ResponseBag`이 지정 위치에 있고 App 가방과 중복되지 않는지 확인
5. 전체 App PlayMode Smoke

## 2026-08-28 Integration correction — Grip retrieval and retained barricade

- This section supersedes the earlier Left-Y and barricade-deactivation notes in this document.
- Gas-detector retrieval now uses left Grip through `SharedXRInputContract.Grip` plus the Quest/OpenXR `gripButton` fallback. Left Y is no longer subscribed for this action.
- The first Grip must occur inside `PreEntry_BagRetrievalZone`. The detector rises to `PreEntry_ItemRetrievalAnchor`, remains there after the opening Grip is released, and enables its XR Grab only after that release. A second Grip directly grabs the detector; Stage progression occurs on the actual Grab event, not on bag activation.
- The detector keeps one runtime Rigidbody, fitted BoxCollider, and XRGrabInteractable on its existing prefab instance. No duplicate detector or App-owned bag object is created.
- `Barricade` remains active through `ClearAccessRestriction`; only the separate no-access overlay may fade. The existing right-A handoff remains the route to LeakResponse.
- Changed files: `EntrySafetyInputAdapter.cs`, `EntrySafetyScenarioController.cs`, `EntrySafetyHclDetectorController.cs`, and `Guidance/EntrySafetyInstructionCatalog.cs`.
- Validation: current Assembly-CSharp Roslyn compile completed with 0 errors; `05_PreEntry.unity` has 0 duplicate YAML document IDs. Quest zone reach, rise height, second-Grip attach pose, and right-A handoff remain for the scene owner to verify.

## 2026-09-02 Integration correction — 측정기 첫 Grip/두 번째 Grip 분리

- 작업 브랜치: `integration-windsock-pivot` (commit 없음).
- 첫 Grip은 측정기를 가방 위로 올리는 동작만 수행한다. 측정기가 상승 중이거나 상승을 마친 것만으로는 `DetectorGrabbed`를 발생시키지 않고 단계도 완료하지 않는다.
- 첫 Grip과 두 번째 Grip은 모두 `EntrySafetyInputAdapter`의 의도적인 Grip 입력 경로를 사용한다. 첫 Grip을 완전히 놓아야 다음 Grip 이벤트가 생기므로, 버튼을 계속 누른 상태에서 자동 장착되지 않는다.
- 두 번째 Grip에서는 첫 Grip에 사용한 정확한 `Common XR Origin` 왼손을 그대로 전달해 저장된 Ghost 포즈에 직접 안착한다. Additive Scene에 남아 있는 다른 `Left Controller`를 전역 검색해 잡지 않는다.
- 런타임 XRI 선택이 측정기를 먼저 가져가거나 잘못된 Rigidbody 좌표를 만들지 않도록 측정기의 `XRGrabInteractable`은 비활성 상태로 유지한다. Ghost 안착에 실제로 성공한 뒤에만 `DetectorGrabbed`를 한 번 발생시킨다.
- 측정기 상승 높이는 `0.58m`이다. 상승이 끝나고 Ghost 스냅샷이 준비된 뒤에만 두 번째 Grip을 받을 수 있다.
- 가방 위치 `(-0.281, -0.004, -15.2)`는 유지했다. `PreEntry_BagRetrievalZone`은 플레이어 방향까지 닿도록 로컬 size `(1.6, 1.5, 1.2)`, center `(0.4, 0.75, 0.25)`로 확장했다.
- 변경 파일: `Assets/2.YJH/Scripts/Scenario/EntrySafetyAppXrResolve.cs`, `EntrySafetyEquipmentTransferZone.cs`, `EntrySafetyHclDetectorController.cs`, `EntrySafetyScenarioController.cs`, `Assets/Scenes/05_PreEntry.unity`, `docs/handoffs/preentry/LATEST.md`.
- 전체 흐름 Play Mode 검사 PASS: `Common XR Origin` 왼손 연결 성공, 가방에서 플레이어 방향 `0.72m` 떨어진 손 위치에서 첫 Grip 인식, 측정기 활성/Renderer 표시, 상승량 `0.58m`, 첫 Grip 뒤 단계 `EquipDetector` 유지, Grip 해제 후 두 번째 Grip에서 `MeasureInitialHcl`로 전환, 장착 상태 70프레임 유지, NaN Rigidbody 오류 0회.
- Quest 실기기에서 실제 컨트롤러 Grip 감도와 최종 손 체감은 최종 확인이 필요하다.

## 2026-09-02 Integration correction — Quest Grip 재무장과 측정기 제시 차단 제거

- 사용자 Quest 실행 기록에서 한 번의 물리 Grip 도중 측정기 성공 이벤트까지 발생한 사실을 확인했다. 여러 LeftHand 장치 값의 최댓값을 섞던 입력 필터가 순간적인 해제를 새 입력으로 오판할 수 있었다.
- App의 `SharedXRInputContract` 값을 유일한 통합 입력값으로 사용하고, App 계약이 없을 때만 OpenXR/InputSystem 순으로 fallback한다.
- 첫 Grip 뒤에는 값이 `0.2` 이하인 완전 해제 상태가 `0.12초` 연속 유지되어야만 다음 Grip을 받을 수 있다. 한 프레임 입력 끊김이나 계속 누른 상태에서는 두 번째 입력을 만들지 않는다.
- 새 Grip은 `0.7` 이상에서 한 번만 발생한다. 이전의 누름 유지 0.18초 조건은 제거했다.
- `GasDetector` Inventory 체크아웃 실패는 장비 이력에만 영향을 주고, Stage 필수 측정기의 활성화·상승·진행을 막지 않는다.
- 회귀 검사 PASS: 첫 Grip 1회, 계속 누름 중 중복 0회, 1프레임 순간 해제 후 중복 0회, `0.12초` 이상 완전 해제 후 두 번째 Grip 1회. Unity 재컴파일 오류 0개.
- 실제 사용자 로그의 왼손 위치 `(-0.3235, 1.1231, -15.2095)`는 현재 `PreEntry_BagRetrievalZone` 안에 있었고 Grip 값은 `1.0`이었다. 감지 범위는 이번 결함 원인이 아니었다.

## 2026-09-02 Integration correction — 화면 표시 확인 후 단계 완료 및 종료 순서 안전화

- `EntrySafetyHclDetectorController`는 두 번째 Grip 직후 단계를 완료하지 않는다. 측정기를 실제 `PPE_VrHand_Left_Wearable` 아래에 붙이고, 한 프레임 렌더링한 뒤 활성 Renderer가 확인되어야 `DetectorGrabbed`를 발생시킨다.
- Additive Scene이나 비활성 복제본의 왼손 및 raw controller origin fallback은 사용하지 않는다. 첫 Grip을 받은 추적 손 아래의 활성 wearable root만 장착 대상으로 인정한다.
- App 종료 시 `StageLifecycleAdapter.OnDestroy()`가 이미 파괴된 PreEntry 오버레이를 다시 복구하려 하지 않도록, 종료 해제 경로에서는 Stage presentation 비활성화를 호출하지 않는다.
- `EntrySafetyNoAccessOverlayController`와 호출부에는 Unity destroyed-object(fake null) 검사를 추가했다. PreEntry에서 LeakResponse로 넘어갈 때 PreEntry ambient player도 함께 정지한다.
- Unity PlayMode 직접 검사 PASS: 측정기 parent=`PPE_VrHand_Left_Wearable`, 손과 거리=`0.000m`, 활성 표시 Renderer=`2`, 이후 Full PreEntry→LeakResponse 전환 완료. 수정 뒤 `MissingReferenceException` 재발 0회, C# 컴파일 오류 0개.
- 전체 smoke는 그 이후 LeakResponse 폐기물 용기 뚜껑의 기존 Grab 구성 검사에서 중단됐다. 측정기 표시·단계 완료·PreEntry 종료 수명주기 검사는 해당 실패 전에 모두 통과했다.

## 2026-09-02 Integration correction — 0 크기 측정기 복구 및 Ghost 표시 일치

- 이 절은 바로 위의 `손과 거리=0.000m`, `활성 표시 Renderer=2`만으로 표시 성공이라 판단했던 검증 결과를 대체한다. 당시에는 측정기 루트 Scale이 실제로 `(0,0,0)`이었고, 존재만 하는 Renderer를 잘못 성공으로 계산했다.
- 측정기 원본은 Integration에서 비활성화되는 Scene 전용 XR rig 아래에 있어 World Scale이 0이 된다. 가방 Anchor로 옮길 때 `worldPositionStays=true`를 사용하면서 이 0 Scale이 복사된 것이 본체 미표시의 직접 원인이었다.
- 첫 Grip 제시 시 가방 Anchor 로컬 공간으로 다시 부모를 지정하고 원래 Scale을 복구한 다음 Collider를 맞춘다. 본체 최대 길이가 `0.08m` 미만이면 표시 실패로 간주하여 `DetectorGrabbed`와 다음 단계 전환을 발생시키지 않는다.
- 저장 Ghost 손은 측정기 화면 뒤로 `0.055m` 이동해 화면을 가리지 않게 했다. 두 번째 Grip 뒤에는 해당 Ghost 포즈의 복제 손을 실제 주황 장갑 Material로 표시하고, 추적 장갑의 중복 Renderer는 보관 후 숨긴다. 측정기 반납·리셋 시 원래 추적 장갑 Renderer를 복구한다.
- Unity 실제 입력 경로 검사 PASS: `EntrySafetyInputAdapter` 해제→첫 Grip→가방 Zone→상승→완전 해제→두 번째 Grip→한 프레임 표시 확인→`MeasureInitialHcl` 순서가 통과했다.
- 렌더 검사 PASS: 측정기 루트 World Scale 약 `(1,1,1)`, 본체 Bounds 약 `(0.129, 0.218, 0.169)m`, 손목과 본체 중심 거리 `0.098m`. `Logs/PreEntryDetectorGhostValidation.png`과 `Logs/PreEntryDetectorGripValidation.png`에서 Ghost 및 주황 장갑 뒤로 측정기 본체가 표시되고 전면 화면이 가려지지 않음을 확인했다.
- 같은 실행에서 C# 컴파일 오류, `MissingReferenceException`, NaN Rigidbody 좌표, 표시 실패 후 조기 단계 전환은 0회였다. 전체 smoke는 이후 별도 LeakResponse 폐기물 용기 뚜껑 Grab 구성 검사에서 중단됐다.

## 2026-09-02 Integration correction — 측정기 전면 및 상부 Grip 자세 복구

- TMP 측정기 화면의 읽는 면은 `Transform.forward` 반대쪽이다. 제시 회전과 검증 카메라가 이 전면을 사용하도록 고정해 측정기 뒷면을 성공으로 오인하지 않는다.
- Ghost 손 이동 방향을 전면 반대인 `+display.forward`로 수정했다. 상승 중 Ghost는 화면 뒤에서 측정기 측면을 감싸고, 화면 앞으로 나오지 않는다.
- 두 번째 Grip 검증은 실제 왼손 손목을 Ghost 손목에 맞춘 뒤 수행한다. 렌더 결과에서 Ghost와 주황 장갑이 같은 위치를 사용하며 `READY / HCl --.- ppm` 화면이 정방향으로 완전히 보였다.
