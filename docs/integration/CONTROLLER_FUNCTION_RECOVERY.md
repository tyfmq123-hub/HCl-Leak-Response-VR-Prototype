# Shared XR Controller 기능 매핑

이 문서는 App Shared XR 통합 후에도 각 Stage가 기존 입력 의미와 진행 로직을 소유하도록 하는 현재 연결을 기록한다.

## 공용 기반

- App의 `Common XR Origin`만 Tracking, Main Camera, InputActionManager, XR Interaction Manager를 소유한다.
- `SharedXRInputContract`는 Trigger, Grip, X/Y/A/B, Thumbstick, Select, Activate, UI Press와 Pose를 물리 입력으로만 제공한다.
- Stage XR Origin과 Controller는 통합 실행에서 비활성화되며 비교용 reference로만 남긴다.
- Stage 전환 중 기존 InputActionManager가 같은 XRI ActionAsset을 비활성화해도 공용 계약이 기존 액션을 다시 활성화한다. 새 ActionAsset은 만들지 않는다.
- 활성 `XRRayInteractor`와 `NearFarInteractor`의 Select는 각 손의 `XRI Left/Right Interaction/Select`와 `Select Value`를 사용한다. 두 액션의 Quest 바인딩은 Grip이다.
- Activate는 각 손의 `Activate`와 `Activate Value`, UI Press는 `UI Press`와 `UI Press Value`를 사용한다.

## PPE

PPE의 기존 상호작용은 물리 오브젝트를 자유 이동시키는 `XRGrabInteractable` 방식이 아니라 `XRSimpleInteractable`을 Ray로 가리킨 뒤 Grip으로 선택·착용하는 방식이다. 이 진행 방식을 유지한다.

| 입력 | Stage 기능 | 소유 코드 |
|---|---|---|
| Grip / XRI Select | 첫 입력은 현재 Ray hover PPE 선택·미리보기, 다음 입력은 착용 또는 준비 확정 | `XRInteractionConfirmRouter`, `PPESelectionManager`, `PPEOutlineHighlight` |
| Left X | PPE 체크리스트 열기/닫기 | `PPEChecklistPanel` |
| Right B | 현재 PPE 선택 취소 | `PPESelectionManager` |
| Left Y | 가방 내부 장비 표시 토글 | `ResponseEquipmentManager` |
| Right A | 모든 PPE 절차 완료 후 다음 Stage 진행 | App 완료 Adapter + PPE 완료 상태 |
| Trigger / UI Press | World Space UI 선택 | 공용 Ray + PPE UI |
| Thumbstick | PPE Stage locomotion profile | App 공용 locomotion + Stage profile |

PPE Requirements는 Standard Ray, UI, Grab, Haptic을 요구하고 Near/Far pointer는 요구하지 않는다.

## PreEntry

| 입력 | Stage 기능 | 소유 코드 |
|---|---|---|
| Left Y + 공용 가방 근접 | 기존 HCl 측정기를 왼손 Visual에 장착 | `EntrySafetyInputAdapter`, `EntrySafetyEquipmentTransferZone`, `EntrySafetyScenarioController`, `EntrySafetyHclDetectorController` |
| Left Trigger | 현재 측정 단계에서 HCl 측정 hold | `EntrySafetyInputAdapter`, `EntrySafetyScenarioController` |
| Right B | 측정 완료 후 측정기 보관 | `EntrySafetyInputAdapter`, `EntrySafetyScenarioController` |
| Left X | 현재 안내 반복 | `EntrySafetyInputAdapter`, `EntrySafetyScenarioController` |
| Right A | 허용된 단계에서 PT 구역 진행 | `EntrySafetyInputAdapter`, `EntrySafetyScenarioController` |
| Right Trigger | PT 단계의 펌프 정지 | `EntrySafetyInputAdapter`, `EntrySafetyScenarioController` |

PreEntry는 현재 Scripted attach와 Camera gaze를 사용하므로 별도 Ray/Near/Far/Grab interactor를 활성화하지 않는다. Left Y의 의미는 PreEntry에만 있다.

## LeakResponse

| 입력 | Stage 기능 | 소유 코드 |
|---|---|---|
| Left Y + 공용 가방 근접 | 현재 완료된 TTS/진행 단계에 맞는 기존 Sock 또는 Pad 회수 | `P102SharedBagItemHandler` |
| Grip / XRI Select | Valve, Sock, Pad, 용기 뚜껑, 집게 등 현재 구현된 XR Grab/Select | 각 LeakResponse interaction component |
| Trigger / XRI Activate | 활성화 가능한 도구 기능 및 UI Press | 각 LeakResponse interaction component |
| Near/Far pointer | 근거리 접촉과 원거리 선택을 하나의 손별 pointer로 제공 | App 공용 NearFarInteractor + LeakResponse Requirements |
| Thumbstick | LeakResponse locomotion profile | App 공용 locomotion + Stage profile |

LeakResponse Requirements는 Near와 Far casting을 모두 요구한다. `P102SharedBagItemHandler`는 기존 `INT_AbsorbentSock_Straight_01`, `INT_AbsorbentPad_01` 인스턴스만 회수하며 복제하지 않는다.

## Lifecycle 기준

- PPE Scene unload 시 `XRInteractionConfirmRouter`와 PPE 입력 구독이 해제된다.
- `EntrySafetyInputAdapter`는 enable/disable에 맞춰 공용 입력을 구독/해제하고 Scenario의 자체 이벤트는 destroy 시 해제한다.
- `P102SharedBagItemHandler`는 enable/disable에 맞춰 공용 입력을 구독/해제하고 narration/install group 이벤트는 재구성 또는 destroy 시 해제한다.
- 동일 Stage를 다시 로드해도 이전 Stage handler가 공용 입력에 남아 있으면 안 된다.

## 검증 기준

- 한 손당 활성 pointer는 하나다.
- PPE는 Standard Ray 두 개, PreEntry는 pointer 없음, LeakResponse는 Near/Far 두 개다.
- 모든 활성 input interactor의 Select/Activate action과 value action이 enabled 상태다.
- LeakResponse Near/Far의 내부 Near Casting과 Far Casting이 Requirements와 일치한다.
- X/Y/A/B 버튼 이벤트와 XRI Select/Activate는 서로 다른 경로로 따로 검증한다.
