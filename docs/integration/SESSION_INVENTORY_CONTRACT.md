# Session Inventory 병렬 작업 계약

## 목적

PPE, PreEntry, LeakResponse가 서로의 feature 브랜치 완료를 기다리지 않고 작업한다.
Inventory는 장비 데이터만 유지하며 가방 Transform, Collider, Prefab, Stage 진행을 소유하지 않는다.

## 공용 API

공용 구현은 `Assets/Common/XR/SessionEquipmentInventory.cs`다.

```csharp
using HClTraining.SharedXR;

var inventory = SessionEquipmentInventory.Ensure();
inventory.BeginPpeBagCapture();
inventory.Store(SharedEquipmentIds.GasDetector);
inventory.TryCheckOut(SharedEquipmentIds.GasDetector);
inventory.ReturnCheckedOut(SharedEquipmentIds.GasDetector);
inventory.ConsumeCheckedOut(SharedEquipmentIds.AbsorbentSock);
```

- `BeginPpeBagCapture`: PPE의 실제 가방 수납 단계 시작 시 한 번 호출한다.
- `Store`: PPE에서 장비를 가방에 넣었을 때 호출한다.
- `TryCheckOut`: Stage 가방에서 장비를 꺼내기 직전에 호출한다.
- `ReturnCheckedOut`: 장비를 다시 보관하거나 작업을 취소할 때 호출한다.
- `ConsumeCheckedOut`: 흡착재 설치처럼 장비 사용이 확정됐을 때 호출한다.
- `Contains`: 현재 가방에 보관된 장비가 있는지 확인한다.

## 통합 실행

1. 새 전체 교육 시작 시 Inventory를 비운다.
2. PPE는 수납 단계 시작 시 `BeginPpeBagCapture`, Grip 수납 성공마다 `Store`를 호출한다.
3. PPE 완료 시 현재 `PPELoadoutData` 호환 브리지도 기존 기본 장비 목록을 전달한다.
4. PreEntry는 `GasDetector`를 체크아웃하고 보관 단계에서 반환한다.
5. LeakResponse는 Sock/Pad를 체크아웃하고 설치 완료 시 소비한다.

실제 GameObject는 Scene 사이를 이동하지 않는다. 각 Stage가 자기 Scene의 기존 장비 인스턴스나 Prefab을 표시한다.

## 단독 Scene 테스트

Inventory의 `Source`가 `None`일 때만 개발용 목록을 한 번 시드한다.

- PreEntry: `GasDetector`
- LeakResponse: `AbsorbentSock`, `AbsorbentPad`, `RecoveryTongs`

이미 PPE 결과나 FieldReady preset이 있으면 단독 테스트 시드는 아무것도 덮어쓰지 않는다.
따라서 같은 Stage 코드를 단독 실행과 App 통합 실행에 모두 사용한다.

## Stage별 가방 소유

```text
04_PPE
└─ ResponseBag_HardShell (PPE 담당자가 이름과 계층을 정리 가능)

05_PreEntry
└─ PreEntry_ResponseBag
   ├─ ResponseBag_HardShell_Visual
   ├─ PreEntry_BagRetrievalZone
   └─ PreEntry_ItemRetrievalAnchor

06_LeakResponse
└─ LeakResponse_ResponseBag
   ├─ ResponseBag_HardShell_Visual
   ├─ LeakResponse_BagRetrievalZone
   └─ LeakResponse_ItemRetrievalAnchor
```

- 위치·회전·전체 크기: 가방 Root Transform 수정
- 모델 크기만 변경: `ResponseBag_HardShell_Visual` 수정
- 손 접근 범위: 각 `BagRetrievalZone`의 Collider 수정
- 장비가 나타날 위치: 각 `ItemRetrievalAnchor` 수정

App은 위 Transform을 이동하거나 스케일을 덮어쓰면 안 된다.

## 병렬 작업 순서

1. 각 담당자는 같은 `develop`의 공용 API를 받는다.
2. PPE는 수납, PreEntry는 체크아웃/반환, LeakResponse는 체크아웃/소비를 동시에 작업한다.
3. 각 feature 브랜치는 자기 Stage 단독 Scene에서 개발용 시드로 검증한다.
4. Integration은 세 브랜치를 합친 후 PPE 결과가 다음 Stage에서 우선되는지만 검증한다.

## 금지

- Inventory에 GameObject, Transform, Collider, Stage 이름 저장
- PreEntry/LeakResponse 단독 시드로 기존 PPE 결과 덮어쓰기
- App 코드에 `if (PreEntry)`, `if (LeakResponse)` 방식으로 장비 선택 구현
- Stage 가방 위치를 App에서 런타임 강제 보정
