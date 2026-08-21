# HCl 누출 대응 VR 작업 경계

이 문서는 App Shared XR 통합 구조를 보호하면서 PPE, PreEntry, LeakResponse 담당자가 자기 Stage 기능을 독립적으로 수정할 수 있게 하는 기준이다.

## 1. 작업 역할

작업 시작 전에 다음 중 하나를 선언한다.

- Integration
- PPE (`Assets/1.JSR/`)
- PreEntry (`Assets/2.YJH/`)
- LeakResponse (`Assets/3.SSH/`)

Integration 관리자는 전체 통합 호환성 작업을 위해 각 Stage의 실제 소유 영역을 수정할 수 있다. 이 경우에도 Stage 전용 로직은 해당 Stage 폴더에 둔다.

## 2. 공식 Git 브랜치와 통합 흐름

공식 브랜치 구조는 다음과 같다.

```text
main
  │
  └── develop
        ├── feature/ppe
        ├── feature/preentry
        ├── feature/leakresponse
        └── integration/<통합작업명>
```

- `main`은 출시, 제출, 시연이 가능한 최종 안정 브랜치다. 전체 검증이 끝난 `develop`만 반영한다.
- `develop`은 App Shared XR, 공용 인프라, 세 Stage를 합치고 검증하는 전체 개발 통합 브랜치다.
- 기존 `integration` 브랜치는 폐기하며 새 commit, push, merge, Pull Request에 사용하지 않는다.
- `feature/ppe`, `feature/preentry`, `feature/leakresponse`는 각 담당 Stage의 기능과 소유 파일을 작업하는 브랜치다.
- Integration 관리자의 공용 수정은 `integration/<통합작업명>`에서 진행하고 `develop`로 Pull Request한다.
- Stage feature branch는 항상 최신 `develop`에서 시작하며 `main`에서 직접 만들지 않는다.
- Stage feature branch는 `main`에 직접 merge하거나 Pull Request하지 않는다.

Pull Request 방향은 다음으로 고정한다.

```text
feature/ppe          ─┐
feature/preentry      ├──→ develop ──→ main
feature/leakresponse ─┘
integration/<작업명> ─┘
```

```text
base: develop  compare: feature/ppe
base: develop  compare: feature/preentry
base: develop  compare: feature/leakresponse
base: develop  compare: integration/<통합작업명>

전체 통합 검증 완료 후에만:
base: main     compare: develop
```

Stage 작업 중 `develop`이 변경되면 통합 직전에 최신 상태와 자신의 변경을 비교한다. App, Shared XR, Input, Equipment, SceneFlow, 공용 Prefab의 충돌은 Stage 담당자가 임의로 선택해 해결하지 않으며 Integration 관리자가 최종 판단한다.

## 3. Integration 전용 공용 영역

다음 기반은 Integration 관리자가 소유한다.

- `Assets/Scenes/01_App.unity`, `02_Title.unity`, `03_Start.unity`, `07_Complete.unity`
- `Assets/Scripts/App/`
- `Assets/Resources/App/`
- `Assets/Common/XR/`
- `Assets/Common/InteractionGhosts/`
- `Assets/Editor/AppIntegrationBuilder.cs`
- `Assets/Editor/AppPlayModeSmokeRunner.cs`
- `Assets/Editor/IntegrationSceneValidator.cs`
- App의 `Common XR Origin`, XR Main Camera, Controller Tracking
- 공용 Input Action 기반과 `SharedXRInputContract`
- 공용 Ray/Near-Far/Grab/Haptic 기반
- `SessionEquipmentInventory`의 데이터 API와 공용 장비 ID 계약
- `SharedIncidentState`, `SharedTrainingTelemetry` 공용 계약
- 세션/Stage 이력과 로컬 텔레메트리 저장 기반
- App의 Scene 전환, Stage 완료 Adapter, 공용 EventSystem
- `ProjectSettings/`, `Packages/`

Stage 담당자는 위 기반을 직접 수정하지 않는다. 공용 입력 종류나 공용 장비 API가 부족하면 Integration 관리자에게 계약 추가를 요청한다.

## 4. Shared XR 소유 원칙

통합 App 실행에서 플레이어 XR은 하나만 활성화한다.

```text
App / Common XR Origin
├─ Main Camera
├─ Left Controller
└─ Right Controller
```

- PPE, PreEntry, LeakResponse는 통합 실행에서 App의 동일한 XR Main Camera와 Controller Tracking을 사용한다.
- Stage에 남아 있는 XR Origin, Camera, Controller는 단독 개발 테스트용이다. 통합 실행에서는 비활성화된다.
- Stage는 Camera FOV, stereo projection, projection matrix를 임의로 변경하지 않는다.
- Stage별 시작 위치와 방향은 `StageXRRequirements`로 선언할 수 있다.
- Ray, Near/Far, UI, Grab, Haptic의 필요 여부도 Stage 요구사항으로 선언할 수 있다.
- 각 Stage 담당자는 자기 `Assets/<Stage>/Resources/SharedXR/` 프로필의 선언값을 수정할 수 있다. 프로필의 스크립트·스키마와 `Assets/Common/XR/` 런타임은 Integration 소유다.
- Controller Visual 보정은 Visual 계층에만 둔다. Tracking Transform이나 Ray Origin을 시각 보정값으로 회전시키지 않는다.
- 한 손의 활성 포인터는 선택된 방식 하나만 사용한다. Standard Ray와 Near/Far를 동시에 활성화하지 않는다.

## 5. 공용 입력과 Stage 입력의 경계

Integration은 물리 입력을 의미 없는 공용 신호로 제공한다.

- Left/Right Trigger
- Left/Right Grip
- Left/Right Primary Button
- Left/Right Secondary Button
- Thumbstick Axis/Click
- Select, Activate, UI Press
- Position, Rotation
- Haptic 요청

버튼이 실제로 무엇을 하는지는 활성 Stage가 결정한다.

```text
SharedXRInputContract
└─ Left Secondary Button(Y)
   ├─ PreEntry handler → 측정기 꺼내기
   └─ LeakResponse handler → 현재 단계의 Sock/Pad 꺼내기
```

- App 공용 코드에 `if PreEntry`, `if LeakResponse` 방식으로 Stage 게임 동작을 넣지 않는다.
- Stage handler는 `OnEnable/OnDisable` 또는 생명주기에 맞춰 이벤트를 정확히 구독/해제한다.
- 비활성 Stage handler가 같은 입력에 반응하면 안 된다.
- 일반적인 버튼 동작 추가는 Stage 담당자가 자기 영역에서 수행할 수 있다.

## 6. Session Inventory와 Stage별 가방

공용 계층은 장비 ID와 수량만 보관한다. 가방 Visual과 상호작용은 각 Stage가 소유한다.

- PPE는 자기 가방의 수납 Zone과 `Store(itemId)` 호출을 관리한다.
- PreEntry는 `PreEntry_ResponseBag`과 측정기 `TryCheckOut/ReturnCheckedOut`을 관리한다.
- LeakResponse는 `LeakResponse_ResponseBag`과 Sock/Pad/Tongs `TryCheckOut/ConsumeCheckedOut`을 관리한다.
- 가방 Root를 이동·회전·스케일하면 Visual, Retrieval Zone, Retrieval Anchor가 함께 움직인다.
- App은 Stage 가방 Transform을 이동하거나 크기를 덮어쓰지 않는다.
- Inventory에 GameObject, Transform, Collider, Stage 이름, 진행 단계를 저장하지 않는다.

단독 Scene은 Inventory Source가 `None`일 때만 개발용 장비를 시드한다. PPE 결과나 FieldReady preset이 있으면 시드는 기존 상태를 덮어쓰지 않는다. 상세 API는 `docs/integration/SESSION_INVENTORY_CONTRACT.md`를 따른다.

공용 설비 상태와 이벤트 수집 원칙:

- 1차 교육 시나리오의 누출 설비는 P-102로 고정하며 교육생 설비 선택 기능을 만들지 않는다.
- PreEntry는 `SharedIncidentState`에 P-102 정지 결과를 기록하고 LeakResponse는 누출 격리 상태만 기록한다.
- Scene Object, Valve, VFX, AudioSource 참조는 각 Stage의 로컬 binding에 둔다.
- Stage는 `SharedTrainingTelemetry.Current`에 큰 단계 진입/완료, 콘텐츠 확인, 장비 사용 이벤트만 전달한다.
- Stage는 DB, HTTP, 파일 저장을 직접 구현하지 않는다. App 저장 실패는 gameplay를 중단시키지 않는다.

## 7. PPE 허용 범위

- Scene: `Assets/Scenes/04_PPE.unity`
- 로직/UI/자막: `Assets/1.JSR/Scripts/`
- Prefab/Resource/Audio/Art: `Assets/1.JSR/`의 해당 하위 폴더

PPE 담당자가 관리할 수 있는 예:

- PPE 장비 착용 순서와 상호작용
- PPE 전용 Trigger/Grip/A/B/X/Y 동작
- PPE UI, TTS, 자막
- 대응가방 착용 완료를 기존 PPE 상태에 반영하는 Stage 로직

보호 계약:

- `PPESequenceManager.OnPPESequenceCompleted`
- App의 `StageLifecycleAdapter` 구독 및 Scene 전환
- 공용 XR/Camera/Input과 Session Inventory API

## 8. PreEntry 허용 범위

- Scene: `Assets/Scenes/05_PreEntry.unity`
- 로직/UI/자막: `Assets/2.YJH/Scripts/Scenario/`
- Prefab/Data/Audio/Art/UI: `Assets/2.YJH/`의 해당 하위 폴더

PreEntry 담당자가 관리할 수 있는 예:

- 왼손 가방 접근 판정
- Left Y로 기존 HCl 측정기 꺼내기
- 측정기 Grab/Attach/측정/보관
- PreEntry 전용 입력, UI, TTS, 진행 상태
- 설비/펜스 Collider 범위 안의 자유 이동과 회전

통합·단독 실행 모두 `PreEntry_BagRetrievalZone`을 사용한다. App 공용 가방 Collider로 fallback하지 않는다.

보호 계약:

- `EntrySafetyStateMachine.SectionCompleted`
- App의 완료 Adapter와 Scene 전환
- 공용 XR/Camera/Input과 Session Inventory API

## 9. LeakResponse 허용 범위

- Scene: `Assets/Scenes/06_LeakResponse.unity`
- 누출 대응 로직: `Assets/3.SSH/Scripts/LeakResponse/`, `Assets/3.SSH/Scripts/LeakResponseLogic/`
- 밸브/설치 로직: `Assets/3.SSH/Scripts/ValveSystem/`, `Assets/3.SSH/Scripts/ObjectInstallation/`
- TTS/Resource/Material/Effect: `Assets/3.SSH/`의 해당 하위 폴더

LeakResponse 담당자가 관리할 수 있는 예:

- 왼손 가방 접근 판정
- Left Y로 현재 진행 단계에 맞는 기존 Sock 또는 Pad 꺼내기
- Sock/Pad Grab, 설치, 흡수, 회수 진행
- LeakResponse 전용 Grip/Trigger/버튼 동작
- LeakResponse UI, TTS, 자막

Sock/Pad 회수는 기존 `INT_AbsorbentSock_Straight_01`, `INT_AbsorbentPad_01` 인스턴스를 사용한다. 동일 장비를 새로 복제하거나 공용 가방 코드에 진행 단계를 넣지 않는다.

보호 계약:

- `P102LeakNarrationCue.ContainerClosedTaskCompleted`
- App의 완료 Adapter와 Scene 전환
- 공용 XR/Camera/Input과 Session Inventory API

## 10. Unity Scene과 파일 소유권

Unity `.unity` Scene은 충돌 해결이 어렵기 때문에 다음 소유권을 지킨다.

```text
Assets/Scenes/04_PPE.unity → feature/ppe
Assets/Scenes/05_PreEntry.unity       → feature/preentry
Assets/Scenes/06_LeakResponse.unity      → feature/leakresponse
Assets/Scenes/01_App.unity, 02_Title.unity, 03_Start.unity, 07_Complete.unity
                                      → develop의 Integration 관리자
```

- 자기 Stage가 아닌 Scene을 임의로 수정하지 않는다.
- 여러 사람이 같은 Scene이나 Prefab을 동시에 수정하지 않는다.
- 한 Stage에서만 사용하는 Script, Prefab, Resource는 해당 Stage가 소유한다.
- 여러 Stage가 함께 참조하는 Prefab, Script, ScriptableObject, 설정은 Integration/Common 소유로 취급한다.
- Stage 기능은 공용 XR을 사용하더라도 해당 Stage 폴더에 유지한다. PPE X/Grip, PreEntry Y 측정기, LeakResponse Y Sock/Pad 동작을 App 공용 코드로 옮기지 않는다.
- 공용 변경이 필요하면 요구사항을 Integration 관리자에게 전달하고 Integration 작업으로 처리한다.

## 11. 공통 금지 사항

- Stage에 새 XR Origin, XR Main Camera, EventSystem, InputActionManager 추가
- Stage에서 `SceneManager.LoadScene`, `LoadSceneAsync`, `UnloadSceneAsync`, `SetActiveScene` 호출
- Stage 완료 이벤트의 이름, 형식, 발생 조건 변경
- Controller Tracking Transform 또는 Ray Origin을 Visual 보정 목적으로 회전
- 공용 가방에 Stage 이름/아이템 선택 로직 하드코딩
- 다른 Stage 소유 파일을 일반 Stage 작업으로 수정
- `git add .`, `git add -A`
- Stage feature branch에서 `main`으로 직접 Pull Request 또는 merge
- Stage 담당자가 `main`, `develop`, 기존 `integration`에 직접 Push
- 기존 `integration` 브랜치에 새 작업 생성

## 12. 검증 체크리스트

Integration 검증:

1. 활성 XR Origin 1개
2. 활성 XR Main Camera 1개 및 Stage 전환 전후 동일 인스턴스
3. 활성 EventSystem, XR Interaction Manager, Input Action Manager 각 1개
4. Stage별 projection/FOV override 없음
5. 양손 포인터 중복 없음
6. App 공용 가방 Visual 비활성, 활성 Stage의 로컬 가방만 표시
7. PPE → PreEntry → LeakResponse에서 Session Inventory 상태 유지
8. PreEntry Y는 측정기만, LeakResponse Y는 해당 단계의 Sock/Pad만 처리
9. 비활성 Stage handler의 중복 반응 없음
10. 기존 TTS, 행동 순서, 완료 이벤트, Scene 전환 흐름 유지

커밋 전에는 담당 범위의 파일만 경로를 명시하여 stage하고, 보호 파일이나 다른 Stage의 예상치 못한 변경이 있으면 commit하지 말고 Integration 관리자에게 보고한다.
