# LeakResponse → Integration 인수인계

## 2026-09-07 Integration — PreEntry 대기 중 뚜껑 79cm 낙하 수정

- 실제 원인: `06_LeakResponse` 뚜껑 Rigidbody가 gravity=true/kinematic=false인 상태로 PreEntry의 additive 환경에 먼저 로드됨. Leak 활성화까지 중력으로 떨어지고, 그 뒤 `P102WasteContainerRuntime.Configure`가 바닥 위치를 닫힘 pose로 저장했다.
- Scene은 해당 Rigidbody 두 필드만 gravity=false/kinematic=true로 변경. 뚜껑/용기/ghost의 작성 transform은 보존.
- 신규 `P102WasteContainerPhysics`를 bootstrap Awake에서 호출해 Stage 비활성 상태에도 즉시 물리 보호. 열린 입구를 유지하는 16개 몸체 벽+바닥 collider 생성. Configure 재호출은 중복 생성하지 않는다. 기존 작성 solid collider는 보존한다.
- 변경 코드: `P102LeakRuntimeBootstrap.cs`, `P102WasteContainerRuntime.cs`, 신규 helper/단위 검사/`IntegrationWasteLidLifecycleValidation`(+meta).
- 수정 전 0.78975m 낙하 재현 FAIL → 수정 후 6초/FixedUpdate 300회 및 handoff 뒤 오차 0m PASS. Edit Mode 167/167, App smoke 33개, 기존 뚜껑·집게 XRI 회귀 PASS. 최종 C# 오류 0.
- 수동 작업: Play 중지→컴파일→App 재실행. 위치 조정/setup 재생성 불필요. Quest 손 동작 전체 재검수는 미완료. 상세 파일/증거/알려진 제한: [낙하 원인·수정 보고서](../../integration/WASTE_LID_DORMANT_PHYSICS_2026-09-07.md). 추가 Integration 요청 없음.

## 2026-09-07 Integration — Game View/씬 전환 후속 정정

- 빈 Game View를 만드는 위험이 있는 `XRGameViewFullFrame` 별도 덮어쓰기와 shader를 삭제하고 Unity 기본 XR mirror로 복구. 전체 눈 시야 확장 기능은 철회했다.
- App `SceneFlowController`와 새 `SceneLoadingBudget`에서 전환 작업 분산/Player 로딩 예산 제한과 복구를 적용. Stage Scene/Prefab/동작/TTS/작성 pose는 변경하지 않았다.
- Edit Mode 162/162, App smoke 33개, 뚜껑·집게·물방울·카운트 회귀 PASS. 실제 Quest mirror/시스템 모래시계 소멸은 미검증. 잠깐의 전환 fade와 초기 활성화의 긴 프레임은 남을 수 있다.
- 수동 작업: Play 중지→컴파일 완료→App 재실행만 필요. setup/Scene 재생성 불필요.
- 파일·테스트·알려진 문제·검증 제한은 [후속 보고서](../../integration/XR_MIRROR_TRANSITIONS_2026-09-07.md) 참고. 추가 Integration 요청 없음.

## 2026-09-07 Integration — 집게 유지 / 뚜껑 / 물방울 / 카운트 방향

- `P102CarryInputLock` + `P102RecoveryTongsRuntime`: 잡은 집게의 Select를 유지하고 Trigger는 그대로 흡착재 작업에 사용한다. 마지막 반납 안내 완료 후 고스트 30cm 안에서 새 Grip 입력만 반납한다. 반납 고스트는 안내 시작에 표시한다.
- `P102WasteContainerRuntime`: 사용자 작성 닫힘 Pose 보존, 잡기 취소/놓치기 때 복원, 실제 release pose로 고스트 판정. `P102RuntimeUtility`의 복제 ghost는 원본 world scale 유지.
- `HClAbsorptionVisualController`: 0으로 숨겼던 물방울 emission을 재연결 때 원래 값으로 잘못 저장하지 않도록 수정. App runtime에서 rate=7/살아 있는 물방울 확인.
- `P102FlangeGazeRuntime`: 고정 위치를 유지하며 실제 App camera 쪽으로 countdown 방향 갱신. Unity 렌더에서 정방향 `3초` 확인.
- `P102LeakNarrationController`와 canonical MP3 26개: 실제 재생 종료/정지 상태 사용, 단계별 Grip/Trigger 및 반납 안내 정리. 집게/뚜껑의 kinematic 속도 설정 경고도 제거한다.
- `06_LeakResponse.unity`/Prefab/뚜껑·고스트 작성 위치는 변경하지 않았다. setup menu 재실행 불필요.
- Edit Mode 160/160 통과, App 로딩/실제 XRI 선택·해제/버튼 반복/반납/뚜껑 복원 검사 및 렌더 확인. Cue 전이는 테스트에서 주입했으며 Quest 실제 착용 검증과 구분한다.
- 전체 파일·최종 검증 결과·알려진 제한: [Integration 보고서](../../integration/VR_REGRESSION_2026-09-07.md). 추가 Integration 요청 없음.

## 2026-09-04 Integration correction — 폐기물 수거용기 뚜껑 닫힘 높이

- `06_LeakResponse`의 실제 메시를 Unity에서 측정한 결과, 노란 용기 테두리 최고점은 월드 Y `0.9574m`, 검은 뚜껑판 최저점은 `0.8926m`로 뚜껑판이 테두리 안쪽에 약 `6.5cm` 파고들어 노란 테두리가 검은 뚜껑 위로 보였다.
- `INT_WasteContainer_01/HazWasteContainer_Lid`의 Scene 작성 로컬 Y만 `0.903 → 0.975`로 올렸다. 이 값에서 검은 뚜껑판 하단과 노란 용기 테두리 상단이 월드 Y `0.9574m`로 맞닿는다. 기존 X/Z 위치, `-90° X` 회전, 용기 본체, 측면 보관 Ghost, Grab·완료 흐름은 변경하지 않았다.
- 런타임은 기존대로 Scene 작성 닫힘 Pose를 그대로 캐시하므로 초기 표시, 놓친 Grab 복귀, 최종 닫힘이 모두 같은 수정 높이를 사용한다.
- Unity 재측정 및 렌더 캡처 기준 검은 뚜껑판 하단과 노란 테두리 상단은 모두 월드 Y `0.9574m`로 일치한다. 관련 EditMode 회귀 검사 `1/1` 통과. Quest 실기기에서는 검은 뚜껑 하부가 노란 입구에 바로 놓이는지 최종 시각 확인이 필요하다.

## 2026-09-04 Integration correction — Interaction별 오입력 요약

- 오입력은 활성 `InteractionPrompted`부터 `InteractionCompleted` 사이에서만 수집한다. Gaze/Automatic/Movement 단계, Pause·비활성 시간, 스틱 축·스틱 클릭·메뉴·UI 입력은 제외한다.
- 각 완료 이벤트에 요구 손·요구 control·Quest 버튼명, 첫/마지막 오입력, 총 물리 오입력 횟수, 서로 다른 오입력 목록, 반복 횟수와 입력 패턴을 함께 저장한다. Primary/Secondary는 손에 따라 A/X와 B/Y로 구분한다.
- 같은 Interaction에서 같은 잘못된 손·키를 반복해도 `InteractionAttempted`는 한 번만 발행한다. 빠르게 여러 버튼을 누른 경우 `RapidMultiButtonInput`과 `Low` 신뢰도로 표시해 한 사용자의 연타가 전체 키 통계를 지배하지 않게 했다.
- 성공 입력이 명시되지 않은 완료 콜백에서 마지막 오입력을 성공 키로 재사용하지 않는다. 마지막 관측 입력이 요구 손·키와 일치할 때만 사용하고, 아니면 요구 입력으로 완료를 기록한다.
- 회귀 검사 4/4 통과: 기존 v1 JSON/Batch, 아날로그 눌림 중복 제거, A/B/X/Y 문맥 요약·스틱 제외, 마지막 오입력의 성공 키 오인 방지. Scene/Prefab/훈련 진행은 변경하지 않았다.

## 2026-09-04 Integration correction — 입력 통계 중복 방지

- `SharedXRInputContract`의 Grip/Trigger 값이 한 번 누른 상태에서 미세하게 변하며 `performed`를 여러 번 발생시켜, 같은 물리 입력이 잘못된 입력 여러 건으로 부풀려지던 문제를 수정했다.
- `TrainingTelemetryRuntimeMonitor`가 버튼을 `performed -> canceled` 한 주기로 추적한다. 스틱 축과 스틱 클릭은 서비스에서 오입력 수집 대상에서 제외하며, 훈련 Pause와 앱 Pause에서는 눌림 상태를 안전하게 초기화한다.
- 새 입력 회귀 검사들을 포함한 집중 검사 4/4 통과. 임시 테스트 실행기는 제거했으며 최종 Unity 도메인 리로드의 C# 컴파일 오류는 0개다.
- 변경 파일: `Assets/Scripts/App/TrainingTelemetryService.cs`, `Assets/Scripts/App/TrainingTelemetryRuntimeMonitor.cs`, `Assets/Editor/Tests/AppFlowStateTests.cs`, 세 Stage 인수인계 문서. Scene/Prefab/LeakResponse 동작은 변경하지 않았다.
- 이미 서버에 저장된 과거 이벤트는 변경하거나 삭제하지 않으며, 요구 키별 한 줄 집계는 서버 대시보드에서 별도로 처리한다.

## 2026-09-03 Integration correction — 시간 집계, 뚜껑 Pose, Ghost Hand, TTS

- 최신 로컬 JSONL 세션에서 LeakResponse 실제 경과 시간은 약 `224초`였지만 `ApplicationFocus=false` 이후에도 XR 상호작용이 계속된 약 `190초`를 비활성 시간으로 잘못 뺀 사실을 확인했다. 앞으로 Desktop mirror/Unity Editor 포커스 손실은 상태 이벤트로만 기록하며 플레이 시간을 멈추지 않는다. 실제 앱 Pause와 Quest user presence, 명시적 훈련 일시정지는 계속 비활성 시간으로 처리한다.
- 사용자가 `06_LeakResponse`에서 직접 맞춘 실제 뚜껑과 측면 Ghost의 위치·회전을 런타임 초기화가 덮어쓰지 않도록 side-Ghost 회전 정규화를 제거했다. 이번 수정은 `06_LeakResponse.unity`를 저장하거나 변경하지 않았으며 파일 시각 `2026-09-03 17:47:48`을 그대로 유지했다.
- 집게와 뚜껑의 `VRHandPoseTool_Ghosts` 렌더러는 해당 Grab 단계가 열릴 때 표시되고, 실제 선택 중·정상 안착·완료 때 숨겨진다. 놓친 경우에는 다시 표시되어 재시도를 안내한다. 제거된 Hand Pose Tool 컴포넌트에는 다시 의존하지 않는다.
- 양손 밸브 `Grip`, 흡착삭·패드의 가방 배출/잡기 `Grip`, 뚜껑 `Grip`, 집게 `Grip`, 흡착재 회수 `Trigger`, 집게 반납을 위한 Toggle `Grip`을 각 행동 TTS에 명시했다. 변경된 LeakResponse MP3 12개를 재생성했다.
- 관련 회귀 검사 4/4 통과: 포커스 손실의 플레이 시간 유지, PreEntry 왼손 Grip 입력 연결, 집게 단계 Ghost Hand 표시, 뚜껑 단계 Ghost Hand 표시와 씬 작성 Pose 보존. Unity 6000.4.8f1 최종 도메인 리로드에서 C# 컴파일 오류 0개.
- 변경 파일: `TrainingTelemetryRuntimeMonitor.cs`, `P102RuntimeUtility.cs`, `P102RecoveryTongsRuntime.cs`, `P102WasteContainerRuntime.cs`, `P102RuntimeRegressionTests.cs`, `AppFlowStateTests.cs`, LeakResponse MP3 12개, TTS 대본 문서.
- 기존 대시보드에 이미 저장된 50초 기록은 소급 수정되지 않는다. 수정 후 새 세션부터 정상 집계되는지 Quest 전체 과정으로 확인해야 한다.
- 커밋·푸시는 하지 않았으며 현재 Integration 브랜치는 `integration-windsock-pivot`이다.

## 작업 식별

- 브랜치: `feature/leakresponse`
- commit: 미커밋 작업 트리 (`d445493` 위 변경)
- 기준 develop commit: `d445493`
- 작성자/작성일: Codex / 2026-08-21

## 이번 변경 목적과 동작

- 흡입·토출 밸브는 서로 다른 왼손과 오른손이 동시에 Grab한 동안만 회전한다. 한 손만 잡거나 한 손을 놓으면 즉시 회전 입력이 0이 된다.
- 기존 밸브의 열림·닫힘 회전 범위와 `ValveWheelClosureController`의 완전 닫힘 판정·래치는 유지했다.
- LeakResponse 가방은 사용자가 옮긴 외형의 월드 위치·크기를 유지하면서 `LeakResponse_ResponseBag` 루트를 외형 위치로 정규화했다. Grip 감지 Zone과 배출 Anchor가 이후 가방 이동을 함께 따라간다.
- 가방 Zone 안에서 왼손 또는 오른손 Grip을 누르면 현재 단계에서 허용된 흡착재가 가방 입구 기준 아래쪽에서 위쪽으로 수직 제시된다.
- `INT_AbsorbentSock_Straight_01`, `INT_AbsorbentPad_01`은 단순 전시물이 아니라 실제 회수 원본이므로 삭제하지 않았다. 시작 시 비활성 상태를 유지하고 가방 루트의 숨겨진 원본으로 편입했다.
- 사용자가 옮긴 `INT_WasteContainer_01`의 현재 Transform과 렌더 Bounds를 런타임 기준 위치·투입 Zone·닫힌 뚜껑 Pose로 사용한다. 측면 뚜껑 Ghost도 초기 월드 Pose를 보존한 채 용기 자식으로 연결되어 이후 용기 이동을 따라간다.
- 기존 LeakResponse 자막 HUD 생성, CSV 로드, WAV 로드·재생·대기 로직을 제거했다. 1~26 진행 Cue 이벤트 계약은 유지하되 시작·완료 이벤트를 즉시 연속 전달하므로 미디어가 없어도 단계가 멈추지 않는다.
- 기존 TTS WAV 26개와 두 위치의 자막 CSV 리소스 및 Scene AudioSource를 삭제했다.
- TTS 콘텐츠 조회 텔레메트리는 제거하고 단계·장비 텔레메트리만 유지했다.
- 제공받은 6개 MP3를 `LR_*` 영문 파일명으로 정리하고 LeakResponse 전용 3D 환경음으로 연결했다.
  - `LR_Flange_Mist_Loop`: `LeakPoint` 플랜지 미스트
  - `LR_Flange_Droplets_Loop`: `FallingDrops` 물방울
  - `LR_P102_Pump_Loop`: `Pump_102` 펌프
  - `LR_Fence_PumpArea_Ambience_Loop`: P-102가 아닌 현장 Fence
  - `LR_Distant_Alarm_01_Loop`, `LR_Distant_Alarm_02_Loop`: 서로 떨어진 Fence 두 지점
- 미스트·물방울은 가까울수록 선명하고 멀어질수록 감쇠하지만 최대 거리에서도 볼륨이 0이 되지 않는다. 따라서 LeakResponse가 PreEntry 의존 씬으로 Additive 로드된 동안에도 누출 방향을 들을 수 있다.
- 펌프·Fence·경보음은 이후 TTS를 방해하지 않도록 낮은 기본 볼륨으로 설정했다. 향후 TTS 재생기는 `SetNarrationDucked(true/false)`로 전체 환경음을 추가 감쇠·복구할 수 있다.
- 음량 재조정: 미스트 최대 볼륨은 최종 `0.06`, 물방울은 요청대로 `0.09 → 0.135`(1.5배)로 변경했다.
- 밸브는 여전히 서로 다른 좌·우 두 손이 동시에 Grab되어야만 회전한다. 다만 두 컨트롤러의 추적 갱신이 같은 프레임에 들어와야 했던 과도한 조건을 제거해, 두 손을 잡은 상태라면 한쪽의 유효 회전 추적 프레임도 반영한다.
- 밸브 Grab 표면 허용 거리는 손/컨트롤러 Attach 오차를 고려해 `0.08m → 0.15m`, 최소 회전 추적값은 `0.05° → 0.02°`로 조정했다. 손 구분은 Interactor 자체뿐 아니라 부모 계층의 Left/Right 이름까지 확인한다.
- 밸브 선택 시 컨트롤러 Tracking Origin과 림 표면 거리를 중복 검사하던 필터를 제거했다. XRI의 실제 림 Collider Hover 판정은 유지하므로 손이 림에 닿으면 Grab할 수 있다.
- LeakResponse 활성화 시 두 밸브의 `XRGrabInteractable`, `ValveWheelGrabTransformer`, `ValveWheelClosureController`를 명시적으로 활성화한다. TTS/Cue 대기는 없으며 Stage 활성화 직후 양손 Grab이 가능하다.
- 실기기 로그에서 좌·우 Grip과 XRI 선택 조건은 모두 정상인데 `interactableEnabled=False`인 상태를 확인했다. TTS 제거로 `LeakSceneArrival` Cue가 즉시 완료되면서, 2프레임 뒤 구독하는 `StageLifecycleAdapter`가 완료 이벤트를 놓치고 상호작용 잠금을 계속 유지한 것이 원인이었다.
- LeakResponse Flow 시작을 Lifecycle 바인딩보다 한 프레임 뒤인 3프레임 후로 미뤄 Arrival 시작·완료 이벤트가 Integration에 전달되게 했다. 추가 대기 절차가 아니라 약 0.05초의 초기화 순서 보정이며, 완료 이벤트 직후 밸브 Grab 잠금이 해제된다.
- `FallingDrops` 위치에 대각선 아래 방향의 런타임 미스트 파티클을 추가했다. 토출 밸브 누출 강도와 연결되어 밸브를 닫을수록 방출량이 줄고 누출이 0이면 정지한다.
- Quest 빌드에서 런타임 `Shader.Find` 결과가 누락되어 미스트가 보이지 않을 수 있어, Resources에 포함되는 URP Particle Unlit 재질과 런타임 소프트 입자 텍스처를 사용하도록 변경했다. 입자 크기·알파·방출량도 VR에서 식별 가능한 수준으로 보강했다.
- 폐기물 용기 뚜껑은 시작 시 코드로 높이를 재계산하지 않고 Scene에 저장된 위치·회전을 그대로 닫힘 Pose로 사용한다. 따라서 Stage 시작 때 뚜껑이 위로 솟지 않는다.
- 가방 장비 배출은 `Grip 누름`에 대기 상태만 기록하고 `Grip 해제` 때 한 번 실행한다. 흡착삭·패드는 가방 위로 올라온 뒤 Kinematic 상태로 정지하며, 같은 Grip에 자동으로 잡혔다가 떨어지지 않고 사용자가 다음 Grip으로 직접 잡는다.
- 폐기물 뚜껑 Grab은 제거된 Hand Pose Tool의 멀리 떨어진 Attach를 더 이상 사용하지 않는다. 실제 뚜껑 손잡이를 컨트롤러 기준점에 맞추고 안정된 컨트롤러 부모를 따라가므로 손에서 멀리 붙거나 Thumbstick Attach 이동에 끌려가지 않는다.
- 플레이어·집게가 용기 안에 갇히던 원인이었던 런타임 바닥·4면 Solid Collider 생성을 제거했다. 오염물 투입 판정은 기존 렌더 Bounds 기반 Intake Zone을 그대로 사용한다.
- Scene의 폐기물 용기 전체, 측면 뚜껑 Ghost, 집게 Ghost 크기를 기존의 90%로 줄였다. 용기 루트 축소로 실제 몸체와 실제 뚜껑은 함께 같은 비율로 줄어든다.
- 기존에는 `Pump_102`의 일부 직계 자식만 Collider 보강 대상이라 펜스·배관·펌프·둑을 걸어서 통과할 수 있었다. LeakResponse Scene의 활성 정적 Mesh를 순회해 로컬 Mesh Bounds 기반 Solid BoxCollider를 보강한다. 기존 Collider, Rigidbody, Grab, 손/컨트롤러, 고스트, 흡착재, 가방, 집게, 폐기물 용기는 자동 제외한다.
- 네 개의 흡착삭 Ghost Renderer Bounds를 합친 `P102_SockContainment_PlayerBoundary_Runtime`을 생성한다. 이 Solid 영역은 CharacterController만 차단하고 Rigidbody/Grab/Interactor Collider와는 `Physics.IgnoreCollision`을 유지하므로 플레이어는 걸어서 진입할 수 없지만 집게와 집게가 든 흡착재는 들어갈 수 있다.
- LeakResponse Stage가 비활성화되면 이번에 추가한 환경 Collider와 흡착삭 Player 전용 영역도 비활성화하고, Stage 재활성화 때 다시 켠다. Additive로 남은 Scene이 다른 Stage 이동을 막지 않는다.

## 변경 파일

- `Assets/Scenes/06_LeakResponse.unity`
- `Assets/3.SSH/Scripts/ValveSystem/ValveWheelGrabTransformer.cs`
- `Assets/3.SSH/Editor/ValveSystem/ValveWheelSceneConfigurator.cs`
- `Assets/3.SSH/Tests/Editor/ValveWheelClosureControllerTests.cs`
- `Assets/3.SSH/Scripts/LeakResponse/Narration/P102LeakNarrationController.cs`
- `Assets/3.SSH/Scripts/LeakResponse/Narration/P102LeakRuntimeBootstrap.cs`
- `Assets/3.SSH/Scripts/LeakResponse/Narration/LeakResponseFacilityBinding.cs`
- `Assets/3.SSH/Scripts/LeakResponse/Narration/P102SharedBagItemHandler.cs`
- `Assets/3.SSH/Scripts/LeakResponse/Narration/P102WasteContainerRuntime.cs`
- `Assets/3.SSH/Scripts/LeakResponse/Narration/P102FlangeGazeRuntime.cs`
- `Assets/3.SSH/Scripts/LeakResponse/Narration/LeakResponseTelemetryReporter.cs`
- `Assets/3.SSH/Scripts/LeakResponse/Narration/LeakResponseSpatialLeakAudio.cs`
- 추가: `Assets/3.SSH/Scripts/LeakResponse/LeakResponseDiagonalMistRuntime.cs`와 `.meta`
- 추가: `Assets/3.SSH/Resources/LeakVFX/M_DiagonalMist.mat`과 폴더·파일 `.meta`
- `Assets/3.SSH/Editor/LeakResponseIntegrationTests.cs`
- 추가: `Assets/3.SSH/Resources/LeakAudio/`의 MP3 6개와 `.meta`
- `Assets/3.SSH/Editor/P102PretendardFontAssetGenerator.cs`
- `Assets/3.SSH/Editor/P102RuntimeRegressionTests.cs`
- 삭제: `Assets/3.SSH/Resources/LeakTTS/`, `Assets/3.SSH/Resources/P102/Unity_TTS_Map.csv`, `Assets/3.SSH/Data/P102LeakSubtitles/`
- `docs/handoffs/leakresponse/LATEST.md`

## Scene 변경과 편집 기준

- 가방 외형을 다시 옮길 때 외형 자식만 움직이지 말고 `LeakResponse_ResponseBag` 루트를 옮긴다. 그래야 외형·Grip Zone·수직 배출 Anchor·숨겨진 원본이 함께 움직인다.
- 폐기물 용기는 `INT_WasteContainer_01` 루트를 옮긴다. 런타임이 그 위치에서 용기 Bounds와 닫힘 Pose를 다시 계산한다.
- 숨겨진 흡착삭·패드 원본은 삭제하거나 활성화하지 않는다. Ghost 배치 대상과는 별개이며 가방 배출에 필요하다.
- Scene에 사용자가 미리 옮긴 폐기물 용기·뚜껑 Ghost Transform은 보존했다.

## 공용 계약 사용

- 입력: `SharedXRInputContract`의 좌·우 `Grip`
- 장비: `SessionEquipmentInventory`, `SharedEquipmentIds`
- 상태: `SharedIncidentState`, `LeakState.Isolated`
- 텔레메트리: `SharedTrainingTelemetry.Current`의 단계·장비 이벤트
- App 완료 계약: `P102LeakNarrationCue.ContainerClosedTaskCompleted` 유지

## 검증

- Unity 6000.4.8f1 Roslyn 응답 파일 정적 컴파일: 성공
  - `VRProject.ValveSystem.Runtime.rsp`
  - `Assembly-CSharp.rsp`
  - `Assembly-CSharp-Editor.rsp`
  - `VRProject.ValveSystem.Tests.rsp`
- 새 컴파일 오류: 0개. 기존 obsolete/unused 경고만 존재한다.
- 음원 원본 6개와 프로젝트 복사본의 SHA-256 일치, 6개 배치 Anchor의 Scene 내 유일성 확인.
- 거리 감쇠 곡선의 최대 거리 비영(非零) 볼륨 하한 회귀 테스트 추가.
- 대각선 미스트 방출량이 누출 강도 0에서 정지하고 강도에 따라 증가하는 회귀 테스트 추가.
- 미스트의 Resources 재질 포함 여부와 폐기물 뚜껑 초기 위치·회전 보존 회귀 검증을 추가했다.
- 정적 환경 Mesh Collider 생성·Stage 비활성화 연동, 흡착삭 영역의 CharacterController 차단·집게 Rigidbody 충돌 무시 회귀 테스트를 추가했다.
- 양손 중 한쪽씩 추적되는 프레임의 회전, 양손 해제 시 정지, 반대 방향 입력 거부, 15cm Grab 허용 거리와 부모 계층 손 구분 회귀 테스트를 갱신했다.
- TTS/CSV 리소스명, 제거된 자막 API, `oneHandRotationGain: 0.55` 잔존 검색: 0개
- Unity Test Runner: 미실행
- App PlayMode·Quest 실기기 확인: 사용자 확인 대상, 미검증

## 알려진 문제와 위험

- 양손 Grab의 실제 손 판별, 가방 Grip Zone 체감 범위, 수직 제시 높이, 이동된 폐기물 용기의 뚜껑 스냅은 Quest에서 최종 확인이 필요하다.
- 밸브 잠금 해제와 `FallingDrops` 대각선 미스트의 방향·농도는 Quest에서 최종 확인이 필요하다.
- 6개 환경음의 실기기 체감 음량·방향성과 MP3 반복 경계는 Quest에서 최종 확인이 필요하다.
- 자동 환경 Collider는 12cm 이상인 활성 정적 Mesh만 대상으로 한다. 작은 볼트·장식은 제외되며 펜스·배관·펌프·둑·바닥과 같은 보행 차단 대상의 실제 체감 범위는 Quest에서 최종 확인이 필요하다.
- 작업 시작 전부터 수정되어 있던 PPE/PreEntry 폰트 자산과 `Pretendard-SemiBold SDF.asset`은 이번 작업에서 수정·복원하지 않았다.

## Integration 요청

- 기존 문서나 Smoke가 LeakResponse의 `Left Y`, 26개 TTS/자막 존재, 미디어 재생 대기를 전제로 하면 새 계약인 `좌·우 Grip + 즉시 Flow Cue`로 갱신한다.
- 새 TTS를 연결할 때 LeakResponse의 `LeakResponseSpatialLeakAudio.SetNarrationDucked(true/false)`를 TTS 시작·종료에 호출한다.
- 장기적으로는 `StageLifecycleAdapter.BindStage`가 구독 시점에 `LeakSceneArrival`의 기존 완료 여부도 확인하면 초기화 프레임 순서에 더 강해진다. 현재 feature 브랜치에서는 Integration 소유 파일을 건드리지 않고 LeakResponse Flow를 3프레임 지연해 계약을 맞췄다.
- 병합 후 App 전환에서 LeakResponse 진입, 양손 밸브, 가방 배출, 흡착재 설치·회수, 이동된 폐기물 용기 닫힘과 완료 이벤트를 확인한다.

## 권장 재검증 순서

1. LeakResponse 진입 직후 실제 흡착삭·패드 원본이 보이지 않는지 확인
2. 각 밸브가 한 손에서는 정지하고 서로 다른 양손 Grab에서만 제한 범위 내 회전하는지 확인
3. 양쪽 손으로 가방 위 Grip을 눌렀을 때 장비가 위로 제시되는지 확인
4. 흡착삭·패드 설치와 집게 회수가 순서대로 진행되는지 확인
5. 옮긴 폐기물 용기에서 투입 Zone, 측면 뚜껑 보관, 최종 닫힘·완료가 작동하는지 확인
6. 전 과정에서 기존 자막·TTS가 나오지 않고 진행 대기가 발생하지 않는지 확인
7. PreEntry에서 누출 방향의 미스트·물방울과 먼 경보가 작게 들리고, LeakResponse 접근 시 공간 방향성이 자연스러운지 확인
8. 펜스·펌프·배관·둑을 걸어서 통과할 수 없는지, 흡착삭 구역은 몸만 막히고 집게는 안쪽으로 들어가는지 확인

## 2026-08-28 Integration correction — merged waste-container lid and local bag only

- The imported waste-container lid is now authored with local identity rotation instead of the obsolete `-90° X` override that made it stand vertically beside the bin after merge.
- At runtime the lid is normalized flat, centered over `HazWasteContainer_Body`, and placed from the body-only renderer top before the closed pose, closed ghost, Grab collider, and snap targets are cached. Reconfiguration is deterministic and does not accumulate height.
- The PPE body-following response bag is disabled at its source. LeakResponse therefore shows only the existing `LeakResponse_ResponseBag` local scene bag; its current Grip-release presentation behavior remains unchanged.
- Changed files for this correction: `P102WasteContainerRuntime.cs` and `06_LeakResponse.unity`; the wearable-source correction is documented in the PPE handoff.
- Validation: current Assembly-CSharp Roslyn compile completed with 0 errors; `06_LeakResponse.unity` has 0 duplicate YAML document IDs. Quest lid resting height, handle Grab, side stow, final close, and absence of a second following bag remain for the scene owner to verify.

## 2026-09-02 Integration correction — dormant valve Grip diagnostics

- PreEntry 실행 중 Additive로 대기하는 LeakResponse의 흡입·토출 밸브 진단기 두 개가 모든 전역 Grip을 각각 기록해 한 번의 입력이 두 번 들어온 것처럼 보였다.
- 비활성 `XRGrabInteractable`에서는 Grip 진단을 실행하지 않으며, `06_LeakResponse`에 저장된 두 진단기의 `logGripAttempts`도 비활성화했다.
- 변경 파일: `Assets/3.SSH/Scripts/ValveSystem/ValveGrabSelectionDiagnostics.cs`, `Assets/Scenes/06_LeakResponse.unity`.
