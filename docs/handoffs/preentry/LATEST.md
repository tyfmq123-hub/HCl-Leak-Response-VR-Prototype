# PreEntry → Integration 인수인계

> PreEntry 담당 Codex는 작업 종료 전에 아래 내용을 실제 결과로 교체한다. 빈 항목을 삭제하지 말고 `없음` 또는 `미검증`으로 표시한다.

## 작업 식별

- 브랜치: `feature/preentry`
- commit: 작업 전(로컬 변경만 존재, 아직 commit하지 않음)
- 기준 develop commit: `febb455` Build shared integration foundation and enforce stage ownership
- 작성자/작성일: PreEntry Codex / 2026-08-20

## 변경 목적과 동작

- 기존 공용 이동 기반은 그대로 두고, PreEntry 로컬 Collider로 허용 구역 밖 이동을 막는다. 누출 쪽 입구는 기존 `EntrySafetyNoAccessOverlay` 경계를 사용한다.
- P/T 패널의 압력·온도·RUN 응시와 STOP/감압 재확인 응시를 자동 완료해 패널 절차를 간소화했다. 사용자 조작은 P-102 STOP 버튼 + 감압 대기로 줄였다.
- STOP 판정은 고정 P-102 행의 로컬 버튼/표시에 연결한다. 손-버튼 거리와 버튼 Collider로 VR 입력을 넓혔다.
- `05_PreEntry`에 `StageBagAnchor`를 배치했다. 가방 Visual은 복제하지 않는다.
- 누출 구역 앞(접근 금지 경계 너머)에서 3D 누출 앰비언트를 Stage 시작부터 재생한다.
- `SharedTrainingTelemetry`로 PreEntry 진입/완료, 패널 시작·완료·P-102 STOP 실패, 측정기 가방 사용만 전달한다. 윈드삭은 구현하지 않았다.

## 변경 파일

- `Assets/2.YJH/Scripts/Scenario/EntrySafetyLeakAmbientPlayer.cs`
- `Assets/2.YJH/Scripts/Scenario/EntrySafetyMovementBounds.cs`
- `Assets/2.YJH/Scripts/Scenario/EntrySafetyScenarioController.cs`
- `Assets/2.YJH/Scripts/Scenario/Goal02_PtPanel/EntrySafetyGoal2PtPanelController.cs`
- `Assets/2.YJH/Scripts/Scenario/Guidance/EntrySafetyInstruction.cs`
- `Assets/2.YJH/Scripts/Scenario/Guidance/EntrySafetyGuidanceCoordinator.cs`
- `Assets/2.YJH/Scripts/Scenario/Guidance/EntrySafetyInstructionCatalog.cs`
- `Assets/Scenes/05_PreEntry.unity`
- `docs/handoffs/preentry/LATEST.md`

## Scene·Prefab·Inspector 수동 변경

- `Assets/Scenes/05_PreEntry.unity` 루트에 `StageBagAnchor` 추가. 위치 `(0.12, 0.92, -16.20)`, 회전 identity. 시작 지점 왼허리/장비 존 옆이다.
- 패널 4열은 환경으로 유지하지만 훈련 로직은 P-102 Inspector 참조와 P-102 이름 fallback만 사용한다.
- `EntrySafetyLeakAmbientPlayer`, `EntrySafetyMovementBounds`는 `EntrySafetyRuntimeRoot`에 런타임 AddComponent 한다. Inspector 수동 할당은 없다.
- STOP 근접 반경 기본값 `0.32m`, 버튼 Collider 도달 `0.12m`. `_useAnchorScaleAsRadius`는 꺼 두었다.
- Prefab 변경 없음.

## 공용 계약 사용

- 사용한 입력/장비/상태/텔레메트리 계약:
  - P-102 정지 시 `SharedIncidentState.Current.SetFacilityRunState(Stopped)`
  - `SharedPlayerEquipment.IsLeftHandInRetrievalZone()` (기존 장비 존 fallback 유지)
  - `SharedTrainingTelemetry.Current` (`StepEntered`/`StepCompleted`/`ContentViewed`/`BagItemRequested`/`EquipmentUsed`)
  - `SharedEquipmentIds.GasDetector`
  - 기존 `SharedXRInputContract` Left Y / Right Trigger / Right A·B
- 계약의 사용 위치:
  - `EntrySafetyGoal2PtPanelController` — P-102 행·STOP·운전 상태 기록
  - `EntrySafetyScenarioController` — 텔레메트리 및 패널 실패 횟수
  - `EntrySafetyEquipmentTransferZone` — 기존 공용 가방 존 우선
  - Scene `StageBagAnchor` — App `TryPlaceAtStageAnchor`가 이름으로 찾음

## 검증

- 수행한 테스트: 코드 정적 확인, 허용 경로 diff 확인, Unity 스크립트 재컴파일 확인. Unity PlayMode/단독 Scene 실행은 수행하지 않음.
- Unity 스크립트 컴파일: 성공, 마지막 컴파일 요청 이후 `error CS` 0개
- 결과: 런타임 미검증
- Quest 실기기 확인: 미검증
- Console compile error: 0개 / Missing Script: 런타임 미검증
- 완료 확인 항목 1~6은 PlayMode 재검증 필요. 항목 7(허용 범위 밖 변경 0)은 로컬 diff 기준으로 충족.

## 알려진 문제와 위험

- Goal 2 TTS WAV와 자막은 P-102 고정 대본이다.
- 단독 `05_PreEntry`의 Stage XR Origin ContinuousMove/Teleport Provider는 기존처럼 비활성이다. 자유 이동은 App 공용 이동 기반에 의존한다.
- `SharedIncidentState` Null 구현은 `SetFacilityRunState`가 no-op이다. App 미등록 단독 실행에서는 정지 상태가 세션에 남지 않는다.
- 패널 응시 단계는 자동 완료라 Editor 시뮬레이터의 패널 Gaze Confirm은 Stop 이전 단계에서 쓰이지 않는다.
- 윈드삭 신규 구현 없음. 기존 Goal 1 풍향 응시/회전은 그대로 둔다.
- 작업 중 `ProjectSettings/Packages/com.unity.probuilder/Settings.json`이 git status에 dirty로 보였으나 content diff는 없고 CRLF 경고만 있다. PreEntry가 내용을 수정하지 않았고 restore/reset하지 않았다.

## Integration 요청

- App가 PreEntry Scene 로드 시 `StageBagAnchor`(world `(0.12, 0.92, -16.20)`)로 공용 가방을 배치하는지 확인해 달라. 가방 스케일/앞면 방향이 어색하면 App 가방 Visual만 조정하고, 앵커 위치가 잘못되면 PreEntry에 원하는 pose를 알려 달라.
- 단독 Scene 자유 이동이 필요하면 Stage XR Origin의 기존 ContinuousMoveProvider 활성화를 Integration이 공용 이동 정책과 맞춰 달라. PreEntry는 이동 Provider를 추가/활성화하지 않았다.
- 공용 CharacterController가 PreEntry 로컬 Default-layer BoxCollider와 충돌하지 않으면, Collision Matrix 또는 Common XR CharacterController 설정을 확인해 달라.
- `ProjectSettings/Packages/com.unity.probuilder/Settings.json`이 이 작업 중 dirty로 표시되었다. content 변경은 없고 CRLF 경고만 있다. PreEntry는 이 파일을 stage/commit하지 않는다.
- 기존 App 빈 셸 이슈(`Assets/2.YJH/Docs/PREENTRY_APP_INTEGRATION_REQUESTS.md` 1~5: Detector 셸, EquipmentTransferZone 셸, PreEntry XR Profile, Right Controller 경로)는 이번 작업에서 재현하지 못했으나 요청은 유지한다.

## 권장 병합·재검증 순서

1. 변경 파일이 PreEntry 허용 경로만인지 확인
2. PreEntry 단독 실행: 이동 경계, 진입 전 누출음, 가방 존 Y 측정기, 패널 STOP
3. App Full 모드에서 P-102 패널 행/정지 상태/`SharedIncidentState` 전달 확인
4. 공용 가방이 `StageBagAnchor`에 1개만 있는지 확인
5. 전체 App PlayMode Smoke
