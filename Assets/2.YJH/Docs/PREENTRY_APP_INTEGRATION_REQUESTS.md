# PreEntry → Integration 요청 목록 (App 실행 시 깨지는 항목)

작성 역할: **PreEntry 담당자**  
기준 문서: `AGENTS.md`, `docs/integration/STAGE_EDITING_BOUNDARIES.md`  
증상 요약: `05_PreEntry` 단독 Play는 동작하는데, **App 씬부터** PreEntry Stage로 들어오면 장비 꺼내기·감지·XR 연동이 깨지거나 달라짐.

PreEntry 쪽에서 가능한 런타임 보정(존 분리, live XR resolve, detector reparent 등)은 `Assets/2.YJH`에 반영함.  
아래는 **PreEntry가 수정할 수 없는 공용/App 영역**이라 Integration 작업이 필요함.

---

## 1. Common XR에 비어 있는 HCl Detector 셸

- 경로: `Assets/Scenes/01_App.unity`
- 오브젝트: `Common XR Origin/Camera Offset/Left Controller/Visual/JH_HClDetector_Prototype`
- 증상: Transform만 있고 mesh/view 컴포넌트가 없음. App rebind가 이 빈 셸을 가리키면 장비 장착 시 모델/디스플레이가 안 보임.
- 요청:
  - JH Prefab `Assets/2.YJH/Prefabs/Goal01_WindAndHCl/JH_HClDetector_Prototype.prefab` 내용을 Common XR 쪽으로 제대로 머지하거나
  - 빈 셸을 제거하고, Stage 쪽 실물 인스턴스를 쓰도록 빌더 정책을 수정

## 2. Common XR의 EntrySafetyEquipmentTransferZone 셸 불완전

- 경로: `Assets/Scenes/01_App.unity`
- 오브젝트: `Common XR Origin/EntrySafetyEquipmentTransferZone`
- 증상: Transform만 있고 `EntrySafetyEquipmentTransferZone` 컴포넌트/콜라이더 없음, `m_IsActive: 0`.
- 요청:
  - 불필요하면 셸 삭제
  - 유지하려면 JH Scene과 동일한 컴포넌트·콜라이더·참조로 재구성

## 3. PreEntry Stage XR Profile 갱신

- 경로: `Assets/Resources/App/StageXRProfiles/PreEntry.asset` (**Integration 전용**)
- 증상/불일치:
  - `spawnPosition`이 구버전 `(0, 0.84, -18.76)` (JH XR Origin은 `(0.407, 0.84, -16.27)`)
  - `EntrySafetyEquipmentTransferZone` / `Right Controller` componentStates가 현재 JH 계층과 어긋남
  - Detector componentStates가 Common XR의 빈 셸을 전제로 함
- 요청: JH `05_PreEntry`의 현재 XR Origin / 장비 구성에 맞게 profile rebuild (`AppIntegrationBuilder` 등)

## 4. Right Controller 경로 불일치 + 이중 오브젝트

- App: 실제 컨트롤러는 `Common XR Origin/Camera Offset/Right Controller`
- JH 스테이지: `XR Origin (VR)/Right Controller` (Camera Offset 형제가 될 수 있음)
- App에 추가로 `Common XR Origin/Right Controller` 빈/비활성 셸 존재
- 증상: `StageXRProfileController` relative-path rebind 실패 → Goal2 오른손/STOP 존/pose fallback이 죽은 Transform을 참조
- 요청:
  - Common XR의 잘못된 `Right Controller` 셸 정리
  - rebind 경로를 PPE/Common 기준으로 통일 (또는 빌더가 JH 경로를 Camera Offset 하위로 정규화)

## 5. AppIntegrationBuilder 머지 정책

- 경로: `Assets/Editor/AppIntegrationBuilder.cs`
- 증상: XR merge가 EntrySafety 전용 컴포넌트를 건너뛰면서 **Transform-only inactive 셸**을 남김
- 요청:
  - 게임플레이 오브젝트(Detector, EquipmentTransferZone 등)는 빈 셸로 만들지 말 것
  - Prefab 실체를 넣거나 Stage Scene 인스턴스만 쓰도록 정책 명확화

## 6. (참고) Scene 공동 로드

- PreEntry 단독으로는 `06_LeakResponse`를 Additive로 올리지 않음 (규칙: Stage에서 `SceneManager` 전환 금지)
- App에서 PreEntry와 LeakResponse 환경을 같이 띄워야 하면 **Integration의 `SceneFlowController`** 쪽에서 처리 필요

---

## PreEntry가 이미 보정한 항목 (참고)

| 항목 | 위치 |
|------|------|
| EquipmentTransferZone을 XR Origin 밖으로 분리 | `05_PreEntry.unity` |
| 왼손/가방 콜라이더 live resolve + local BoxCollider fallback | `EntrySafetyEquipmentTransferZone.cs` |
| Detector를 live Left Controller/Visual로 reparent | `EntrySafetyHclDetectorController.cs` |
| Main Camera live resolve | `EntrySafetyCameraGazeAdapter.cs` |
| Goal2 XR Origin/Head/RightHand live resolve | `EntrySafetyGoal2PtPanelController.cs` |
| Untracked pose fallback live resolve | `EntrySafetyUntrackedControllerPoseFallback.cs` |
| `_stateMachine` null guard | `EntrySafetyScenarioController.cs` |
| 공용 resolve 헬퍼 | `EntrySafetyAppXrResolve.cs` |

이 보정으로도 1~5가 남아 있으면 App 쪽 공용 XR/Profile 재구성이 필요함.
