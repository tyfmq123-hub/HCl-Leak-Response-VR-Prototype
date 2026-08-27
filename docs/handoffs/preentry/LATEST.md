# PreEntry → Integration 인수인계

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
- 통합 Full에서 PPE가 `GasDetector`를 수납하지 않으면 꺼내기가 실패한다. PreEntry는 자막 안내만 하고 Inventory/PPE를 고치지 않는다.
- 작업 중 `ProjectSettings/Packages/com.unity.probuilder/Settings.json`과 폰트 자산이 git status에 dirty로 보일 수 있다. PreEntry가 내용을 수정하지 않았고 restore/reset하지 않았다.

## Integration 요청

- App 공용 가방을 PreEntry Scene에서 수정할 필요는 없다. `05_PreEntry`의 `PreEntry_ResponseBag` / `ResponseBag_HardShell_Visual` / `PreEntry_BagRetrievalZone` / `PreEntry_ItemRetrievalAnchor`가 PreEntry 소유다.
- 단독 Scene 자유 이동이 필요하면 Stage XR Origin의 기존 ContinuousMoveProvider 활성화를 Integration이 공용 이동 정책과 맞춰 달라. PreEntry는 이동 Provider를 추가/활성화하지 않았다.
- 공용 CharacterController가 PreEntry 로컬 Default-layer BoxCollider와 충돌하지 않으면, Collision Matrix 또는 Common XR CharacterController 설정을 확인해 달라. 북쪽 `NorthWall`이 추가되었으므로 같은 확인이 필요하다.
- Full 모드에서 PPE가 `GasDetector`를 반드시 `Store`하는지 확인해 달라. PreEntry는 실패 자막만 보여 준다.
- `ProjectSettings/Packages/com.unity.probuilder/Settings.json`과 TMP 폰트 바이너리는 이 작업에서 stage/commit하지 않는다.
- 기존 App 빈 셸 이슈(`Assets/2.YJH/Docs/PREENTRY_APP_INTEGRATION_REQUESTS.md` 1~5: Detector 셸, EquipmentTransferZone 셸, PreEntry XR Profile, Right Controller 경로)는 이번 작업에서 재현하지 못했으나 요청은 유지한다.

## 권장 병합·재검증 순서

1. 변경 파일이 PreEntry 허용 경로만인지 확인
2. PreEntry 단독 실행: 이동 경계(북쪽 포함), PT_Panel을 몸으로 뚫지 못함, STOP 손은 그대로, 진입 전 누출음, 가방 존 Y 측정기 실패 안내, 패널 도착 3초, STOP, 감압 후 Barricade/overlay 시각 해제, A 이동
3. App Full 모드에서 측정기 응시 측정, P-102 패널 행/정지 상태/`SharedIncidentState` 전달 확인
4. `PreEntry_ResponseBag`이 지정 위치에 있고 App 가방과 중복되지 않는지 확인
5. 전체 App PlayMode Smoke
