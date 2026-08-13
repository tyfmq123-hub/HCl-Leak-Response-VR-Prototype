# PPE Loadout — 현장/출입전 검증 씬 공식 연동 스펙

이 문서를 출입전 검증·현장 씬 담당에게 전달하세요.  
`PPELoadoutData.cs`만으로는 부족하므로 **아래 파일·표·프리셋·Source 규칙**을 함께 사용합니다.

---

## 제공 파일 (이 레포)

| 파일 | 역할 |
|------|------|
| `Assets/1.JSR/Scripts/PPELoadoutData.cs` | DontDestroyOnLoad + `PPELoadoutSource` |
| `Assets/1.JSR/Scripts/PPELoadoutReader.cs` | null-safe 조회 API |
| `Assets/1.JSR/Scripts/PPELoadoutSceneBootstrap.cs` | **출입전 검증 씬 전용** 자동 적용 |
| `Assets/1.JSR/Scripts/PPELoadoutPresets.cs` | 공식 스냅샷 / Clear·Replace·Commit |
| `Assets/1.JSR/Scripts/PPELoadoutResultCommit.cs` | PPE 룸 완료/확정 버튼용 1회 커밋 |
| `Assets/1.JSR/Scripts/PPELoadout_Integration_Prompt.md` | 본 문서 |

**금지:** PPE 룸 씬에 `PPELoadoutSceneBootstrap` 추가.

---

## 세 상태 (`PPELoadoutSource`)

| Source | 의미 |
|--------|------|
| `None` | 현재 훈련에서 PPE 결과가 전혀 기록되지 않음 |
| `PpeSceneResult` | PPE 결과가 기록됨 (장비 일부/전체 누락 가능) |
| `FieldReadyPreset` | PPE를 거치지 않은 현장 단독 시작 공식 프리셋 |

판정 API (목록 개수 / singleton / `sequenceComplete`로 판단하지 말 것):

```csharp
PPELoadoutReader.HasRecordedPpeResult  // Source == PpeSceneResult
PPELoadoutReader.HasResolvedLoadout    // Source != None
PPELoadoutReader.Source
```

`Source == PpeSceneResult`이면 Equipped/Prepared가 비어 있어도 **실결과**다. 프리셋으로 덮지 않는다.

---

## 진행 모드

### A) 전체 훈련 (PPE 룸 → 출입전 검증)
1. PPE 룸에서 장착/준비 진행 (`SyncFromScene`은 draft일 뿐, Source를 확정하지 않음)
2. 사용자가 **완료/결과 확정** 시 한 번만:
   - `PPELoadoutResultCommit.CommitResult()` 또는
   - `PPELoadoutPresets.CommitPpeSceneResult()` / `PPELoadoutData.CommitPpeSceneResultFromManagers()`
   - 시퀀스 전부 완료 시 `PPESequenceManager`도 동일 Commit 1회 호출
3. `ReplaceWithSnapshot`은 **장비마다 반복 호출 금지** (호출 전 Clear됨)
4. Single `LoadScene`으로 출입전 검증 씬 이동 — **Clear 금지**
5. 검증 씬 `PPELoadoutSceneBootstrap`이 `Source=PpeSceneResult`를 그대로 읽음

### B) 현장/검증 파트 단독 시작 (모듈 선택)
```csharp
PPELoadoutPresets.BeginFieldStandaloneWithOfficialPreset();
// Source = FieldReadyPreset
```

### C) 새 전체 훈련 / PPE 파트 새로 시작 (사용자 명시 선택 시에만)
```csharp
PPELoadoutPresets.BeginNewFullTrainingClear();
// Source = None, 장비 Clear
```

---

## Equipped vs Prepared

| ItemId | 저장 |
|--------|------|
| `InnerGloves` | **Equipped** |
| `ChemicalSuit` | **Equipped** |
| `ChemicalSafetyBoots` | **Equipped** |
| `FaceRespirator` | **Equipped** |
| `ResponseBag` | **Equipped** |
| `ChemicalFaceShield` | **Equipped** |
| `ChemicalGloves` | **Equipped** |
| `WasteContainer` | **Prepared** |

### 개별 ItemId로 저장하지 않음

| ItemId | 처리 |
|--------|------|
| `GasDetector` | `bagContentsChecked=true`일 때 준비된 것으로 처리 |
| `AbsorbentSock` | 동일 |
| `AbsorbentPad` | 동일 |
| 집게 | **ItemId 신설 금지**. `WasteContainer` Prepared를 그대로 따름 (`HasTongs()`) |

---

## 공식 프리셋 `FieldReadyComplete`

- Source: `FieldReadyPreset`
- Equipped: 위 7종 전부
- Prepared: `WasteContainer`
- `sequenceComplete = true`, `bagContentsChecked = true`

---

## 출입전 검증 씬 최소 구현

1. `PPELoadoutSceneBootstrap` 1회만 배치 (`Missing Loadout Mode = LeaveEmpty` 권장; 현장 단독은 모듈 선택에서 미리 `BeginFieldStandaloneWithOfficialPreset()`)
2. 조회는 `PPELoadoutReader`만 사용
3. Source 기반으로만 “결과 없음 / PPE 결과 / 프리셋”을 구분
4. 모듈 선택 현장 단독이면 씬 로드 전 `BeginFieldStandaloneWithOfficialPreset()` 호출

---

## 금지

1. 싱글턴 존재 / 빈 목록 / `sequenceComplete`로 PPE 결과 유무 판단
2. `PpeSceneResult`(빈 목록 포함)를 프리셋으로 덮어쓰기
3. PPE 완료 후 스테이지 이동 시 Clear
4. `GasDetector` 등 가방 내용물을 Equipped로 가정
5. 집게 ItemId 신설
6. PPE 룸에 Bootstrap 배치
7. 장비마다 `ApplySnapshot`/`ReplaceWithSnapshot` 반복 호출

---

## Agent 한 줄 요약

> Source로 세 상태를 구분한다. PPE는 Commit 한 번으로 `PpeSceneResult`를 남기고, 검증 씬은 Source≠None이면 절대 프리셋으로 덮지 않는다. 측정기는 `bagContentsChecked`, 집게는 `WasteContainer` Prepared를 따른다.
