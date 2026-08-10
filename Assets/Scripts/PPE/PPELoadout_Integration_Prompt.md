# PPE Loadout — 현장 씬 공식 연동 스펙

이 문서를 현장(누출 대응) 씬 담당에게 전달하세요.  
`PPELoadoutData.cs`만으로는 부족하므로 **아래 파일·표·프리셋을 함께** 사용합니다.

---

## 제공 파일 (이 레포)

| 파일 | 역할 |
|------|------|
| `Assets/Scripts/PPE/PPELoadoutData.cs` | DontDestroyOnLoad 영속 상태 |
| `Assets/Scripts/PPE/PPELoadoutReader.cs` | null-safe 조회 API |
| `Assets/Scripts/PPE/PPELoadoutSceneBootstrap.cs` | 현장 씬 자동 적용 |
| `Assets/Scripts/PPE/PPELoadoutPresets.cs` | 공식 스냅샷 / Clear·Replace |
| `Assets/Scripts/PPE/PPELoadout_Integration_Prompt.md` | 본 문서 |

---

## 진행 모드 (필수)

### A) 전체 훈련 (PPE 룸 → 현장)
1. PPE 룸에서 실제 장착/준비가 `PPELoadoutData`에 자동 동기화됨
2. `LoadScene` 후 DDOL 데이터가 유지됨
3. 현장 씬 `PPELoadoutSceneBootstrap`이 **그 실데이터를 읽음**
4. **씬 전환 시 Clear 금지**

### B) 현장 파트 단독 시작 (모듈 선택)
1. **빈 `PPELoadoutData`를 만들지 말 것**
2. 모듈 선택에서 공식 프리셋 적용:

```csharp
PPELoadoutPresets.BeginFieldStandaloneWithOfficialPreset();
// 내부: Ensure() → ApplySnapshot(FieldReadyComplete) → 기존 상태 Clear 후 교체
```

3. Bootstrap Inspector: `Missing Loadout Mode = Apply Field Ready Preset`

### C) 새 훈련 시작
이전 DDOL이 남지 않도록:

```csharp
// 전체 훈련(PPE 룸부터)
PPELoadoutPresets.BeginNewFullTrainingClear();

// 또는 현장 단독으로 새 시작
PPELoadoutPresets.BeginFieldStandaloneWithOfficialPreset(); // Clear + 교체
```

`ApplySnapshot` / `ReplaceLoadout`은 **항상 먼저 Clear** 합니다.

---

## Equipped vs Prepared — 공식 표

| 의미 | ItemId (문자열 정확 일치) | PPE 룸 저장 | 조회 |
|------|---------------------------|-------------|------|
| 속장갑 (1차) | `InnerGloves` | **Equipped** | `IsEquipped` |
| 내산성 화학보호복 | `ChemicalSuit` | **Equipped** | `IsEquipped` |
| 내화학 안전장화 | `ChemicalSafetyBoots` | **Equipped** | `IsEquipped` |
| 전면형 호흡보호구 | `FaceRespirator` | **Equipped** | `IsEquipped` |
| 대응가방 | `ResponseBag` | **Equipped** (`MirrorResponseBagEquipped`) | `IsEquipped` (방어적으로 `IsPrepared`도 OK) |
| 안면보호구 | `ChemicalFaceShield` | **Equipped** | `IsEquipped` |
| 외부 보호장갑 | `ChemicalGloves` | **Equipped** | `IsEquipped` |
| 유해폐기물 수거용기 | `WasteContainer` | **Prepared** (`SetPrepared`) | `IsPrepared` |

상수: `PPEEquipmentManager.Id*` / `PPESequenceManager.IdInnerGloves`

### 완료 플래그

| 플래그 | 의미 |
|--------|------|
| `sequenceComplete` / `HasCompletedPreparation()` | PPE 시퀀스 전부 완료 |
| `bagContentsChecked` / `BagContentsChecked()` | 가방 내용물 **한 번** 확인(Y) 완료 |

---

## ResponseBag 내용물 (측정기·흡착재·집게)

PPE 룸 `ResponseEquipmentManager.RequiredBagContentIds` 기준:

| 내용물 | ItemId | Equipped/Prepared에 저장? | BagContentsChecked |
|--------|--------|---------------------------|--------------------|
| 가스/농도 측정기 | `GasDetector` (`IdConcentrationMeter` 별칭) | **아니요** | 확인 시 포함 (3종 중 1) |
| 흡착재(소크) | `AbsorbentSock` | **아니요** | 확인 시 포함 |
| 흡착재(패드) | `AbsorbentPad` | **아니요** | 확인 시 포함 |
| 집게 | — | **존재하지 않음** | — |

중요:
- 측정기(`GasDetector`)는 **가방 내부 검사 ID**일 뿐, `PPELoadoutData.equippedItemIds` / `preparedItemIds`에 **들어가지 않음**
- Y로 내용물 한 번 확인하면 3종이 모두 inspected 처리되고 `bagContentsChecked = true`
- 현장 씬에서는 개별 측정기 Equipped가 아니라 **`BagContentsChecked()`** 로 “가방 점검 완료”를 판단할 것

---

## 공식 프리셋 `FieldReadyComplete`

`PPELoadoutPresets.FieldReadyComplete`:

**Equipped**
- `InnerGloves`
- `ChemicalSuit`
- `ChemicalSafetyBoots`
- `FaceRespirator`
- `ResponseBag`
- `ChemicalFaceShield`
- `ChemicalGloves`

**Prepared**
- `WasteContainer`

**Flags**
- `sequenceComplete = true`
- `bagContentsChecked = true`

(`GasDetector` / `AbsorbentSock` / `AbsorbentPad`는 리스트에 넣지 않음 — 플래그로만 표현)

---

## 현장 씬 최소 구현

1. 저장 시스템 새로 만들지 말 것
2. `PPELoadoutSceneBootstrap` 추가  
   - `Missing Loadout Mode = ApplyFieldReadyPreset`
3. 조회는 `PPELoadoutReader`만 사용
4. 모듈 선택 현장 단독 진입 시:

```csharp
PPELoadoutPresets.BeginFieldStandaloneWithOfficialPreset();
// then LoadScene(field)
```

5. 전체 훈련 새 시작:

```csharp
PPELoadoutPresets.BeginNewFullTrainingClear();
// then LoadScene(ppeRoom)
```

---

## 금지

1. 현장 단독에서 `PPELoadoutData.Ensure()`만 호출해 **빈 상태**로 진행
2. ItemId 임의 변경 (`ChemicalSuit` ≠ `Suit`)
3. `GasDetector`를 Equipped로 가정
4. 집게 ItemId 가정 (현재 PPE 룸 가방에 없음)
5. PPE 룸 grab/equip 로직을 현장으로 복사

---

## Agent 한 줄 요약

> 전체 진행은 PPE 룸이 쓴 `PPELoadoutData`를 읽고, 현장 단독은 `PPELoadoutPresets.FieldReadyComplete`로 Clear 후 교체한다. Equipped/Prepared 표를 지키고, 측정기는 ItemId 장비가 아니라 `bagContentsChecked`다. 집게는 없다.
