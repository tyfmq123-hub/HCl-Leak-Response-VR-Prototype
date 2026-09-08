# PPE Integration Handoff

## Work metadata

- Role: PPE
- Branch: `feature/ppe`
- Commit: InnerGloves XR hand tint on `feature/ppe`, cherry-picked to `develop`
- Push target: `origin/develop`
- Updated: 2026-09-08
- Current focus: InnerGloves equip feedback by tinting XR default-hand meshes.

## InnerGloves XR hand tint (2026-09-08)

속장갑을 한 손씩 착용하면 해당 손의 XR 기본 핸드 메시가 반투명 회색에서 `RightInnerGlovePhysical`과 같은 회청색으로 바뀐다. 선반의 속장갑 메시는 기존처럼 착용 후 숨기고, 트래킹·레이·공용 XR·외부 화학장갑 메쉬는 건드리지 않는다.

- 런타임에 `RightInnerGlovePhysical` Visual 머티리얼/알베도를 읽어 색을 맞춘다. 샘플이 실패하면 `PPE_glrove_basecolor` 평균색 `(0.378, 0.495, 0.492)`를 쓴다.
- 왼손/오른손을 따로 칠한다. 양쪽 착용 완료 후에만 바뀌지 않는다.
- 색은 `MaterialPropertyBlock`만 써서 공용 `HandsDefaultMaterial`을 수정하지 않는다.
- 대상은 `ChemicalGlovesWearableVisual`의 기본 손과 활성 XR Origin의 `PPE_DefaultHand_Left/Right`다. App additive와 `04_PPE` 단독 모두 같은 경로를 쓴다.
- PPE 룸 리셋(`ResetPair`)이면 원래 손 색으로 돌아간다. 이후 외부 화학장갑을 끼면 기존처럼 장갑 메쉬로 교체된다.
- `origin/develop` (`5770f85`)를 `feature/ppe`에 합쳤다. `04_PPE.unity` 충돌은 develop 씬을 사용했다. develop의 `Wall_Left`가 이미 막혀 있어, feature/ppe 전용 분할 벽 오브젝트는 가져오지 않았다.

### Files changed

- `Assets/1.JSR/Scripts/PPEInnerGloveHandTint.cs` (new)
- `Assets/1.JSR/Scripts/PPEGlovePairCoordinator.cs`
- `Assets/1.JSR/Scripts/ChemicalGlovesWearableVisual.cs`
- `Assets/1.JSR/Editor/PPEInnerGloveHandTintTests.cs` (new)
- this handoff

### Inspector / Scene manual work

없음. Play Mode에서 속장갑을 한 손씩 착용하면 손 색이 바뀌는지 확인하면 된다. Unity가 아직 `main`을 열고 있으면 `feature/ppe`로 씬을 다시 열어야 한다.

### Validation

- Edit Mode `PPEInnerGloveHandTintTests.ApplyToHand_TintsThenRestoresDefaultHandColor`를 추가했다. Unity Test Runner 실행은 이 환경에서 하지 못했다.
- 실제 Quest/Play Mode 착용 피드백은 미검증.
- Console 오류와 Missing Script는 코드 경로상 새 의존성 없음. 런타임 확인은 미검증.

### Known issues

- XR 손은 속장갑 텍스처를 입히지 않고 평균 회청색만 쓴다. 손가락 주름은 기본 손 메시를 유지한다.
- 외부 화학장갑 단계 이후에는 이 틴트가 보이지 않는다. 그때는 기존 화학장갑 메쉬가 손을 대체한다.

### Integration 요청

없음. 공용 XR, App Scene, Input Action 변경 없음.

## 2026-09-07 Integration — Game View/씬 전환 후속 정정

- 빈 Game View를 만드는 위험이 있는 `XRGameViewFullFrame` 별도 덮어쓰기와 shader를 삭제하고 Unity 기본 XR mirror로 복구. 전체 눈 시야 확장 기능은 철회했다.
- App `SceneFlowController`와 새 `SceneLoadingBudget`에서 전환 작업 분산/Player 로딩 예산 제한과 복구를 적용. Stage Scene/Prefab/동작/TTS/작성 pose는 변경하지 않았다.
- Edit Mode 162/162, App smoke 33개, 뚜껑·집게·물방울·카운트 회귀 PASS. 실제 Quest mirror/시스템 모래시계 소멸은 미검증. 잠깐의 전환 fade와 초기 활성화의 긴 프레임은 남을 수 있다.
- 수동 작업: Play 중지→컴파일 완료→App 재실행만 필요. setup/Scene 재생성 불필요.
- 파일·테스트·알려진 문제·검증 제한은 [후속 보고서](../../integration/XR_MIRROR_TRANSITIONS_2026-09-07.md) 참고. 추가 Integration 요청 없음.

## 2026-09-07 Integration — 대본/화이트보드/화질 회귀 수정

- `PPETTSManager`의 중복 재생 시간 대기를 제거하고 실제 `CueStarted`를 `Whiteboard/PPEProcessDiagramController`에 연결했다. 화이트보드 위험 위치를 보는 실제 음성이 끝난 뒤 준비 순서로 바뀐다.
- canonical PPE MP3 37개와 전체 대본을 현행 Grip 잡기/착용 위치에서 놓기, 3종 장비 수납 후 가방 Grip, 수거용기 바라보기+Grip 기준으로 검토/재생성했다. 과거 Y/B 안내로 되돌리지 않는다.
- `04_PPE.unity`/Prefab/네임태그 작성 위치는 변경하지 않았다. 공용 App UI 깊이 분리 및 camera clip/품질 수정은 Integration에서 관리한다. 수동 setup 실행 불필요.
- Edit Mode 160/160 통과(음성 83개 해시·대본·Unity 디코딩 포함). Quest 실제 착용 및 음성 청취 검수는 미검증. 서버 기능은 변경하지 않았다.
- 전체 변경 파일·검증 결과·알려진 제한·수동 확인: [Integration 보고서](../../integration/VR_REGRESSION_2026-09-07.md). 추가 Integration 요청 없음.

## 2026-09-04 Integration correction — Interaction별 오입력 요약

- 오입력은 활성 `InteractionPrompted`부터 `InteractionCompleted` 사이에서만 수집한다. Gaze/Automatic/Movement 단계, Pause·비활성 시간, 스틱 축·스틱 클릭·메뉴·UI 입력은 제외한다.
- 각 완료 이벤트에 요구 손·요구 control·Quest 버튼명, 첫/마지막 오입력, 총 물리 오입력 횟수, 서로 다른 오입력 목록, 반복 횟수와 입력 패턴을 함께 저장한다. Primary/Secondary는 손에 따라 A/X와 B/Y로 구분한다.
- 같은 Interaction에서 같은 잘못된 손·키를 반복해도 `InteractionAttempted`는 한 번만 발행한다. 빠르게 여러 버튼을 누른 경우 `RapidMultiButtonInput`과 `Low` 신뢰도로 표시해 한 사용자의 연타가 전체 키 통계를 지배하지 않게 했다.
- 성공 입력이 명시되지 않은 완료 콜백에서 마지막 오입력을 성공 키로 재사용하지 않는다. 마지막 관측 입력이 요구 손·키와 일치할 때만 사용하고, 아니면 요구 입력으로 완료를 기록한다.
- 회귀 검사 4/4 통과: 기존 v1 JSON/Batch, 아날로그 눌림 중복 제거, A/B/X/Y 문맥 요약·스틱 제외, 마지막 오입력의 성공 키 오인 방지. Scene/Prefab/훈련 진행은 변경하지 않았다.

## 2026-09-04 Integration correction — 입력 통계 중복 방지

- `SharedXRInputContract`의 Grip/Trigger 값이 한 번 누른 상태에서 미세하게 변하며 `performed`를 여러 번 발생시켜, 같은 물리 입력이 잘못된 입력 여러 건으로 부풀려지던 문제를 수정했다.
- `TrainingTelemetryRuntimeMonitor`가 버튼을 `performed -> canceled` 한 주기로 추적한다. 스틱 축과 스틱 클릭은 서비스에서 오입력 수집 대상에서 제외하며, 훈련 Pause와 앱 Pause에서는 눌림 상태를 안전하게 초기화한다.
- 로컬 JSONL로 확인한 `Trigger -> Grip 95건`, `SecondaryButton -> Grip 36건`, `PrimaryButton -> Grip 35건`의 대부분은 2026-09-03 17:35에 시작한 수정 전 단일 세션에서 발생했다. 이미 서버에 저장된 과거 이벤트는 변경하거나 삭제하지 않는다.
- 새 입력 회귀 검사들을 포함한 집중 검사 4/4 통과. 임시 테스트 실행기는 제거했으며 최종 Unity 도메인 리로드의 C# 컴파일 오류는 0개다.
- 변경 파일: `Assets/Scripts/App/TrainingTelemetryService.cs`, `Assets/Scripts/App/TrainingTelemetryRuntimeMonitor.cs`, `Assets/Editor/Tests/AppFlowStateTests.cs`, 세 Stage 인수인계 문서. Scene/Prefab/훈련 진행은 변경하지 않았다.
- 서버 대시보드에서 같은 요구 키를 한 행으로 합치는 작업은 서버 소스에서 별도로 처리해야 한다.

## 2026-09-03 Integration correction — interaction-button narration

- Every normal PPE wear prompt now states the actual two-step input: select the highlighted item with `Grip`, then press `Grip` again to equip it.
- Response-bag guidance now states the complete live sequence: select with `Grip`, inspect contents with left `Y`, then press `Grip` again to finish preparation.
- The Korean subtitle source and the matching runtime MP3 files were updated together. The respirator wording remains `전면형 호흡보호구` without the pronunciation-breaking space.
- Changed runtime narration: `PPETTSSubtitles.cs` and PPE clips `08`, `10`, `12`, `14`, `16`, `18`, `21` under `Assets/Resources/TrainingNarration/PPE`.
- Unity 6000.4.8f1 reimported all changed clips and completed the final domain reload with 0 current C# compiler errors. Quest audio timing and pronunciation remain the final device check.
- No commit or push was made. Integration branch remains `integration-windsock-pivot`.

## 2026-09-03 PPE presentation, narration, and telemetry correction

- The app-loaded PPE room now preserves the five authored `NameTag_*` objects. `PPEEquipmentWallLabels` waits for the additive scene to finish initializing and only generates fallback labels when the complete authored label set is genuinely absent.
- The preparation whiteboard order is now `Inner Gloves -> Chemical Suit -> Boots -> Respirator -> Face Shield -> Outer Gloves -> Response Bag -> Waste Container`. Each item receives a green completion check driven by `PPESequenceManager` state.
- The HCl leak-location preview remains visible for at least seven seconds and the preparation board for at least twelve seconds. The opening narration explicitly directs the trainee to inspect the right-side equipment rack.
- PPE step-completion narration now interrupts stale guidance immediately. The following step prompt remains queued behind that completion cue, preventing delayed speech and the response-bag-to-waste-container narration stall.
- Interrupted or user-ended sessions now emit a partial `StageEnded` event for the active PPE stage, using the pause-aware active duration. Interaction prompts also carry `previous_interaction_id` and pause-aware `transition_active_duration_ms`.
- Quest render scale was raised from `0.8` to `1.0`; the existing 4x MSAA remains enabled. This targets the blurred/shimmering edges visible in the PPE room.
- The local JSONL proves the reported dashboard row was not produced by the latest PPE run: the session is `PPEOnly`/`PPE`, while the local telemetry upload key is empty. The live v0.6 dashboard also calculates transition gaps from raw UTC timestamps, so it includes headset-focus loss unless the server is updated to consume `transition_active_duration_ms`.

### Files changed in this pass

- `Assets/1.JSR/Scripts/PPEEquipmentWallLabels.cs`
- `Assets/1.JSR/Scripts/PPESequenceManager.cs`
- `Assets/1.JSR/Scripts/Whiteboard/PPEProcessDiagramController.cs`
- `Assets/1.JSR/Scripts/PPETTSManager.cs`
- `Assets/1.JSR/Scripts/PPETTSSubtitles.cs`
- `Assets/Resources/TrainingNarration/PPE/02_WearEssentialPPE.mp3`
- `Assets/Resources/TrainingNarration/PPE/03_WearInOrder.mp3`
- `Assets/Scripts/App/TrainingTelemetryService.cs`
- `Assets/Settings/Mobile_RPAsset.asset`
- `Assets/Editor/IntegrationSceneValidator.cs`
- `Assets/Editor/Tests/AppFlowStateTests.cs`
- `docs/audio/FULL_COURSE_TTS_SCRIPT.md`
- this handoff

### Validation

- Unity 6000.4.8f1 Asset Pipeline compile: 0 current C# errors.
- Integration scene validation: all seven scenes, XR profiles, serialization, and Build Settings passed.
- New telemetry regression tests: 2/2 passed.
- Full existing EditMode suite: 48 passed, 5 failed. The five failures are pre-existing regression expectations in PPE hand-visual post-processing and LeakResponse/P102 tests; they are outside this scoped correction and were not hidden or deleted.
- Quest headset verification is still required for final label legibility, whiteboard check placement, narration timing, and shimmer reduction.
- No commit or push was made. Integration branch remains `integration-windsock-pivot`.

## Cabinet shelf release support (2026-08-28)

- `04_PPE` now stores nine solid shelf BoxColliders under `PPE_StorageRoot/Physics`: one full-width bottom support plus four left and four right compartment supports. The two shelf columns match the authored PPE slots while leaving the hanging-suit bay clear.
- The cabinet model's nested, scaled PPE transforms do not line up with the old fixed `1.7 m` cabinet width. At runtime, an additional solid `Shelf_Item_*_Collider` is therefore fitted directly below every authored `PPEStoredDisplayState` physical bound. Inner gloves and protective gloves no longer depend on guessed cabinet-column coordinates.
- The cabinet's full-size player blocker excludes the held/released PPE physics layer instead of disabling shelf collisions. Shelf colliders remain on Default and continue to collide with every released cabinet PPE object.
- When cabinet PPE is released near its shelf, its solid bounds are lifted out of any initial shelf penetration and all shelf/item collision pairs are restored.
- Final release policy: if the release does not successfully equip the PPE, it is restored to its authored cabinet pose immediately in the same release flow. Gravity is not enabled for that invalid release, so even a very small drop cannot pass through a shelf. This applies to every cabinet PPE object using `PPEStoredDisplayState`, including inner gloves, protective gloves, boots, respirator, and face shield. Successful equip releases are unchanged.

### Files

- `Assets/1.JSR/Scripts/PPECabinetPhysics.cs`
- `Assets/1.JSR/Scripts/PPEStoredDisplayState.cs`
- `Assets/Scenes/04_PPE.unity`
- this handoff

### Validation

- CODE VERIFIED: Unity C# manual compile completed with 0 errors
- SCENE VERIFIED: nine shelf BoxColliders are enabled, solid, and serialized; duplicate YAML document IDs: 0
- PLAY MODE VERIFIED: NOT TESTED
- QUEST VERIFIED: NOT TESTED

## Whiteboard inner gloves + packed bag final Grip (2026-08-28)

- Whiteboard item `01 속장갑` now uses the same flat, front-facing pair presentation as item `07 외부 보호장갑`; the other seven item poses are unchanged.
- After the detector, absorbent pad, and absorbent sock are packed, enabling the bag confirmation interaction now also enables the collider registered to `XRSimpleInteractable`. The next Grip can therefore call the existing bag-preparation flow, hide the bag, and continue the PPE sequence.

### Files

- `Assets/1.JSR/Scripts/Whiteboard/PPEProcessDiagramController.cs`
- `Assets/1.JSR/Scripts/ResponseBagPackingController.cs`
- this handoff

### Validation

- CODE VERIFIED: Unity C# manual compile completed with 0 errors
- PLAY MODE VERIFIED: NOT TESTED
- QUEST VERIFIED: NOT TESTED

## Solid left wall (2026-08-27)

`Wall_Left_Back` was a 2.4 m door-side segment at `z=1.8`, leaving a 1.2 m gap with `Wall_Left_Front`. It now matches `Wall_Right`: position `(-3.5, 1.5, 0)`, scale `(0.15, 3, 6)`. Lower band `WallBand_Left_Back` matches `WallBand_Right` (`z=5.9` at `x=-3.4`). Door leftovers `Wall_Left_Front`, `Wall_Left_Top`, `WallBand_Left_Front`, and `PPE_RoomExitBlocker` are inactive.

Did not change desk, bag, cabinets, or common XR.

### Files

- `Assets/Scenes/04_PPE.unity`
- this handoff

### Validation

- SCENE VERIFIED: left wall transform matches right wall; door pieces inactive
- PLAY MODE VERIFIED: NOT TESTED
- QUEST VERIFIED: NOT TESTED

## Desk height + hide packed items (2026-08-27)

`PPE_DeskTop` world Y `0` → `0.15`. GasDetector / AbsorbentPad / AbsorbentSock stay desk children, so they rise with the table. Rotation and XZ are unchanged. Bag transform is unchanged.

Packed items `SetActive(false)` after snap. Reset/`SetPacked(false)` turns them back on.

### Files

- `Assets/Scenes/04_PPE.unity`
- `Assets/1.JSR/Scripts/ResponseBagPackableItem.cs`
- this handoff

### Validation

- CODE VERIFIED: packed hide via SetActive(false)
- SCENE VERIFIED: desk Y 0.15; item local poses unchanged
- PLAY MODE VERIFIED: NOT TESTED
- QUEST VERIFIED: NOT TESTED

## ResponseBag GhostPoseCalibration + table packing (2026-08-27)

## ResponseBag GhostPoseCalibration + table packing (2026-08-27)

Did not change desk/bag Position/Rotation/Scale, glove pair rules, ChemicalBoots Ghost, common XR, App/Stage adapters, or wearable `ResponseBag_Wearable`.

### Why the bag had a GhostPoseCalibration child

`ResponseBag_HardShell` carries `PPEEquipable` (`itemId: ResponseBag`) so sequence/checklist can complete the bag step. `PPEEquipable` default `ppeType` is Gloves, and `Awake`/`OnValidate` always created a `GhostPoseCalibration` child plus Scene-view mesh gizmos. That is the wearable glove/boot Ghost path. The world bag is not wearable PPE and does not need a ghost hand.

Scene child `GhostPoseCalibration` is removed. `PPEEquipable` no longer creates or draws Ghost calibration for `ResponseBag`. Bag `XRSimpleInteractable` and `ResponseBagPrepareInteractable` are disabled so the bag is a packing destination, not a grab/ghost target.

### Packing

Table items stay on authored `PPE_DeskTop`. Play binds `GasDetectorSlot` / `AbsorbentPadSlot` / `AbsorbentSockSlot` as `ResponseBagPackableItem` grabs. Release inside the bag mouth (`BagPackingTrigger`) snaps to `HClMeterSlot` / `AbsorbentPadSlot` / `AbsorbentSockSlot`, calls `InspectResponseBagItem` + `SessionEquipmentInventory.Store`, then `TryCompleteResponseBagPreparation` when all three are in.

Mouth trigger used to sit at bag-root `(0, 0.14, 0)` size `(0.28, 0.18, 0.18)`. The visual is `ResponseBag_HardShell_Visual` at local `y=-0.9` / scale `0.63`, and the fitted root collider is `(0.83, 1.12, 0.40)` centered at `y=0.56`. Putting an item into the visible case missed that small origin box. Trigger, open-shell walls, and snap slots now fit the visual renderer bounds (90% interior, 8 cm slack). Fall-in watch is 2 s.

### Files

- `Assets/1.JSR/Scripts/PPEEquipable.cs`
- `Assets/1.JSR/Scripts/ResponseBagPackingController.cs`
- `Assets/Scenes/04_PPE.unity`
- this handoff

### Validation

- CODE VERIFIED: ResponseBag skips GhostPoseCalibration; packing trigger + fall-in watch
- SCENE VERIFIED: bag children are `BagContentsRoot` + visual only; `ghostPoseCalibration` none; bag Simple/Prepare disabled
- PLAY MODE VERIFIED: NOT TESTED
- QUEST VERIFIED: NOT TESTED
- DEVICE TEST REQUIRED: after donning, grab HCl meter / absorbent pad / absorbent sock from the desk, put each into `ResponseBag_HardShell`, confirm snap + all-three completion. Desk/bag transforms must stay as authored.

## ProtectiveGloves Controller Grab ownership + near-cast origin (2026-08-27)

Did not change NearFar radius, Interaction Layer, ChemicalBoots, InnerGloves grab distance, sequence, WrongHand, ResponseBag, table, XR Interaction Manager, or App. Select allowed distance remains **0.06 m**. Assist 0.16 m is still hover-only.
- Follow-up: Phase 3 scene integrity + ChemicalGloves visual ownership remains in place.
- Follow-up: Dropped cabinet PPE no longer acts as walkable ground for CharacterController / GravityProvider (IgnoreCollision + Ignore Raycast while Held/Released).
- Follow-up: `PPESequenceManager.CancelWearableSelections` uses `IXRSelectInteractable` / `IXRHoverInteractable` (XRI 3 obsolete `XRBaseInteractable` overloads).

## ProtectiveGloves Controller Grab ownership + near-cast origin (2026-08-27)

Did not change NearFar radius, Interaction Layer, ChemicalBoots, InnerGloves grab distance, sequence, WrongHand, ResponseBag, table, XR Interaction Manager, or App. Select allowed distance remains **0.06 m**. Assist 0.16 m is still hover-only.

`04_PPE` standalone has no active NearFar on its local controllers (Ray Interactor inactive). Controller Grab validation is `01_App → 04_PPE`. Standalone Controller Select failure is not treated as a ProtectiveGloves code bug.

### Fix 1 — Logical root collider ownership

`PPE_Gloves` `XRSimpleInteractable` had `m_Colliders: []`, so XRI auto-collected `Left/RightProtectiveGlovePhysical` colliders. NearFar then hovered the logical root; `PPEOutlineHighlight.Process` rejects non-ray Select.

Scene now has disabled trigger child `LogicalRootColliderLock` (1 mm) assigned as the only root collider. Coordinator Awake (`-300`) pins that dummy before `XRSimpleInteractable` Awake so the list is never empty. PhysicalCollider and NearGrabAssist stay on each `PPEHandGrabbable`. Dummy is disabled and does not physics-grab.

### Fix 2 — Repair toggle removed

`RepairProtectiveGloveColliderOwnership` and `grab.enabled` / `simple.enabled` toggles are gone. Root collider list is edited in place. Inner/Boots `EnsureHoverOnlyInteractable` still clears as before.

### Fix 3 — Protective Controller distance origin (0.06 m kept)

ProtectiveGlove NearFar Select now measures PhysicalCollider distance from `nearInteractionCaster.effectiveCastOrigin` (fallback `castOrigin`), not generic attach / interactor root. Allowed distance stays 0.06 m. Assist is not a Select volume.

`[First Glove SelectAttempt]` logs `OriginSource=NearCaster|Attach`.

### Files

- `Assets/1.JSR/Scripts/PPEGlovePairCoordinator.cs`
- `Assets/1.JSR/Scripts/PPEOutlineHighlight.cs`
- `Assets/1.JSR/Scripts/PPEDirectGrabProximityFilter.cs`
- `Assets/Scenes/04_PPE.unity`
- this handoff

### Validation

- CODE VERIFIED: ownership pin + dummy lock; no grab.enabled toggle; Protective origin uses near caster; 0.06 m unchanged
- SCENE VERIFIED: `PPE_Gloves` colliders = `LogicalRootColliderLock` only; members still list PhysicalCollider + NearGrabAssist
- PLAY MODE VERIFIED: NOT TESTED (`01_App → 04_PPE` required)
- QUEST VERIFIED: NOT TESTED

## PPE grab / ResponseBag regression correction (2026-08-27)

Did not change table or cabinet Position/Rotation/Scale/Parent/Prefab transform, pair hand order, WrongHand, proximity distances, ChemicalBoots Ghost, Release-to-Equip, common XR, App/Stage adapters, or other scenes.

### ProtectiveGloves physical collider ownership

Component-by-component comparison found no grab-blocking serialized mismatch in the physical members: both pairs are active on Default, use enabled solid CapsuleCollider + enabled trigger NearGrabAssist, kinematic/no-gravity Rigidbody, Interaction Layer bit 1, Single Select, Attach None, Dynamic Attach/Match Position/Match Rotation/Snap on, and one `PPEDirectGrabProximityFilter`. Protective left/right Coordinator, side, collider, assist, and EquipReference references are valid.

The meaningful runtime difference was XRI collider registration ownership. The Unity log records both Protective `PhysicalCollider`s remaining associated with parent `PPE_Gloves (XRSimpleInteractable)`, which registered before `Left/RightProtectiveGlovePhysical (PPEHandGrabbable)`. The working Inner pair records the reverse order: each physical member remains the owner and the parent root loses the duplicate registration. Consequently Protective hover could target the logical root, whose ray-only filter rejects XR Hands before the member proximity filter is reached.

`PPEOutlineHighlight` now waits until initial XRI registration is complete, then only for `ChemicalGloves` unregisters the logical root, strips its auto-collected member colliders, re-registers the two existing physical member grabs first, and finally re-enables the collider-free logical root. No distance, layer, collider geometry, hand resolver, attach pose, or shared interactor setting changed.

### Glove rotation

The previous follow-up added `PPEHandGrabbable.EnsureTrackedGloveGrabAttach()`. It assigned the placement-only `EquipReference` as the XRI attach, then forced `useDynamicAttach = false`. Because Match Rotation is enabled, Select immediately replaced the authored world orientation with the hand/EquipReference orientation.

That override was removed. InnerGloves and ProtectiveGloves retain the authored XRI configuration: Attach Transform None, Dynamic Attach on, Match Position/Rotation on, Snap To Collider Volume on. XRI therefore creates the attach pose from the contact pose and preserves the visible pre-Select orientation on the first frame. Pair-root `XRSimpleInteractable` remains non-ray-select rejected so actual hand Grab is owned by the left/right physical members.

### ResponseBag collider

`ResponseBag_HardShell` had one enabled, solid root BoxCollider: center `(0.0595, 0.5623, -0.0122)`, size `(0.8280, 1.1245, 0.3964)`, Default layer. It enclosed the mouth and surrounding empty space.

The legacy root collider is disabled in `04_PPE` and at packing initialization. Runtime creates compact open-shell `PhysicalColliders/Bottom`, `LeftSide`, `RightSide`, `Back`; there is no front/top wall. `BagPackingTrigger` remains a separate `IsTrigger=true` mouth volume `(0.22, 0.12, 0.16)` and cannot physically block a hand or item.

### General equipment Grab

No ProtectiveGloves equip path changes either hand Interactor, interaction layer, or global Select filter. The apparent post-glove failure occurred because the equipment-preparation setup runs after donning and `ResponseBagPackableItem.IgnorePlayerLocomotionCollisions()` changed each equipment collider's Physics layer to `Ignore Raycast`. Near/Far could no longer discover HCl meter, pad, or sock.

Packable items now keep their authored Physics layer and Interaction Layer. Only `Physics.IgnoreCollision(itemCollider, playerCharacterController)` is applied, so locomotion does not climb the item while both left and right Near/Far interactors can still select it. PPE pair filters remain attached only to PPE wearable grab objects.

### Packing / completion

Packing remains Allowed Item + `BagPackingTrigger` occupancy + Release. Successful release snaps to the existing per-item slot, records Inventory once, allows any item order, and completes the existing ResponseBag sequence step only after all three items are packed. The protected sequence still contains its existing WasteContainer step after ResponseBag; this task did not change the stage-completion event contract.

Integration request: if the product requirement is to remove WasteContainer and fire `OnPPESequenceCompleted` immediately after the three packed items, Integration must approve that protected completion-condition change.

### Files

- `Assets/1.JSR/Scripts/PPEOutlineHighlight.cs`
- `Assets/1.JSR/Scripts/PPEHandGrabbable.cs` (uncommitted attach override removed; resulting file matches branch baseline)
- `Assets/1.JSR/Scripts/ResponseBagPackableItem.cs`
- `Assets/1.JSR/Scripts/ResponseBagPackingController.cs`
- `Assets/Scenes/04_PPE.unity` (legacy ResponseBag root collider disabled only)
- this handoff

### Validation

- CODE VERIFIED (`dotnet build Assembly-CSharp.csproj --no-restore`: 0 errors; existing warnings only)
- SCENE VERIFIED (Protective/Inner member references valid; four glove grabs retain Dynamic Attach; oversized bag root collider disabled; table transform unchanged)
- PLAY MODE VERIFIED: NOT TESTED
- QUEST VERIFIED: NOT TESTED

## Required-step outline hides while held (2026-08-27)

Did not change glove pair rules, ChemicalBoots Ghost, Release-to-Equip, common XR, App/Stage adapters, desk/bag transforms, or packing pose.

Direct-hand Grab (Near-Far / non-ray) turns off all outlines on that PPE — including the cyan/yellow required-step pulse — until Release. If it is still the current sequence step after release, the pulse comes back. Ray sticky select does not hide the required LED. Pair-root SimpleInteractable select no longer counts as a hand grab.

### Files

- `Assets/1.JSR/Scripts/PPEOutlineHighlight.cs`
- this handoff

### Validation

- CODE VERIFIED
- SCENE VERIFIED: not required (script-only)
- PLAY MODE VERIFIED: NOT TESTED
- QUEST VERIFIED: NOT TESTED

## Response equipment table packing (2026-08-27)

Did not change glove pair rules, ChemicalBoots Ghost, Release-to-Equip, common XR, or App/Stage adapters.

Packing no longer overwrites authored item position/rotation/scale on Play. Grab wiring and bag snap still run. Desk Transform is not written.

### Files

- `ResponseBagPackableItem.cs`, `ResponseBagPackingController.cs`
- `PPEResponseEquipmentTablePackingSetup.cs`
- `ResponseEquipmentManager.cs` (skip hide-until-Y and Y-inspect while packing is present; restore table on bag reset)
- `PPESequenceManager.cs` (`NotifyBagContentsChecked` no longer skipped while TTS is busy)
- `04_PPE.unity` (`ResponseBagPackingController` on `Response_EquipmentManager`, `PPE_DeskTop` FBX instance)
- this handoff

### Validation

- CODE VERIFIED
- SCENE VERIFIED (packing component + desk instance)
- PLAY MODE VERIFIED: NOT TESTED
- QUEST VERIFIED: NOT TESTED

## Body/head Release-to-Equip range (2026-08-27)

Suit / Face / Respirator / Helmet-class items used a tight EquipReference-to-target distance. Face Ghost also hides below `0.22 m` while Face/Respirator equip was `0.18–0.20 m`, so the guide could disappear before Release succeeded. Grab range, glove pair rules, and boot Ghost were not changed.

Scene `PPEEquipable.equipDistance`: FaceShield `0.20` → `0.35`, Respirator `0.18` → `0.35`, Suit `0.30` → `0.45`. Authoring profiles match, including Helmet `0.18` → `0.35` (Helmet is still not in `04_PPE`). Inner/Protective gloves and boots keep previous placement distances.

### Validation

- CODE VERIFIED (authoring profiles)
- SCENE VERIFIED (three wearable equipDistance values)
- PLAY MODE VERIFIED: NOT TESTED
- QUEST VERIFIED: NOT TESTED

## Glove grab range aligned with other PPE (2026-08-27)

Did not change pair hand order, opposite-hand rule, ChemicalBoots Ghost, common XR, or cabinet physics.

InnerGloves and ProtectiveGloves Select used `0.16 m` against PhysicalCollider **or** the large `NearGrabAssist` trigger, so the remaining glove could be grabbed from farther than suit / respirator / face (`0.06 m`, physical only). Select now uses the same `0.06 m` physical-collider distance. Assist stays a hover trigger only. Far-ray still fails above `0.5 m`. `HAND_UNRESOLVED` / `WRONG_HAND` remain log-only, not Select rejects.

### Files / Inspector

- `PPEDirectGrabProximityFilter.cs`: `NearGrabHoverSelectDistance = 0.06`; glove Select is physical-only.
- `04_PPE.unity`: four glove `maxDirectGrabDistance` `0.16` → `0.06`. Boots stay `0.10`. Other PPE stay `0.06`.
- Authoring already reads the filter constant.

### Validation

- CODE VERIFIED
- SCENE VERIFIED (four glove filter distances `0.06`)
- PLAY MODE VERIFIED: NOT TESTED
- QUEST VERIFIED: NOT TESTED

## Cabinet shelf drop after Grab/Release (2026-08-27)

Did not change glove pair logic, ChemicalBoots Ghost rotation, common XR, App/Stage flow, throw policy, or PPE Rigidbody restore.

### Root cause

Start placement is a kinematic stored pose, not shelf collision. `PPE_Storage` FBX has `addColliders: 0` and no added colliders. `PPE_StorageRoot` only had FitCabinet's thin **back-panel** BoxCollider (`1.7 × 2.05 × 0.04`, `IsTrigger=0`, Default layer). There were no shelf/floor/side BoxColliders. After Grab, `ApplyReleasedPhysics` correctly sets `isKinematic=false`, `useGravity=true`, `ContinuousDynamic`, clears velocity, and leaves physical colliders enabled/non-trigger. Gravity then had nothing to land on, so items fell through the visual shelves.

Collision matrix still allows Default vs Default and Default vs Ignore Raycast. Layer move on Held/Released is not why items fell through shelves.

### Fix

Authored `PPE_StorageRoot/Physics` BoxColliders (non-trigger, enabled, Default): Left/Right/Back walls, five shelves under slot Y, and an invisible Catch under the cabinet footprint only. `PPECabinetPhysics` keeps those colliders in sync and disables the duplicate root back panel. Grab/Release physics restore was already correct; throw velocity was not clamped.

### Files / Inspector

- `Assets/1.JSR/Scripts/PPECabinetPhysics.cs` + `.meta`
- `Assets/1.JSR/Scripts/PPEStoredDisplayState.cs` (one-shot Before Grab / After Release logs)
- `Assets/1.JSR/Editor/PPECabinetFitSetup.cs` (`Ensure Cabinet Shelf Physics` menu; FitCabinet also ensures Physics)
- `Assets/Scenes/04_PPE.unity`
- this handoff

Scene objects: `PPE_StorageRoot` (`PPECabinetPhysics`), `Physics/LeftWallCollider`, `RightWallCollider`, `BackWallCollider`, `Shelf_01`–`Shelf_05_Collider`, `CatchCollider`. Root back-panel BoxCollider `Enabled=0`. Shelf BoxColliders `IsTrigger=0`, thickness `0.06`. Catch `IsTrigger=0` at local Y `0`, cabinet footprint only.

### Validation

- CODE VERIFIED: missing shelf colliders; PPE release already restores gravity/colliders/ContinuousDynamic and zeros velocity.
- SCENE VERIFIED: Physics children and BoxCollider YAML authored; PPE_Storage FBX `addColliders: 0`.
- PLAY MODE VERIFIED: NOT TESTED
- QUEST VERIFIED: NOT TESTED
- DEVICE TEST REQUIRED: Grab each of InnerGloves, ProtectiveGloves, ChemicalBoots, Respirator, FaceProtection, ProtectiveSuit, move onto a shelf, slow Release, confirm rest. Ghost Release-to-Equip must still equip. Dropped recovery still fires if an item reaches the floor.

### Integration request

none

## First-glove XR Hands select regression (2026-08-27)

`ProcessGloveSelectAttempt` rejected Select when `ResolvedHand == Unknown`, same-hand, or attach was null. That ran on the first glove too. XR Hands Direct/Near often has `handedness = None` at filter time, so Inner/Protective first-glove Select died with `HAND_UNRESOLVED` even when distance was valid. HEAD only checked distance; hand belonged to Release-to-Equip.

Select again allows `physical <= 0.16 OR assist <= 0.16`. `HAND_UNRESOLVED` / `WRONG_HAND` / `NO_ATTACH_TRANSFORM` are logged, not reject reasons. Far ray still fails at `> 0.5 m`. ChemicalBoots Ghost files were not changed in this pass.

## Second glove SelectAttempt + ChemicalBoots Ghost yaw (2026-08-27)

Did not change App NearFar, Release-to-Equip structure, BothEquipped, logical Equip-once, PPE donning order, or other Stage files.

### Glove SelectAttempt

Hand-off was required because `PPEGrabHandResolver` treated XR Hands pinch / closer palm to the **object** as more authoritative than the selecting interactor. After Left was equipped, grabbing remaining Right with the Left NearFar still recorded `ResolvedHand=Right` (bare hand nearer the shelf glove). Release-to-Equip then saw WRONG_HOLDING_HAND and the piece returned to the cabinet. Grabbing with the bare hand and handing off moved the object, so pinch/palm finally agreed with the equipped interactor.

Fix: Resolve uses `Interactor.handedness` first. Pinch/palm are fallback only when handedness is unknown. Glove SelectAttempt logs `[First Glove SelectAttempt]` / `[Glove SelectAttempt]` on change. Same-hand and unresolved hand are **not** Select rejects. `GrabSessionHoldingHand` is still written on SelectEntered from Resolve. There is no left-first / right-first pair order in the coordinator.

### ChemicalBoots Ghost

Look-down facing used `BodyYaw * GhostPoseCalibration.localRotation`. Scene already stored `bootModelAxisCorrectionEuler (0,180,0)` on both boot members, but the C# field had been removed so the 180 yaw never applied. The field is restored and multiplied after calibration for BodyLowerLeg Ghost visuals only. EquipReference / Release-to-Equip distance is unchanged. XR Origin, camera, and lower-leg targets are not rotated.

### Files / validation

- Changed: `PPEGrabHandResolver.cs`, `PPEDirectGrabProximityFilter.cs`, `PPEGlovePairMember.cs`, `PPEPlacementGuideManager.cs`, `PPEBootGhostFacingPreview.cs`, this handoff.
- Inspector/Scene: no new boot calibration YAML; existing `(0,180,0)` now binds again.
- DEVICE TEST REQUIRED: ProtectiveGloves and InnerGloves start either side, second glove direct grab without hand-off, WrongHand reject, far-ray reject, BothEquipped once. ChemicalBoots Ghost look-down ㅗ, stable when turning, equip distance unchanged.
- Integration request: none.

## Second ProtectiveGlove select after one side equipped (2026-08-27)

Scope is limited to tracked-hand glove pair Select distance (InnerGloves + ProtectiveGloves). Sequence, pair completion, WrongHand/Release-to-Equip, Ghost calibration, cabinet, scale, XR Origin, TTS, boots/suit/respirator/face, ResponseBag, WasteContainer, and App NearFar/Direct interactors were not changed.

### Cause

Hover used `NearGrabAssist` plus the NearFar near cast radius (`0.16 m`). Select used `PPEDirectGrabProximityFilter` against the tight `PhysicalCollider` only, at `0.065 m`, and could fall back to an XR Hands Palm that disagreed with the selecting interactor attach. After one glove was worn, the remaining glove could highlight and still reject Grip (`Hovering=true`, `FilterAllows=false`).

### Fix

- Tracked-hand glove pairs share one distance: `PPEDirectGrabProximityFilter.NearGrabHoverSelectDistance` (`0.16 m`). That value must stay consistent with the PPE NearFar hover/assist range. Highlighted gloves stay directly selectable.
- Glove Select measures PhysicalCollider **and** NearGrabAssist. Assist remains a trigger; it is not a physics grab collider.
- Distance uses only the current selecting interactor `GetAttachTransform`. Far-ray Grip stays rejected because distance is from the hand attach, not the ray hit. Other PPE keep their serialized physical-only distances (`0.06` / `0.10`).
- InnerGloves and ProtectiveGloves scene `maxDirectGrabDistance` are `0.16`. Runtime also forces the shared constant on tracked-hand pair members so a stale `0.065` cannot return.
- Authoring `GloveGrabDistance` reads the same constant. Boot authoring stays `0.10`.
- Second-glove on-change logs (`[Second ProtectiveGlove Grab]` / `[Second InnerGlove Grab]`) report Interactor, Hand, CorrectHand, NearGrab, Distance, AllowedDistance, FilterAllows, SelectSucceeded, FirstFailure (`TOO_FAR` / `WRONG_HAND` / `NOT_NEAR_INTERACTOR` / `PAIR_STATE_INVALID`).

### Files / validation

- Changed: `PPEDirectGrabProximityFilter.cs`, `PPEGlovePairCoordinator.cs`, `PPEPairPhysicalAuthoringSetup.cs`, `Assets/Scenes/04_PPE.unity` (four glove filter distances), this handoff.
- Inspector/Scene manual work: none beyond the four serialized glove distances.
- Validation: InnerGloves + ProtectiveGloves YAML distances are `0.16`; suit/respirator/face stay `0.06`; boots stay `0.10`. Runtime C# was not compiled in this pass. Play Mode and Quest were not run.
- DEVICE TEST REQUIRED: first and second InnerGlove/ProtectiveGlove grabs with the opposite hand, Highlight-edge Grip, far-ray Grip rejected, WrongHand does not equip, BothEquipped logical Equip once.
- Integration request: none.

## Second ProtectiveGlove grab + reversed stored Gloves/Boots + Boot Ghost facing (2026-08-24)

Scope is limited to the second ProtectiveGlove XRI source/diagnostic, authored physical display rotations for four gloves/two boots, and verification of the existing boot Ghost calibration. Sequence, pair completion, Release-to-Equip, glove Ghost calibration, scale, cabinet dimensions, XR Origin, reset architecture, TTS, other PPE, ResponseBag, and WasteContainer were not changed.

### Second ProtectiveGlove

- First failure identified in the pre-fix select path: Hover used the remaining member's large `NearGrabAssist`, but `PPEDirectGrabProximityFilter` measured from a resolver-selected XR Hands Palm to the tight physical collider. That resolver could disagree with the actual selecting NearFar hand, yielding `Hover=true` and `FilterAllows=false`.
- Initial select distance remains attach-first: actual `IXRSelectInteractor.GetAttachTransform(target)` from active XR Origin Left/Right Controller -> NearFar/Direct interactor. XR Hands Palm is fallback-only. No visible `Hand` search exists and the authored `0.065 m` ProtectiveGlove distance was not increased.
- Coordinator diagnostics no longer fall back to a scene-wide NearFar search. They use the cached `PPEHandTargetResolver` source from `PPEActiveXrOrigin` and report on-change under `[Second ProtectiveGlove Grab]`, including ordered `FirstFailure`.
- `PPEWearableVisualOnly` keeps `Hand`/Animator but forces worn roots to Ignore Raycast and disables child Colliders, `XRBaseInteractable`, `XRBaseInteractor`, and `PPEDirectGrabProximityFilter`.
- Static code state after Left equip: Left controller/interactor/select/attach are never disabled; only Left bare Renderers are hidden and Left wearable is visual-only. `RightProtectiveGlovePhysical` remains active with `PPEHandGrabbable`, physical collider, NearGrabAssist, proximity filter, and Stored/Released state.
- DEVICE TEST REQUIRED: actual Quest transition must show `Hovering=true`, `FilterAllows=true`, `SelectSucceeded=true`, `FirstFailure=NONE`.

### Authored stored presentation

- Reversed each physical member root around its local cabinet-vertical Y axis. Local position and scale are unchanged; Left/Right identities and child local calibration values are unchanged.
  - LeftInnerGlovePhysical: quaternion `(0,0,0,1)` -> `(0,1,0,0)` / Euler `(0,180,0)`
  - RightInnerGlovePhysical: quaternion `(0,0,0,1)` -> `(0,1,0,0)` / Euler `(0,180,0)`
  - LeftProtectiveGlovePhysical: quaternion `(0,0,0,1)` -> `(0,1,0,0)` / Euler `(0,180,0)`
  - RightProtectiveGlovePhysical: quaternion `(0,0,0,1)` -> `(0,1,0,0)` / Euler `(0,180,0)`
  - LeftBootPhysical: quaternion `(0,0,0,1)` -> `(0,1,0,0)` / Euler `(0,180,0)`
  - RightBootPhysical: quaternion `(0,0,0,1)` -> `(0,1,0,0)` / Euler `(0,180,0)`
- No clipping position correction was applied.
- `PPEStoredDisplayState.Awake/CaptureStoredPose` captures the revised root local rotation; `ResetPpeRoomTraining()` therefore restores the new reversed authored pose, not the former identity rotation.

### Boot Ghost

- Mesh toe directions are approximately Left `(+0.9860,0,+0.1665)` and Right `(+0.9860,0,-0.1667)` in boot-local space; Y is the local vertical axis.
- Reverification showed that an additional Y 180-degree model-axis layer would be a double correction: it changes toe dot BodyForward from approximately `+0.999` to approximately `-0.999`. That extra layer was removed.
- Existing side calibrations remain unchanged:
  - Left `(0,-0.64554595,0,0.76372147)`
  - Right `(0,-0.7637795,0,0.64547724)`
- Those existing calibrations are the single model-axis correction. They rotate both toe vectors to `(0,0,+1)` within numerical error, so dot with preview BodyForward is approximately `1.0`.
- `PPEBootGhostFacingPreview` uses the same calibration-only formula as runtime.
- HMD pitch/roll remain absent: `PPEPlayerBodyReference.ComputeLowerLegWorldPose` returns `BodyYawRotation`; look-down only controls visibility.

### Files / validation

- Changed for this follow-up: `PPEGlovePairCoordinator.cs`, `PPEWearableVisualOnly.cs`, `PPEGloveHandTargetFollower.cs`, `PPEBootGhostFacingPreview.cs`, `Assets/Scenes/04_PPE.unity`, this handoff.
- Inspector/Scene manual work: none beyond the six serialized stored root rotations already authored in `04_PPE`.
- Validation: runtime and Editor C# projects compile with 0 errors. Scene YAML contains exactly six reversed physical root rotations and no extra boot-axis layer.
- DEVICE TEST REQUIRED: second-glove Hover/Filter/Select, visual cabinet facing/clipping, both boot Ghost toes on Quest, body 90-degree turn, looking farther down without tilt, and Reset returning all six items to new rotations.
- Integration request: none.

## Glove target-hand presentation + authoritative wearable + second-side grab (2026-08-24)

Scope is limited to InnerGloves / ProtectiveGloves target-hand visual presentation, ChemicalGloves per-side wearable state, and the ProtectiveGloves second-side XRI select-distance path. Sequence, pair completion conditions, Release-to-Equip, Ghost calibration, cabinet, scale, XR Origin, reset architecture, and other PPE were not changed.

### Visual state

- `ChemicalGlovesWearableVisual` owns the exact scene visual roots `PPE_DefaultHand_Left`, `PPE_DefaultHand_Right`, `PPE_VrHand_Left_Wearable`, and `PPE_VrHand_Right_Wearable`.
- The ChemicalGloves coordinator applies its authoritative false/false state in `Start`, so PPE IDLE begins bare/bare even though the App callback correctly defers to the coordinator.
- A valid opposite-hand glove grab sets a transient target-side presentation flag. Only `Renderer.enabled` below the target `PPE_DefaultHand_*` root is changed; the root, `Hand`, `Animator`, controller tracking, NearFar/Direct interactor, select input, attach transform, and target resolver stay active.
- Wrong-hand grabs never enter the transient state. Tracking loss, invalid release, interaction cancellation, placement-manager disable, and Reset clear it. InnerGloves have no dedicated worn visual, so successful InnerGlove equip restores the bare renderer after the Ghost is removed.
- ProtectiveGlove success first applies the coordinator's side flags while the bare hand is still transiently hidden, then clears the transient flag. This produces Ghost -> solid ChemicalGlove without a bare-hand frame.
- `ApplyAuthoritativeVisual()` now always reads `PPEGlovePairCoordinator.ChemicalGlovesInstance.LeftEquipped / RightEquipped`. It runs after each side change and again after logical pair `PPEEquipmentManager.Equip`, removing the final-side ordering gap. The PPE-stage `PPEStateController.StateChanged` callback still exits while the coordinator exists, and `ApplyPpeRoomState` also rejects stale caller booleans in favor of coordinator flags.
- `ResetPair()` clears transient presentation, pair flags, both wearables, and restores both bare renderers while restoring physical members to their authored Stored poses. It does not disable interaction hands and remains repeat-safe.

### Second-side interaction

- Root cause in the current filter path: `PPEDirectGrabProximityFilter` preferred an independently valid XR Hands Palm pose over the actual selecting NearFar/Direct interactor attach. In controller-driven PPE interaction, the palm could be valid but spatially unrelated to the active XRI selection source, producing `FilterAllows=false` even when that interactor hovered the opposite physical glove.
- Initial-select distance now uses `IXRInteractor.GetAttachTransform(target)` first. XR Hands Palm remains fallback-only when an active attach is unavailable. The authored ProtectiveGlove `MaxDirectGrabDistance` remains unchanged at `0.065 m`.
- Wearables are still forced to Ignore Raycast and stripped of Colliders, `XRBaseInteractable`, and `XRBaseInteractor`; target resolution continues to use active XR Origin Left/Right Controller -> NearFar/Direct attach and never searches visible `Hand` meshes.
- Development log `[ProtectiveGlove Second Grab]` is on-change only and reports interactor active/enabled, target tracking, select input state, attach validity, resolver source, remaining physical member/grabbable/collider/assist/filter/storage state, actual filter distance/max distance, exact-side Hover, FilterAllows, SelectCandidate, and Select.

### Changed files / validation / remaining

- Changed: `ChemicalGlovesWearableVisual.cs`, `PPEPlacementGuideManager.cs`, `PPEGlovePairCoordinator.cs`, `PPEGlovePairMember.cs`, `PPEDirectGrabProximityFilter.cs`, this handoff.
- Inspector/Scene manual work: none.
- Validation: `dotnet build Assembly-CSharp.csproj --no-restore` and `dotnet build Assembly-CSharp-Editor.csproj --no-restore` both completed with 0 errors.
- DEVICE TEST REQUIRED on Quest: confirm valid-grab bare Renderer off/Ghost on; wrong-hand no hide; failed-release restoration; first side wearable with both interactors active; second side `Hover=true`, `FilterAllows=true`, `Select=true`; final pair stays true/true with both wearables; unrelated interactable remains selectable by both hands; reset twice returns bare/bare and cabinet poses.
- Known limitation: InnerGloves still have no dedicated worn visual.
- Integration request: none. `Assets/Scripts/App/PPEStateController.cs` was read-only verified and not changed for this task.

## Glove Ghost hand alignment + outline removal (2026-08-24)

Scope is limited to Left/Right InnerGlove and Left/Right ProtectiveGlove Ghost position, rotation, editor calibration, and Ghost material. Grab logic, pair completion, Release-to-Equip, sequence, cabinet, PPE scale, XR Origin, hand tracking, EquipReference placement, Boots, Suit, Respirator, FaceProtection, ResponseBag, and WasteContainer were not changed.

### Runtime

- The four physical glove members have independently authored, non-identity `GhostPoseCalibration` children in `04_PPE`. Saved position / Unity Euler rotation:
  - Left Inner: `(0.0554254, 0.0010908, -0.2083801)` / `(15.6848, 357.1492, 164.1633)`
  - Right Inner: `(-0.0601459, -0.0027404, -0.2074570)` / `(15.1093, 5.0930, 198.8746)`
  - Left Protective: `(-0.0036303, -0.0271679, 0.0076292)` / `(301.2851, 318.6022, 36.7330)`
  - Right Protective: `(-0.1288192, 0.0167177, 0.0275691)` / `(301.0833, 41.1191, 323.5040)`
  The old shared manager `gloveGuidePositionOffset` / `gloveGuideRotationOffsetEuler` fields and scene values remain removed.
- Runtime remains `TargetPalm.position + TargetPalm.rotation * calibration.localPosition` and `TargetPalm.rotation * calibration.localRotation`. The grabbed/holding hand, HMD, body yaw, and EquipReference do not affect glove Ghost pose.
- The previous floating seed `(0,0.02,0.03)` was removed. Reset now returns a glove calibration to zero position / identity rotation.
- Root scale still comes from the corresponding physical Visual; calibration changes pose only.

### Outline/material diagnosis and fix

- `PPEOutlineHighlight` legitimately appends the `PPE/Outline` runtime material to the physical glove Renderer while hovered/selected. The renderer-only Ghost builder copied the physical Renderer material array. InnerGlove appearance replaced only `sharedMaterial` slot 0, leaving the appended outline slot; other gloves could retain the duplicated material-slot count. This was the yellow/green edge/flicker path.
- Tracked-hand glove Ghosts are sanitized before appearance: Renderer material arrays are cleared to one eventual fill slot; Colliders, Rigidbodies, `XRBaseInteractable`, LineRenderers, and MonoBehaviours named Outline/Highlight/Hover are disabled. The normal physical PPE outline remains unchanged.
- All four glove Ghosts now use one emission-free, single-sided URP transparent fill clone: soft blue, NotReady alpha `0.26`, Ready alpha `0.40`. Ready changes fill alpha only. `_EMISSION` is disabled and emission, outline, rim, and Fresnel properties are forced off when present. Single-sided rendering avoids doubled transparent silhouettes at grazing angles.

### Editor authoring

- `Tools > HCl Training > PPE > Glove Ghost Calibration Preview` (also available under the previous Preview name) is off by default and editor-only.
- Preview loads the exact `LeftHand.fbx` / `RightHand.fbx` models used by the scene's `PPE_DefaultHand_Left` / `PPE_DefaultHand_Right`. It reconstructs their actual Controller-child visual root relative to the Controller-fallback TargetPalm (`target = controller + controller.rotation * (0,0,0.07)`) instead of assuming the FBX Palm axes equal the runtime target axes. Hidden preview objects are destroyed when preview is disabled, scripts reload, or Play Mode starts.
- Select a `GhostPoseCalibration` and use normal Move/Rotate tools; edits are converted back into Palm-local position/rotation immediately.
- `Align Ghost To Reference Hand` is an explicit geometry-based starting helper. It uses Palm/MiddleTip bones when available and mesh bounds otherwise, then aligns palm center, finger direction, and palm normal. It never runs automatically and should be manually fine-tuned.
- `Reset Ghost Calibration` and explicit `Copy Mirrored Calibration` remain available. Left/Right are never auto-mirrored.

### Verification / remaining manual work

- `dotnet build Assembly-CSharp.csproj --no-restore` and `dotnet build Assembly-CSharp-Editor.csproj --no-restore`: 0 errors (existing repository warnings remain).
- `Align Ghost To Reference Hand` was explicitly run for each member, then each pose was independently fine-tuned against exported geometry from the same Scene Preview hand FBXs. Palm, side, and wrist projections confirm matching finger direction, palm overlap, wrist-side placement, correct thumb handedness, and no mirrored side.
- Inner gloves required an approximately 180-degree local-Z axis correction. Protective gloves started with an approximately -90-degree local-X correction and then received side-specific palm/normal fine-tuning.
- Scene YAML check: four unique non-identity `GhostPoseCalibration` objects, four serialized member references, unchanged local scale `(1,1,1)`, no duplicate file IDs.
- Preview Ghosts are issued with exactly one `PreviewMaterial` draw call per mesh. The material is translucent fill only; preview objects have no Renderer component material arrays, Outline/Highlight component, edge renderer, or emission.
- Quest runtime wrist-follow acceptance remains device-unverified. The authored Scene Preview alignment is complete; perform the four requested wrist-rotation tests on Quest before release acceptance.
- Integration request: none.

## Ghost visual policy (2026-08-24)

Did not change Release-to-Equip architecture, sequence, pair completion, XR Origin, cabinet, scale, reset, or glove/boot Ghost behavior.

`PPEPlacementGuideManager` now creates Ghost meshes only for pair members (InnerGloves, ProtectiveGloves, ChemicalBoots). ProtectiveSuit, Respirator, FaceProtection, and Helmet never instantiate a Ghost renderer. Their dynamic Face / Torso / Head targets remain live and invisible. Grab hint strings remain in code but the subtitle HUD is off (`PPEEquipFeedbackUI.showSubtitleHud`, `PPETTSManager.showSubtitles`) until a later wiring pass.

Release validation still uses `PPEReleaseEquip` distance-on-SelectExited against those targets. Scene distances: InnerGloves pair `0.12 m`, ProtectiveGloves pair `0.15 m`, ChemicalBoots pair `0.20 m`, Respirator `0.18 m`, FaceProtection `0.20 m`, ProtectiveSuit `0.30 m`. Helmet is not in `04_PPE`; if enabled later it uses Head target and `PPEEquipable.EquipDistance` (setup default `0.18 m`).

---

## Ghost visual policy (2026-08-24)

Did not change Release-to-Equip architecture, sequence, pair completion, XR Origin, cabinet, scale, reset, or glove/boot Ghost behavior.

`PPEPlacementGuideManager` now creates Ghost meshes only for pair members (InnerGloves, ProtectiveGloves, ChemicalBoots). ProtectiveSuit, Respirator, FaceProtection, and Helmet never instantiate a Ghost renderer. Their dynamic Face / Torso / Head targets remain live and invisible. Grab hint strings remain in code but the subtitle HUD is off (`PPEEquipFeedbackUI.showSubtitleHud`, `PPETTSManager.showSubtitles`) until a later wiring pass.

Release validation still uses `PPEReleaseEquip` distance-on-SelectExited against those targets. Scene distances: InnerGloves pair `0.12 m`, ProtectiveGloves pair `0.15 m`, ChemicalBoots pair `0.20 m`, Respirator `0.18 m`, FaceProtection `0.20 m`, ProtectiveSuit `0.30 m`. Helmet is not in `04_PPE`; if enabled later it uses Head target and `PPEEquipable.EquipDistance` (setup default `0.18 m`).

## Ghost pose / face-suit comfort / equipped-hand grab (2026-08-24)

Did not change sequence, Release-to-Equip distances, pair completion, cabinet, scale, reset, or XR Origin. Did not add missing worn visuals for Suit / Boots / Respirator / FaceProtection / InnerGloves.

### Ghost pose

1. **Final glove position/rotation source:** Ghost is parented to the live target palm/wrist (`PPEGlovePairMember.TargetTransform` / `PPEGloveHandTargetFollower`). World pose is `target.position + target.rotation * GhostPoseCalibration.localPosition` and `target.rotation * GhostPoseCalibration.localRotation`. That is the same resolved Palm/Wrist used by Release-to-Equip. Holding-hand rotation, HMD, body yaw, fixed world Euler, and manager-global glove Euler are not used for glove Ghost orientation. `ApplyGuideLocalPose` is reapplied every `LateUpdate` so Inspector calibration edits apply immediately in Play Mode.
2. **GhostPoseCalibration hierarchy:** child `GhostPoseCalibration` on each physical member (`LeftInnerGlovePhysical`, `RightInnerGlovePhysical`, `LeftProtectiveGlovePhysical`, `RightProtectiveGlovePhysical`, `LeftBootPhysical`, `RightBootPhysical`) and on Face/Suit `PPEEquipable` roots. Created by `EnsureGhostPoseCalibration()` in Awake/OnValidate, or **Tools/PPE/Ensure Ghost Pose Calibration**. New glove children seed local position `(0, 0.02, 0.03)` when the old offset was zero.
3. **Left/Right independently tunable:** each member has its own `GhostPoseCalibration`. No mirrored-axis assumption. Rotate the child in the Inspector until the Ghost sits on the mesh.
4. **Boot rotation source:** `PPELowerLegTargetFollower` / `PPEPlayerBodyReference.ComputeLowerLegWorldPose` — **body yaw only**. Ghost world rotation = body yaw * that boot's `GhostPoseCalibration.localRotation`. Looking down only toggles visibility (`bootGuideLookDownAngle`). No HMD pitch, no XR Hand rotation.
5. **Face/Suit pose calibration:** Ghost parented to Face or Torso target. World rotation = Face/Torso target rotation * that item's `GhostPoseCalibration.localRotation`. Extra visual offsets still apply on top of calibration (Face forward 0.15 m, Suit forward 0.12 m + down 0.08 m) and do not change Release-to-Equip.

Scene View: **Tools/PPE/Preview PPE Ghost Pose** (on by default) plus per-member/per-item gizmos. Rotate `GhostPoseCalibration` without Play Mode.

### Face/Suit comfort

6. **Ready/NotReady visual switching removed** for Respirator / FaceProtection / ProtectiveSuit. `ApplyGuideAppearance` for those items only calls `ApplyFaceSuitStaticGhostMaterials`. Gloves/boots still use Ready/NotReady.
7. **Static Ghost alpha:** one cached clone per live Face/Suit Ghost from the NotReady template at `faceSuitStableAlpha` **0.16** (range 0.08–0.35). No green/orange swap, no pulse, no emission, no per-frame material asset swap. Instance destroyed with the Ghost.
8. **Face proximity fade (visual only):** full/stable beyond `faceGhostFadeStartDistance` **0.35 m**; fade 0.35→`faceGhostHiddenDistance` **0.22 m**; hidden closer than 0.22 m. `faceGhostFullVisibleDistance` 0.45 m is documented; fade start is the stable threshold. Release-to-Equip distances unchanged.
9. **Suit HMD overlap:** Ghost stays on dynamic torso + forward/down visual offset. If HMD is inside Ghost renderer bounds, alpha scales to **0.15** of the stable value (~0.024), otherwise fades by nearest bounds distance. Does not fill the HMD with a full-size flashing suit.
10. **Flashing remaining in code:** Face/Suit no longer swap Ready/NotReady materials. `SetGuideState` still updates an internal Ready flag for equip/dwell logic, but appearance for Face/Suit is static + proximity fade only. InnerGloves/ProtectiveGloves/Boots still swap Ready/NotReady (not in front of the HMD).

Instruction text (no Ready color): Respirator `호흡보호구를 얼굴 앞으로 가져간 뒤 놓으세요.` FaceProtection `안면보호구를 얼굴 앞으로 가져간 뒤 놓으세요.` Suit `보호복을 몸 쪽으로 가져간 뒤 놓으세요.` Face/Suit hints activate the existing feedback UI.

### Second glove

11. **Authoritative Left/Right interaction references:** `PPEHandTargetResolver.ResolveControllerHand` caches Left/Right from the active XR Origin **Left/Right Controller** → **NearFar attach** (then Direct interactor, then controller). Wearable visuals are rejected via `PPEWearableVisualOnly`.
12. **Active `Hand` visual search removed** from interaction resolution. No `FindObjectsByType<Hand>()` / `FindActiveHandVisual` in PPE scripts.
13. **Left interactor after Left glove equipped (code contract, not device-verified):** bare-hand GameObject stays active; **Renderer off**; chemical glove visual on; `PPEWearableVisualOnly` strips wearable colliders / XR interactable / XR interactor and sets **Ignore Raycast**. NearFar / Direct / Select / Attach / tracking are not disabled (`disableRayInteractorsWhileWorn` remains false).
14. **RightProtectiveGlovePhysical after left equipped (code contract):** pair completion deactivates only the equipped **physical member**. Remaining member stays active; `PPEHandGrabbable`, grab collider, NearGrabAssist, and `PPEDirectGrabProximityFilter` are not disabled. Grab distance was not increased (`maxDirectGrabDistance` default 0.06 m).
15. **Hover result:** **UNVERIFIED** on Quest / Play Mode. Development log `[ProtectiveGlove Second-Side Grab]` fires on change after one ChemicalGlove side is equipped (`UNITY_EDITOR || DEVELOPMENT_BUILD`).
16. **Proximity filter result:** logged as `FilterAllows` against the equipped-side interactor and remaining grab. Not device-verified.
17. **Exact first failure if Grab still fails:** unknown until the diagnostic runs on device. Use the log fields: interactor Active/Enabled, remaining Active/Grabbable/Collider/Filter, Hovering, SelectCandidate (remaining name if hovering it, otherwise whatever the equipped interactor is hovering), DistanceToCollider, FilterAllows.

### Inspector / Scene

- Run **Tools/PPE/Ensure Ghost Pose Calibration** in `04_PPE`, then rotate each `GhostPoseCalibration` child.
- **Tools/PPE/Preview PPE Ghost Pose** draws the calibration axes in Scene View.
- Do not edit `01_App.unity`. Wearable strip is runtime in `ChemicalGlovesWearableVisual`.

### Verification

Compiler / Quest / Play Mode not run here. **Do not treat second-side Hover/Grab as complete.** DEVICE TEST REQUIRED:

- Right hand holds Left InnerGlove / ProtectiveGlove; rotate actual left wrist → Ghost follows that palm * authored Left calibration.
- Left boot: look down (visible), turn body 90° → Ghost yaws 90°. Head pitch does not tilt the boot.
- Respirator / FaceProtection: one stable translucent Ghost, no orange/green, fades out near the HMD. Release near face still equips.
- Suit: one stable low-alpha Ghost below the HMD, no Ready color, no full-screen flash.
- Equip Left ProtectiveGlove, then with the visually gloved left hand hover/grab RightProtectiveGlovePhysical. Read `[ProtectiveGlove Second-Side Grab]` if Hover is false.

### Integration request

None for this pass. `01_App` XR Origin / interactors were not modified.

---

## Phase 3 — Scene integrity + ChemicalGloves visual ownership (2026-08-24)

Did not revisit Phase 1 / 2 / 2.5 systems except where this pass required it. Did not create missing worn visuals for Suit / Boots / Respirator / FaceProtection / InnerGloves.

### 1. Duplicate ChemicalSuit

Both live `PPEEquipable` instances were under `PreparationRoom / SafetyLocker / SuitSlot`, source FBX `Assets/1.JSR/Models/PPE_ChemicalSuit/PPE_ChemicalSuit_01.fbx`, local scale `0.72` on slot scale `2.15` (world height ~1.548 m).

| Instance | Hierarchy | Local pos | Notes |
|---|---|---|---|
| `PPE_ChemicalSuit_01 (1)` | SuitSlot first child | `(-0.102, 0.018, 0)` | leftover duplicate ~13 cm off hanger center |
| `PPE_ChemicalSuit_01 (2)` | SuitSlot second child | `(0.029, 0.011, 0)` | closer to slot origin; retained |

Retained `(2)` (PrefabInstance `&1656745454`, GO `&1656745456`). Deleted `(1)` (PrefabInstance `&165692668`) from `04_PPE.unity`. SuitSlot now has one child. Checklist `itemId: ChemicalSuit` row kept.

Remaining live interactive ChemicalSuit count: **1**. Still has `PPEEquipable`, `PPEHandGrabbable`, `PPEStoredDisplayState`, Release-to-Equip (static `PPEReleaseEquip`), Body/Torso slot, Ghost via placement fallback, `ResetToStoredPose()`. Scale unchanged.

### 2. ChemicalGloves visual authority

During PPE donning, `PPEGlovePairCoordinator.ChemicalGlovesInstance` `LeftEquipped` / `RightEquipped` is the only visual authority.

- **OnEnable before:** `ShowWearable()` enabled **both** gloves.
- **OnEnable after:** `ApplyAuthoritativeVisual()` restores pair sides, or logical both-equipped outside pair context. Never both-on just because the host activated.
- **ApplyPpeRoomState:** pair flags **replace** caller args (no OR with App).
- **OnDisable:** skips `HideWearable()` while the pair coordinator exists, so Reset host toggles cannot hide both bare hands.
- **Left only:** left chemical ON, right chemical OFF, left bare OFF, right bare ON.
- **Right only:** inverse.
- **Both:** both chemical ON, both bare OFF, logical `ChemicalGloves` Equip once.
- **Reset:** pair flags false → both chemical OFF, both bare ON. Double Reset safe.

### 3. PPEStateController (Integration-owned)

`Assets/Scripts/App/PPEStateController.cs` — smallest scoped change: `OnPpeEquipmentChanged` returns while `CurrentStage == PPE` and `ChemicalGlovesInstance != null`. App logical both-or-nothing equipped flag cannot overwrite a single worn side. After Donning both sides, pair is still `true,true`; no extra visual write, no flicker.

### 4. Runtime debug visuals

Found: InnerGlove ready enter/exit spheres, InnerGlove debug overlay (`INNER GLOVE DEBUG`), placement HMD overlay, dynamic body target gizmos (Editor), placement change logs.

Disabled by default in `04_PPE`:

- `showEquipDebugVisuals: 0`
- `showPlacementDebugOverlay: 0` (script default; overlay also requires equip debug)
- `drawDynamicTargetGizmos: 0`
- `logPlacementDebugOnChange: 0` (was 1)
- InnerGlove `showDevelopmentDebug` default false

Editor Gizmos kept. Runtime geometry visualizers only when Editor/Development **and** explicitly enabled.

### 5. Ghost materials

`renderer.materials` on every Ready/NotReady cloned ghost materials. Now: shared `PPE_GuideNotReady` / `PPE_GuideReady` for non-InnerGlove ghosts. InnerGlove Cull Off / alpha uses **cached** clones per active Ghost. HideGuides / OnDisable destroys Ghost roots **and** cached instances. No per-frame SkinnedMesh `BakeMesh` (ProtectiveGloves Ghost still uses shared bind-pose mesh; silhouette may be slightly imperfect — UX limitation).

### 6. Missing worn visuals (known UX gap, not this phase)

Physical hides on Equip, logical state updates, **no worn visual:** InnerGloves, ProtectiveSuit, ChemicalBoots, Respirator, FaceProtection. ProtectiveGloves worn visual exists and is now side-correct.

### 7. DEVICE TEST REQUIRED

A Suit grab → Ghost → torso Equip once, no duplicate in cabinet, Reset returns the same suit. B Left ProtectiveGlove only visuals. C Right side then logical complete once, no App flicker. D Reset after one outer glove. E Normal Play: no circle/sphere/zone/debug text/gizmo, only PPE Ghost. F Repeated grab/release: one Ghost, no object/material growth.

Compiler / Quest / Play Mode not run here.

### Integration request

`PPEStateController` was edited under this Phase 3 scoped exception. No other App / XR / sequence / cabinet / scale changes.

## Phase 2.5 — Integration & reset completion (2026-08-24)

Did not start wearable visual / duplicate Suit cleanup.

### Retry Integration

App Retry entry points:

- Mid-PPE / same-stage: `ScenarioFlowManager.RestartCurrentStage()` now calls `PPESequenceManager.ResetPpeRoomTraining()` when `CurrentStage` and `RestartTarget` are both PPE (in-place, no scene reload). Guarded by `resetInProgress` so overlapping calls no-op.
- PPE-only Repeat while still in PPE: `RepeatSelectedTraining()` routes to `RestartCurrentStage()`.
- Complete-menu "동일 훈련 다시 하기" after leaving PPE: still uses existing scene reload (`StartTraining` → `ShowStage`). After bind, `StageLifecycleAdapter.BindStage` calls `ResetPpeRoomTraining()` once so the full PPE API still runs.

Reset-while-grabbing uses the existing Phase 2 order: suppress Release-to-Equip → cancel XRI select/hover → restore Stored.

### WasteContainer

`WasteContainerPrepareInteractable.ResetPreparedState()` restores Prepared=false, authored active/pose, interactable enabled, and selection/highlight. Called from `PPEEquipmentManager.ResetAllEquipment()` (used by `ResetPpeRoomTraining()`). Not added to `ResetSequence()`. Not wearable PPE.

### ResponseBag

`ResetResponseBag()` already clears prepared/equipped, inspected contents, pose, grab, and wearable visual. `ResetSequence()` clears `bagContentsChecked` (Y inspect). No ResponseBag redesign.

Remaining design (unchanged): Y inspect and Grip confirm still return while `PPETTSManager.IsBusy` (`ResponseEquipmentManager.OnSharedInputPerformed`, `PPESequenceManager.NotifyBagContentsChecked`, `XRInteractionConfirmRouter.RouteConfirm`). This is bag/waste confirm, not wearable grab.

### TTS

`StageLifecycleAdapter` no longer ORs PPE instructional `PPETTSManager.IsBusy` into `shouldBlock`. Bind no longer disables interactables because VO is playing.

Still blocks: App menu pause (`menuPaused`); LeakResponse arrival/narration; scene transition input lock. Wearable grab/release-to-equip is not `SEQUENCE_BLOCKED` by audio (`CanEquip` was already Phase 1).

### Throw serialization

`04_PPE.unity`: all 10 `PPEHandGrabbable` `m_ThrowOnDetach` / `m_ForceGravityOnDetach` set to 0. Editor `PPEHandGrabSetup` and `PPEPairPhysicalAuthoringSetup` now author false. Runtime `ApplyWearableThrowPolicy()` remains as defense.

### Tests

Retry, WasteContainer restore, grab during InnerGloves VO, throw warning: **CODE VERIFIED**, **DEVICE TEST REQUIRED**.

## Phase 2 — PPE Room stability (2026-08-24)


Did not revisit Phase 1. Invalid release still does **not** teleport PPE to the cabinet.

### State model

`PPEStoredDisplayState.StorageState`: **Stored / Held / Released / Equipped**.

- **Stored:** authored parent/pose/scale, kinematic, no gravity, `throwOnDetach = false`, grab enabled.
- **Held:** XRI grab. Stored pose is restored only when leaving Stored (not when re-grabbing a dropped item).
- **Released:** failed Release-to-Equip. Non-kinematic, gravity on, no throw velocity, object stays in the world.
- **Equipped:** kinematic, no gravity, then existing hide/deactivate. Never routed through Released.

### Failed release

`PPEHandGrabbable.OnSelectExited` → Release-to-Equip. On failure, after XRI `OnSelectExited`, `NotifyInvalidReleaseIfHeld()` applies Released physics. No shelf teleport.

### Throw

Runtime `ApplyWearableThrowPolicy()` on all cabinet wearables (InnerGloves, ProtectiveGloves, ChemicalBoots, ProtectiveSuit, Respirator, FaceProtection). `throwOnDetach = false`, `forceGravityOnDetach = false`. Editor setup tools still serialize `throwOnDetach = true`; runtime overrides. Common XR not modified.

### Pair reset

`PPEGlovePairCoordinator.ResetPair()`: `StopAllCoroutines`, clear Left/Right/logical flags, reactivate authored members, restore Stored pose, re-enable grab. Shared by InnerGloves, ChemicalBoots, ProtectiveGloves.

### Full PPE Room reset

**Entry:** `PPESequenceManager.ResetPpeRoomTraining()` (Context Menu: Reset PPE Room Training).

`ResetSequence()` is still sequence-only (phase, index, completed steps, bag-checked). It does **not** move physical PPE.

**Order:**

1. `PPEHandGrabbable.SuppressReleaseEquip = true`
2. Cancel XRI select/hover on wearable grabs
3. `PPESelectionManager.ClearSelection()`
4. Stop placement coroutines + `HideActiveGuides()` (destroys Ghosts, clears held/target/Ready)
5. `PPEEquipmentManager.ResetAllEquipment()` (logical IDs, reactivate `PPEEquipable` roots, `ResetResponseBag()`)
6. All `ResetPair()`
7. `RestoreToStoredPose()` on remaining stored wearables (skip coordinator roots)
8. `ResetSequence()` (checklist via required-step / equipment state events)
9. `SuppressReleaseEquip = false`

Reset-during-grab: cancel selection first; `OnSelectExited` does not Equip and does not apply Released physics; then Stored pose is restored. Static `SuppressReleaseEquip` is cleared in `finally` and in `OnDestroy`.

### Response equipment

- **ResponseBag:** `ResetResponseBag()` is called from `ResetAllEquipment()`.
- **WasteContainer:** **no visual reset API**. Logical `preparedItemIds` is cleared. If `deactivateOnPrepare` hid the container, it may stay inactive. Do not add new waste interaction in this phase.

### Tests

All acceptance tests A–H: **CODE VERIFIED**, **DEVICE TEST REQUIRED**. Compiler not run here. Quest/Play Mode not run.

### Integration request

No App Retry UI calls `ResetPpeRoomTraining()`. `StageLifecycleAdapter` does not reset PPE. If App retry should restore the PPE Room, call `PPESequenceManager.ResetPpeRoomTraining()` (or `Resolve()?.ResetPpeRoomTraining()`).

- **Stored:** authored parent/pose/scale, kinematic, no gravity, `throwOnDetach = false`, grab enabled.
- **Held:** XRI grab. Stored pose is restored only when leaving Stored (not when re-grabbing a dropped item).
- **Released:** failed Release-to-Equip. Non-kinematic, gravity on, no throw velocity, object stays in the world.
- **Equipped:** kinematic, no gravity, then existing hide/deactivate. Never routed through Released.

### Failed release

`PPEHandGrabbable.OnSelectExited` → Release-to-Equip. On failure, after XRI `OnSelectExited`, `NotifyInvalidReleaseIfHeld()` applies Released physics. No shelf teleport.

### Throw

Runtime `ApplyWearableThrowPolicy()` on all cabinet wearables (InnerGloves, ProtectiveGloves, ChemicalBoots, ProtectiveSuit, Respirator, FaceProtection). `throwOnDetach = false`, `forceGravityOnDetach = false`. Editor setup tools still serialize `throwOnDetach = true`; runtime overrides. Common XR not modified.

### Pair reset

`PPEGlovePairCoordinator.ResetPair()`: `StopAllCoroutines`, clear Left/Right/logical flags, reactivate authored members, restore Stored pose, re-enable grab. Shared by InnerGloves, ChemicalBoots, ProtectiveGloves.

### Full PPE Room reset

**Entry:** `PPESequenceManager.ResetPpeRoomTraining()` (Context Menu: Reset PPE Room Training).

`ResetSequence()` is still sequence-only (phase, index, completed steps, bag-checked). It does **not** move physical PPE.

**Order:**

1. `PPEHandGrabbable.SuppressReleaseEquip = true`
2. Cancel XRI select/hover on wearable grabs
3. `PPESelectionManager.ClearSelection()`
4. Stop placement coroutines + `HideActiveGuides()` (destroys Ghosts, clears held/target/Ready)
5. `PPEEquipmentManager.ResetAllEquipment()` (logical IDs, reactivate `PPEEquipable` roots, `ResetResponseBag()`)
6. All `ResetPair()`
7. `RestoreToStoredPose()` on remaining stored wearables (skip coordinator roots)
8. `ResetSequence()` (checklist via required-step / equipment state events)
9. `SuppressReleaseEquip = false`

Reset-during-grab: cancel selection first; `OnSelectExited` does not Equip and does not apply Released physics; then Stored pose is restored. Static `SuppressReleaseEquip` is cleared in `finally` and in `OnDestroy`.

### Response equipment

- **ResponseBag:** `ResetResponseBag()` is called from `ResetAllEquipment()`.
- **WasteContainer:** **no visual reset API**. Logical `preparedItemIds` is cleared. If `deactivateOnPrepare` hid the container, it may stay inactive. Do not add new waste interaction in this phase.

### Tests

All acceptance tests A–H: **CODE VERIFIED**, **DEVICE TEST REQUIRED**. Compiler not run here. Quest/Play Mode not run.

### Integration request

No App Retry UI calls `ResetPpeRoomTraining()`. `StageLifecycleAdapter` does not reset PPE. If App retry should restore the PPE Room, call `PPESequenceManager.ResetPpeRoomTraining()` (or `Resolve()?.ResetPpeRoomTraining()`).

## Phase 1 stability (2026-08-24)

Stopped feature work. Fixed three blocking architecture issues.

### 1. Legacy Ray+Grip wearable Equip

- `XRInteractionConfirmRouter.allowLegacyWearableEquipFallback` default is **false**.
- `04_PPE.unity` serialized value is **0**.
- Ray hover/sticky select can remain. Grip no longer Equips wearable PPE through the router.
- Defensive guard: `PPEEquipmentManager.Equip` refuses a `PPEGlovePairCoordinator` logical root unless `BothEquipped`. Also checked in `PPESelectionManager.TryEquipCurrentSelection`.
- Pair coordinator still Equips the logical item after both physical sides.

### 2. TTS IsBusy no longer blocks Equip

Removed `PPETTSManager.IsBusy` from:

- `PPESequenceManager.CanEquip`
- `PPESequenceManager.CompleteEquip`

Sequence order gating is unchanged. Current required item can Equip while VO plays. Wrong-order Equip still fails.

Left unchanged (ResponseBag / feedback, not wearable Equip):

- `NotifyBagContentsChecked` still ignores Y while TTS is busy
- `XRInteractionConfirmRouter.RouteConfirm` still returns while TTS is busy (bag/waste Grip)

### 3. One active XR Origin

New `PPEActiveXrOrigin` in `Assets/1.JSR/Scripts/PPEActiveXrOrigin.cs`.

Callers:

- `PPEPlayerBodyReference.ResolveTrackingSource`
- `PPEXrHandsJointPose.ResolveOriginTransform`
- `PPEHandTargetResolver.FindActiveOrigin` / `IsUnderActiveOrigin`
- `PPEPreviewController.CacheCameraAndAnchor`

Not migrated (not Ghost/release targets): `ResponseBagGearPanel`, `CameraGazeSelectDriver`, Editor menus.

Cache invalidates when the Origin is disabled, inactive, or destroyed.

### Integration request — StageLifecycleAdapter

Did **not** edit `Assets/Scripts/App/StageLifecycleAdapter.cs` (App / Integration owned).

`LateUpdate` still does `shouldBlock |= PPETTSManager.Instance.IsBusy` for `TrainingStage.PPE`, then disables stage interactables. That can still force-drop a held PPE while instructional VO plays.

Requested PPE-stage exception: do not include instructional TTS busy in PPE grab/release blocking. Menu pause can still block.

### Tests

- Ray+Grip pair Equip: **CODE VERIFIED**, DEVICE TEST REQUIRED
- Equip during InnerGloves VO: **CODE VERIFIED**, DEVICE TEST REQUIRED
- Out-of-order Suit while InnerGloves required: **CODE VERIFIED** (CanEquip still requires current step)
- App flow Origin: DEVICE TEST REQUIRED (expect Common XR Origin; log `[PPE XR Origin] Active = ...`)
- Standalone `04_PPE`: DEVICE TEST REQUIRED (expect PPE local Origin)
- Compiler: not run here (no Unity/terminal)

## Wearable PPE Ghost visibility (2026-08-24)


InnerGlove Ghost already appeared on the tracked hand. Suit / Boots / Respirator / FaceProtection Ghosts did not.

### Why only glove guides appeared

Glove targets live under runtime `PPE_HandTargets` in the PPE scene, so they stay active after App disables the PPE-scene XR Origin.

`PPE_PlayerBodyReference` was parented under that PPE-scene `XR Origin (VR)`. When App turns that origin off:

1. Grab still fires `PPEHandGrabbable.OnGrabbed` → `PPEPlacementGuideManager.ShowGuides`. Release-to-equip did **not** skip this.
2. Guide creation runs and clones the held PPE mesh.
3. The guide is parented to Face / Body / Foot anchors (or boot lower-leg followers parented to the body reference).
4. Those anchors sit under the inactive origin, so `activeInHierarchy` is false. The Ghost exists but is not rendered.
5. `LateUpdate` on the body reference also stops, so targets do not follow the current HMD.
6. `HasLiveTarget()` then reports false (`HasTracking` needs an active camera) and `EvaluateHeldPlacement` hid the already-created Ghost as `TRACKING LOST`.
7. Boots also require look-down, and `IsLookingDown` is false without tracking, so the Boot Ghost stayed inactive.

Face / Torso / LowerLeg were already empty Transforms. Missing Ghosts were the PPE clones, not missing body meshes.

### Fix

- Runtime host `PPE_BodyTargets` in the PPE scene (not under XR Origin, not under Common XR).
- If the PPE origin is inactive, `PPEPlayerBodyReference.EnsureActiveInPpeScene()` reparents onto that host and follows the **active** App XR Origin / HMD.
- Face / Torso / LowerLeg stay empty Transforms. No torso/leg/face meshes, debug spheres, or target circles were added.
- Suit / Respirator / FaceProtection Ghosts use the same semi-transparent clone path as gloves (`PPE_GuideNotReady`, cull off, visible alpha). Shown immediately on grab. Distance only switches NotReady → Ready.
- Boot Ghosts still require look-down (`HMD forward.y <= -sin(30°)`). Targets keep following while hidden.
- Guides are created once per grab, not every frame.
- InnerGloves / ProtectiveGloves tracked-hand path is unchanged.

### Per-item (code)

| Item | Grab received | Guide created | Source renderer | Guide GO | Renderer enabled | Target | TargetValid after fix | Parent | Visible on grab |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| ProtectiveSuit | yes `OnGrabbed` | yes `ShowGuides` | suit visual clone | yes | yes | Dynamic Torso (`Body`) | body `HasTracking` | Body anchor | yes, immediately |
| ChemicalBoot Left | yes pair member | yes `ShowGloveMemberGuides` | left boot visual | yes | yes | Dynamic Left LowerLeg | lower-leg follower | `LeftBootEquipTarget` | only when looking down |
| ChemicalBoot Right | yes | yes | right boot visual | yes | yes | Dynamic Right LowerLeg | lower-leg follower | `RightBootEquipTarget` | only when looking down |
| Respirator | yes | yes | respirator visual | yes | yes | current HMD Face (respirator offset) | body `HasTracking` | Face anchor | yes, immediately |
| FaceProtection | yes | yes | face-shield visual | yes | yes | current Face/Head (face-protection offset) | body `HasTracking` | Face anchor | yes, immediately |
| ProtectiveGloves | yes | yes | unchanged | yes | yes | tracked hand | hand follower | `PPE_HandTargets` | unchanged |

Quest still required to confirm the Ghost is visible on device.

## Release-to-equip + debug visuals (2026-08-21)

Visible flashing circle was `PPEInnerGloveDonning.AddDebugSphere` on `Left/RightInnerGloveEquipZone` (`showDevelopmentDebug` defaulted true) plus `PPEPlacementGuideManager` enter/exit debug spheres and HMD overlay (`showPlacementDebugOverlay: 1`).

Normal Play: `Show Equip Debug Visuals` (`showEquipDebugVisuals`) defaults **false**. Production player builds never show it. Logical targets and distance validation remain.

InnerGloves release path was **not** redesigned. Other wearable PPE now use `PPEReleaseEquip` on natural `SelectExited` against the same Ghost target. No dwell. ResponseBag / WasteContainer / Helmet unchanged.

Release distances: InnerGloves 0.15 m (unchanged), ProtectiveGloves 0.15 m, Boots 0.20 m, Suit 0.30 m, Respirator 0.18 m, FaceProtection 0.20 m. Rotation off for these.

Quest still required to confirm `[PPE ReleaseEquip] Result=EQUIPPED` / `EQUIP_LEFT` / `EQUIP_RIGHT` per item.

## InnerGloves TargetValid / ControllerHand (2026-08-21)

Runtime failure:

`Member=Left CapturedHolding=Right Target=Left TargetValid=False Distance=inf Result=NO_VALID_TARGET_POSE`

Cause: Stage XR disables Hand Visualizer and `VR Hand Pose Tool Tracking`, and shows `PPE_DefaultHand_Left/Right` under Left/Right Controller. XRHandSubsystem Palm was not tracked. Followers were also parented under the PPE-scene XR Origin, so App disable of that origin stopped LateUpdate and cleared tracking.

Fix (InnerGloves / glove followers only):

- `PPEHandTargetResolver` is the single controller-hand source. Prefers the active `Hand` visual (`PPE_DefaultHand_*`), then NearFar attach, then the active Left/Right Controller on the **active** XR Origin.
- Followers live under runtime `PPE_HandTargets` (PPE scene), not under XR Origin.
- AutoActive: XRHands Palm if `isTracked` + TryGetPose; else ControllerHand. Grab session keeps the chosen source until it becomes invalid.
- Ghost, Ready, release `TargetValid`, and distance all read `LeftGloveEquipTarget` / `RightGloveEquipTarget`.
- Release log includes `TargetSource=ControllerHand|XRHandsPalm|None`.
- Distance still 0.15 m. No dwell. No other PPE changes.

Quest still required: Right grab Left InnerGlove, release beside visible Left hand → `TargetValid=True` finite Distance `EQUIP_LEFT`.

## InnerGloves AutoActive hand target (2026-08-21)

Failing Quest/controller test: `TargetValid=False Distance=inf Result=NO_VALID_TARGET_POSE` because `PPEGloveHandTargetFollower` was XRHands-only while Stage XR showed `PPE_DefaultHand_Left/Right` and disabled Hand Visualizer.

Follower default is now `AutoActive`: XRHands Palm if tracked, else active controller virtual hand (`PPE_DefaultHand_*`, then NearFar attach, then Left/Right Controller). Ghost, Ready, and release all use the follower transform. One log: `[PPE Hand Source]` then `[InnerGlove Release] ... TargetSource=ControllerHand|XRHandsPalm`.

Release distance still 0.15 m. No dwell.

## InnerGloves one-path release (2026-08-21)

Equip is no longer Ready/dwell/zone. One path:

1. `SelectEntered` stores `GrabSessionHoldingHand` and logs `[InnerGlove Grab]`.
2. Natural `SelectExited` calls `TryEquipInnerGloveOnRelease` **before** clearing that hand.
3. Distance: EquipReference ↔ XR Palm, default **0.15 m**. Last valid palm **0.30 s**. No rotation. No extra Select cancel.
4. Valid: `NotifyInnerGloveSideFromRelease` sets Left/RightEquipped, hides the physical member **next frame**. Both sides still `Equip(InnerGloves)` once.

One Console line: `[InnerGlove Release] Member=Left CapturedHolding=Right ... Result=EQUIP_LEFT|TOO_FAR|WRONG_HOLDING_HAND|NO_VALID_TARGET_POSE`

Dev-only `debugLooseReleaseDistance` (0.50 m) defaults OFF.

## InnerGloves release-to-equip (2026-08-21)

InnerGloves no longer auto-equip after 0.20s dwell. Ghost Ready is guidance only.

Equip is evaluated once on natural `PPEHandGrabbable.OnSelectExited` → `OnReleased`:

- opposite holding hand
- XR Hands Palm valid or last pose within 0.20 s
- EquipReference to Palm <= **0.14 m**
- sequence allows InnerGloves

Then `NotifySideEquipped(..., selectionAlreadyExited: true)` — no extra XRI Select cancel. Far / wrong-hand release is a normal drop. First side stays on InnerGloves; both sides still call `PPEEquipmentManager.Equip(InnerGloves)` once.

Hint: `오른손에 가져간 뒤 놓으면 착용됩니다.` Wrong hand: `반대 손으로 장갑을 잡아주세요.`

Release log: `[PPE InnerGlove Release]` Result EQUIPPED / RELEASED_TOO_FAR / WRONG_HAND.

Do not apply this pattern to ProtectiveGloves, Boots, Suit, Respirator, FaceProtection until InnerGloves pass on Quest.

## InnerGloves placement Console first line (2026-08-21)

`[PPE InnerGlove Placement]` is now a single Console list line:

`Right | Tracking=True | Dist=0.084/0.130 | Inside=True | Sequence=True | State=READY | Dwell=0.12/0.20 | FirstFalse=None`

Logged only on State/InsideZone change, Ready start, or Equip attempt. Equip attempt also logs `[PPE InnerGlove Equip]`.

## InnerGloves runtime log and throw warning (2026-08-21)

InnerGlove SelectEntered now emits one `[PPE InnerGlove Grab]` log (Interactor path, handedness, ResolvedHoldingHand, WrongHand, Guide). Valid grabs then emit `[PPE InnerGlove Placement]` only when State or InsideZone changes.

Correct InnerGlove hint: `오른손에 가져가 착용하세요.` / `왼손에 가져가 착용하세요.`
Wrong-hand: `오른손 장갑은 왼손으로 잡아 착용하세요.`

InnerGlove physical members stay kinematic. `throwOnDetach` and `forceGravityOnDetach` are forced false so XRI does not write velocity on detach.

Quest still required to confirm HoldingHand and whether the throw warning is gone.

## InnerGloves holding-hand detection (2026-08-21)

Old `PPEHandGrabbable.ResolveHand` used `interactor.handedness`, then walked every parent name for `"Left"` before `"Right"`. That can classify a Right-hand grab as Left because `LeftInnerGlovePhysical` (or `Left Hand Tracking`) appears in the path.

New `PPEGrabHandResolver` priority:

1. XR Hands pinch near the grabbed object
2. Closer XR Palm to the object when that disagrees with interactor handedness
3. `Interactor.handedness` when Left/Right
4. `XRHandTrackingEvents` on the interactor rig
5. Closest `XRNode` LeftHand/RightHand device
6. Interactor GameObject own name `Left*` / `Right*` only
7. `Unknown` — no full parent-name walk

Same-side grab still holds the object and shows opposite-hand feedback. Unknown does not silently become Left.

## InnerGloves hand-zone donning (2026-08-21)

Authoritative InnerGloves equip is `PPEInnerGloveDonning`, not `PPEPlacementGuideManager` placement result.

- Zones `LeftInnerGloveEquipZone` / `RightInnerGloveEquipZone` parented to existing XR Palm followers. Trigger spheres, no physics push.
- Enter **0.13 m**, exit **0.16 m**, dwell **0.20 s**, tracking grace **0.30 s**.
- Completes through existing `NotifySideEquipped`. First side hides only that physical member. Both sides call `PPEEquipmentManager.Equip(InnerGloves)` once.
- Scene `EquipReference` children on Left/Right InnerGlovePhysical (palm/opening). Tune in Hierarchy before Play.
- Development: hand zone sphere + HMD overlay. Force Complete still uses the same CompleteSide path.

Quest runtime acceptance is still required. Do not treat as complete from code inspection.

## Kinematic stored-pose velocity (2026-08-21)

`PPEStoredDisplayState.HoldStoredPose` no longer writes `linearVelocity` / `angularVelocity` on kinematic Rigidbodies. Stored PPE stays kinematic; pose is still held by transform restore.

## Left InnerGlove vertical slice (2026-08-21)

### First failing state (code)

Ready dwell set `equipAttemptedForCandidate = true` **before** `TryCompletePlacement` succeeded. If the target hand was occluded on that frame (`IsTargetTracking == false`), CompleteSide failed once and never retried. Hand-over-hand occlusion on Quest made this the typical miss.

Secondary: EquipReference sat at mesh AABB center (toward fingers), not the opening/palm.

### InnerGloves-only fixes

- Retry CompleteSide until success. Do not consume the dwell attempt on failure.
- Tracking Loss Grace Time **0.30 s**; last Palm pose held; Ready/dwell continue during grace; after grace Ghost hides and Equip is impossible.
- Ready enter **0.12 m**, exit **0.15 m** (InnerGloves only).
- Rotation remains off.
- EquipReference at glove opening/palm (~18% from wrist along mesh +Z).
- First side: cancel XRI select/hover → disable grab → LeftEquipped → hide LeftInnerGlovePhysical only. Logical Equip only when both sides are true.
- Scene `PPE_InnerGloves.logicalItem` wired to InnerGloves `PPEEquipable`.
- VR world-space debug panel (no OnGUI). Development debug spheres 0.12 / 0.15 m. Menu: `Tools/HCl Training/PPE/Debug Force Complete Left InnerGlove Member`.

Quest headset acceptance test is still required. Do not treat this as shipped until LeftEquipped + Left inactive + Right still on shelf + sequence InnerGloves is observed in VR.

## XR Hands tracking (no automatic controller fallback) (2026-08-21)

`PPEGloveHandTargetFollower` with Tracking Source = **XRHands** (production default):

- Valid XR Hands pose → use actual Palm/Wrist.
- Brief miss → keep last valid pose for **Tracking Loss Grace Time = 0.15 s**. Ghost stays. One-frame miss does not flicker off.
- Still lost after grace → `TargetValid = false`, Ghost hidden, placement false, auto Equip impossible.
- Tracking restored → immediately resume XR Hands Palm/Wrist.
- Controller pose is **never** used automatically. `ControllerFallback` remains a manual Inspector option for editor/dev only.

## InnerGlove Ghost visibility (2026-08-21)

### Why the Ghost was not visible

1. **Tracking hide:** `EvaluateHeldPlacement` called `SetActiveGuidesVisible(false)` whenever the target XR Hand was not `IsTracking`. XR Hands can miss a frame or the empty target hand; the Ghost was created then immediately hidden.
2. **Inside the hand + faint material:** Ghost was parented at Palm local 0 with `PPE_GuideNotReady` alpha **0.24** and backface cull. It sat inside the tracked hand mesh.
3. **No grab guidance text:** `PPE_EquipFeedbackCanvas` is scene-inactive (TTS mute). Wrong-hand grabs failed silently.

Side assignment was already correct: `LeftInnerGlovePhysical` = Left + `InnerGlove_Left.obj`; `RightInnerGlovePhysical` = Right + `InnerGlove_Right.obj`.

### Fix (InnerGloves / hand PPE only)

- Grab-once `[PPE Glove Grab]` diagnostic log.
- Ghost offset **(0, 0.02, 0.03)** palm-local, Inspector `Glove Guide Position/Rotation Offset`.
- Glove Ghost uses a material instance: Cull Off, NotReady alpha ≥ 0.55, Ready ≥ 0.72.
- Distance still 0.12 m, rotation off, auto Equip after 0.20 s. Sequence gated separately (`SEQUENCE BLOCKED`).
- Wrong hand: no Equip; toast **"반대 손으로 장갑을 잡아주세요."** (feedback canvas is activated only for these glove hints).
- Debug overlay: `showPlacementDebugOverlay` on. Visible in Editor / Development Build only, world-space in front of the HMD. Turn off before a shipping build.

### Verify

Right grabs Left InnerGlove → Left Ghost appears immediately on Left Hand. Move Left Hand → Ghost follows. Bring glove within 0.12 m → Ready → 0.20 s → Equip. Right grabs Right InnerGlove → no Equip + opposite-hand text.

## Placement validation so PPE actually equips (2026-08-21)

### Blocking condition (InnerGloves)

Distance was already valid (~0.06 m vs 0.11 m) when the glove was next to the left hand. **Angle failed**: generated glove mesh axes vs XR Palm often ~70–90°, while max angle was **40°**. Ready required `distance AND angle`.

Root pivot vs Palm also made distance noisier than a palm/opening point.

### After

| Item | Distance | Rotation | Hold |
| --- | --- | --- | --- |
| InnerGloves | **0.12 m** (`EquipReference` ↔ XR Palm) | **off** | 0.20 s |
| ProtectiveGloves | **0.12 m** | **off** | 0.20 s |
| ChemicalBoots | **0.18 m** (unchanged) | **off** | 0.20 s |
| ProtectiveSuit | **0.25 m** (unchanged) | **off** | 0.20 s |
| Respirator | **0.16 m** | **on**, 80° | 0.20 s |
| FaceProtection | **0.18 m** | **on**, 80° | 0.20 s |

Ready (green) as soon as placement is valid. Equip after 0.20 s dwell. Leaving the region resets the timer. Sequence `CanEquip` still gates Equip. Pair: first side hides only that physical piece; logical Equip when both sides complete.

`EquipReference` is created at runtime at the visual-mesh center (palm/opening), not the FBX origin. Ghost remains visual only.

Safe XR: `CancelInteractableSelection` + `CancelInteractableHover`, then disable grab, then `SetActive(false)`.

Development: change-only `[PPE Placement]` log on `PPE_PlacementGuideManager`. Inspector snapshot fields. Optional overlay (`showPlacementDebugOverlay`, off). Status: TRACKING INVALID / DISTANCE / ANGLE / WRONG HAND / SEQUENCE / READY.

### Verify

Right holds Left InnerGlove, bring next to Left Hand → Ready → 0.20 s → left piece equips. No twist. Walk away from cabinet still works. Right holds Right InnerGlove → WRONG HAND, no equip.

## Inactive feedback canvas coroutine (2026-08-21)

`PPE_EquipFeedbackCanvas` is still scene-muted (inactive). Grab hints called `PPEEquipFeedbackUI.ShowMessage` via `FindObjectsInactive.Include`, which started a coroutine on the inactive object.

Fix: skip hints when the feedback UI is inactive; `ShowMessage` returns if `!isActiveAndEnabled`. Canvas is **not** re-enabled. Re-enable `PPE_EquipFeedbackCanvas` later to restore grab-hint toasts.

## Dynamic player-following equip targets (2026-08-21)

### Why old Ghosts stayed behind

`PPEPlayerBodyReference` followed HMD X/Z under XR Origin but forced `localRotation = identity` (origin yaw only). Face / torso / feet were **body-local authored offsets**, not live HMD or XR Hands. After a physical turn or walk-then-look, the Ghost stayed in the original origin-forward pose.

Glove followers exist, but `PPEGloveHandTargetFollower.LateUpdate` returned without applying XR Hands pose (callback-only, and skipped `Dynamic` updates). Joint-to-world conversion also cached an inactive origin. Boot targets copied those same static foot anchors and added 0.38 m, so they stayed at the first body pose.

Guides are instantiated **once per grab** and parented to the live target Transform (not recreated every frame). Distance/ready uses that live Transform.

### Architecture

```text
Current HMD
  → planar X/Z (smoothed)
  → horizontal heading / body yaw (smoothed, no pitch/roll)
  → Body Center
       ├ Face / Head  ← HMD pose + item-specific local offset
       ├ Torso        ← HMD X/Z + body yaw + vertical/forward offset
       ├ Left/Right LowerLeg ← body center + spacing/forward/height
       └ Left/Right Hand gizmos ← XR Hands Palm (unsmoothed)

Gloves: PPEGloveHandTargetFollower copies XR Hands Palm/Wrist every LateUpdate
Boots:  PPELowerLegTargetFollower copies TryGetLowerLegWorldPose (body yaw, ground-aligned)
```

The player / XR Origin is never moved. Only the equip target follows the user.

### Follow mapping

| Item | Target |
| --- | --- |
| InnerGlove L/R | XR Hands Left/Right Palm (opposite-hand grab unchanged) |
| ProtectiveGlove L/R | XR Hands Left/Right Palm (pair logic unchanged) |
| Respirator | HMD Face (`respiratorPositionOffset` 0, −0.10, 0.13) |
| FaceProtection | HMD Face (`faceProtectionPositionOffset` 0, −0.02, 0.20) |
| ProtectiveSuit | Dynamic torso |
| ChemicalBoot L/R | Dynamic lower-leg; either hand may hold |

No wearable PPE uses the old fixed world LeftHand/RightHand/Body/Foot authored pose as its placement target.

### Boot look-down

Target always updates while held. Guide visibility only: HMD `forward.y <= -sin(30°)` (`bootGuideLookDownAngle` 30, range 25–35 on Quest). Hidden when looking forward. Orientation is body yaw, not camera pitch.

### Inspector / Quest tuning (`PPE_PlayerBodyReference`, `PPE_PlacementGuideManager`)

| Field | Scene start |
| --- | --- |
| Position Follow Smooth | 0.06 s |
| Rotation Follow Smooth | 0.08 s |
| Height Smooth | 0.08 s |
| Respirator offset | (0, −0.10, 0.13) HMD local |
| FaceProtection offset | (0, −0.02, 0.20) HMD local |
| Torso vertical / forward | 0.40 m / 0.05 m |
| Foot spacing | 0.14 m |
| Lower leg height | 0.38 m |
| Lower body forward | 0.08 m |
| Boot look-down angle | 30° |
| Draw Dynamic Target Gizmos | on (disableable) |

Placement distances **unchanged**: Inner/Protective gloves 0.11 m, FaceProtection 0.18 m, Respirator 0.16 m, Suit 0.25 m, Boots pair 0.18 m. Ready materials still `PPE_GuideNotReady` / `PPE_GuideReady`.

### Changed files

- `Assets/1.JSR/Scripts/PPEPlayerBodyReference.cs`
- `Assets/1.JSR/Scripts/PPEPlacementGuideManager.cs`
- `Assets/1.JSR/Scripts/PPEGloveHandTargetFollower.cs`
- `Assets/1.JSR/Scripts/PPELowerLegTargetFollower.cs`
- `Assets/1.JSR/Scripts/PPEGlovePairCoordinator.cs`
- `Assets/1.JSR/Scripts/PPEPlacementDebugGizmo.cs`
- `Assets/Scenes/04_PPE.unity`
- `docs/handoffs/ppe/LATEST.md`

Not modified: `PPETrainingPhase`, sequence order, pair completion, cabinet, PPE scale, stored state, direct grab distances, ResponseBag, WasteContainer, locomotion, spawn, Common XR, wearable visuals.

### Verify (Quest / Play Mode)

1. Grab Left InnerGlove at cabinet, walk 1 m — Ghost stays on actual Left Hand.
2. Move/rotate Left Hand — Ghost follows immediately.
3. Bring glove to Left Hand — NotReady → Ready → Equip. No walk-back.
4. Grab Respirator, walk/turn — Ghost stays in front of current face.
5. Grab Suit, walk/turn — Ghost stays at current torso.
6. Grab Left Boot, look forward — Ghost hidden. Look down — Ghost under current body.
7. Walk away holding Boot, then look down — Ghost at **current** feet, not cabinet.
8. Turn 90°, look down — L/R boot targets follow body yaw, not HMD pitch.
9. Boot target reachable without kneeling (~0.38 m shin height).
10. Release without Equip — Ghost destroyed, none left behind.

### Remaining risks

- Face/suit offsets and boot look-down angle need Quest headset tuning.
- If App play disables the PPE-scene XR Origin that parents `PPE_PlayerBodyReference`, LateUpdate on that object stops. Script now prefers an **active** XR Origin/camera when the serialized one is inactive, but it does not reparent under Common XR.
- Boot Ghost is fully hidden (not translucent) when looking forward.
- No new worn visuals (Type C still hides on Equip).
- `ProjectSettings/Packages/com.unity.probuilder/Settings.json` is still local dirt; not reverted.

## Cabinet fit around calibrated PPE (2026-08-21)

PPE item world sizes were left at the previous audit reference. The wardrobe was oversized in **width/depth**, not height (suit already needed ~2.0 m). Uniform-scaling `PPE_Storage` would have sheared/shrunk PPE, so slots were unparented onto `PPE_StorageRoot` (scale 1) with **baked slot localScale 2.15**.

Menu (idempotent): `Tools/HCl Training/PPE/Fit Cabinet Around Calibrated PPE`

### Old cabinet world size (`PPE_Storage` 2.15 uniform × native 0.981 × 0.974 × 0.365)

- Width 2.109 m
- Height 2.095 m
- Depth 0.784 m
- Bottom shelf ~0.13 m, mid ~0.63–0.90 m, upper ~1.38 m, top ~1.84 m
- Center hanging bay ~ full 2.1 m width minus side shelves

### New cabinet world size (furniture scale 1.733 / 2.104 / 1.508)

- Width **1.700 m**
- Height **2.050 m**
- Depth **0.550 m**
- Interior still clears the 1.545 m suit (top ~1.72 m, ~0.33 m roof clearance)

Decorative `PPECabinet2` lockers stay **1.55** (side lockers ~1.52 m tall). They are not the PPE wardrobe.

### Slot layout after fit (StorageRoot local meters, yaw 180)

| Slot | Position | Reach |
| --- | --- | --- |
| InnerGlovesSlot | (−0.50, 0.95, 0.14) | player-right, chest |
| GlovesSlot | (0.50, 0.70, 0.14) | player-left, mid |
| BootsSlot | (0.50, 0.13, 0.10) | player-left, downward |
| RespiratorSlot | (0.50, 1.22, 0.14) | player-left, chest/shoulder |
| FaceShieldSlot | (0.50, 1.52, 0.14) | player-left, face height |
| SuitSlot | (0, 0.17, −0.04) | center hang |

Left/Right pair members stay under the same item roots. Item localScales unchanged.

### Colliders

Back-panel `BoxCollider` on `PPE_StorageRoot`: size 1.70 × 2.05 × 0.04 m, center (0, 1.025, −0.255). Does **not** fill the interior, so hands can still reach in. PPE grab colliders unchanged.

### Respirator `tripo_part_5`

3-vertex stray Tripo island (~2 cm native → ~6 mm after 0.13×2.15). Not a filter cartridge. Hide by running the Fit Cabinet menu (disables that child). Do not rescale the whole respirator.

### Verify

Play Mode: `PPEStoredDisplayState` recaptures local pose; items should not fall. Walk to the cabinet (spawn unchanged). Re-run `Audit PPE World Dimensions` to confirm PPE meters unchanged.

## Wearable PPE scale audit (2026-08-21)

Measured **mesh AABB × transform** in meters (not localScale alone). Storage parent is still uniform **2.15**. Editor printer: `Tools/HCl Training/PPE/Audit PPE World Dimensions`.

### Type after Equip

| PPE | Type | After success |
| --- | --- | --- |
| InnerGloves | **C** | Left/Right physical hide. No worn visual. Do not create one. |
| ProtectiveSuit | **C** | Root deactivates. No equippedVisual. |
| ChemicalBoots | **C** | Left/Right physical hide. No worn visual. Do not create one. |
| Respirator | **C** | Root deactivates. No equippedVisual. |
| FaceProtection | **C** | Root deactivates. No equippedVisual. |
| ProtectiveGloves | **A** | Physical hide. `ChemicalGloves_WearableHost` + left/right VrHand worn meshes activate. |

`PPEStateController` only restores chemical-glove worn meshes. Suit / respirator / face / inner / boots have no alternate worn mesh.

Held uses the same mesh/scale as stored. Guides have no dedicated prefab; they copy the held mesh. ProtectiveGloves ghost now bakes `SkinnedMeshRenderer.sharedMesh` so the guide is not empty.

### World dimensions after correction (meters)

Axes are world AABB of the stored visual. Hand-length / shaft called out separately.

| PPE | Stored | Held | Guide | Equipped |
| --- | --- | --- | --- | --- |
| InnerGlove Left | 0.099 × 0.084 × **0.221** | same | copies held | Missing |
| InnerGlove Right | 0.095 × 0.084 × **0.221** | same | copies held | Missing |
| ProtectiveSuit | 0.644 × **1.545** × 0.232 | same | copies held | Missing |
| ChemicalBoot Left | foot **0.280**, shaft **0.373**, width 0.127 | same | copies held | Missing |
| ChemicalBoot Right | same as Left (0% mesh delta) | same | copies held | Missing |
| Respirator | 0.274 × 0.222 × 0.267 | same | copies held | Missing |
| FaceProtection | 0.264 × 0.280 × 0.276 | same | copies held | Missing |
| ProtectiveGlove L/R stored | ~0.118 × 0.116 × **0.274** | same | copies held (skinned bake) | worn 0.817 local = **same world** as shelf 0.38×2.15 |

InnerGloves finger-to-wrist is mesh **Z**, not the 0.12 m X width from the previous pair pass.

### Scale changes

| Object | old localScale | new localScale | old world | new world |
| --- | --- | --- | --- | --- |
| InnerGloves Visuals (combined + L + R) | 0.125 | **0.103** | Z length 0.268 m | **0.221 m** |
| ChemicalBoots FBX | 0.19 / 0.22 / 0.18 | **0.15 / 0.174 / 0.142** | foot 0.355, shaft 0.472 | foot **0.280**, shaft **0.373** |
| FaceRespirator | 0.20 | **0.13** | ~0.42 × 0.34 × 0.41 | **0.27 × 0.22 × 0.27** |
| ChemicalFaceShield | 0.18 | **0.13** | ~0.37 cube | **0.26 × 0.28 × 0.28** |
| Worn VrHand L/R | 0.80 | **0.817** | 2% smaller than shelf | matches shelf world |
| ProtectiveSuit | 0.72 | unchanged | hanging **1.545 m**, shoulder 0.644 m | adult; not child-sized |
| Cabinet / PPE_Storage 2.15 / decorative 1.55 | unchanged | — | — | PPE still fits after shrinks |

ProtectiveGloves shelf local 0.38 vs worn 0.80 looked inconsistent; world was already ~0.817 vs 0.80. Worn aligned to 0.817.

### Colliders

Only InnerGloves PhysicalCollider + NearGrabAssist resized (visual 0.125 → 0.103). Grab distances unchanged (gloves 0.065, boots 0.10). Boot colliders stay in mesh-local units and follow the FBX root scale.

### Remaining Quest tuning

- InnerGloves 0.221 m hand-length vs tracked XR Hands
- Worn chemical gloves 0.817 vs tracked hands
- Respirator tiny second mesh (`tripo_part_5`, ~6 mm after scale) — source filter is not a matching part; do not leave it looking like a real cartridge
- Suit 1.545 m hanging height if it still reads short on-device (not changed here)
- Boot left/right side assignment after Y −90 (swap menu still available)

## Authored Left/Right pair pieces (2026-08-21)

### Source geometry (inspected from FBX)

**InnerGloves** (`PPE_glrove` one Model / one Geometry): **case A** — two disconnected islands in one Mesh. 2389 verts, 2 components (1195 / 1194). Centroids X +0.226 vs −0.221. Not submeshes, not two FBX objects, not a single connected mesh.

**ChemicalBoots** (`PPE_ChemicalSafetyBoots_01` one Model / one Geometry): **case A** — four islands in one Mesh (1039, 1036, 415, 406). Two boot bodies + two soles. Grouped by Z into two complete boots (1445 / 1451 verts). Not two FBX objects. Soles were merged with the nearest body. Side used original shelf yaw Y −90 (`x' = −z`); confirm in Scene View. Swap: `Tools/HCl Training/PPE/Swap ChemicalBoots Sides`.

ProtectiveGloves already have true `VrHand_Left` / `VrHand_Right` meshes.

### Separation method

Connected-component extraction (union-find on triangles). **Not** a simple X-axis vertex cut. **No full-pair copy fallback.** If islands cannot form two pieces, authoring stops and reports that Blender/manual split is required.

### Persistent mesh assets

- `Assets/1.JSR/Generated/PPE/InnerGlove_Left.obj`
- `Assets/1.JSR/Generated/PPE/InnerGlove_Right.obj`
- `Assets/1.JSR/Generated/PPE/ChemicalBoot_Left.obj`
- `Assets/1.JSR/Generated/PPE/ChemicalBoot_Right.obj`

Vertices, triangles, normals, UVs preserved. One material each (`AllSame`). Editor does not regenerate these on Play.

### Editor command

`Tools/HCl Training/PPE/Author Left/Right PPE Pairs`

1. Opens/uses `04_PPE`
2. Reuses authored Left/Right children
3. Assigns generated left/right meshes (never the combined pair mesh)
4. Adds grab / stored / filter / pair member if missing
5. Wires coordinator
6. Marks dirty and saves

Idempotent: a fully authored pair is left unchanged. Alias: `Tools/PPE/Author Left-Right Pair Pieces`. Side swap menus exist if the automatic Left/Right guess is visually reversed.

### Runtime

`splitCombinedShelfPairOnAwake: 0` on `04_PPE` ProtectiveGloves. Play does not generate pair members or instantiate duplicate meshes when authored members exist.

### Colliders / scale

Each member rebuilds PhysicalCollider + NearGrabAssist from **its own** visual bounds. World scale copies the original MeshFilter `lossyScale` onto the new Visual (InnerGloves Visual 0.125 under Storage 2.15; boots keep the FBX instance scale). No extra offset; left/right verts already sit in the original pair pose.

### Remaining

- InnerGloves / Boots **equipped** visuals still missing (out of scope)
- Dedicated guide prefabs still unassigned
- Confirm boot left/right in Scene View once after authoring
- `ProjectSettings/Packages/com.unity.probuilder/Settings.json` still local dirt; not reverted

## Temporary TTS / subtitle mute (2026-08-21)

Scene-only mute for grab/scale testing. No TTS script logic deleted.

- `PPE_TTS` inactive; `playIntroTutorialOnStart: 0`, `showSubtitles: 0`
- `PPE_EquipFeedbackCanvas` inactive (TTS subtitles + grab hint lines)
- `PPE_RoomIntroVoice` inactive

Re-enable those three GameObjects (and the two TTS flags) to restore voice and captions. Bag item-name hover labels were not changed.

## InnerGloves grab + full cabinet scale (2026-08-21)

### Grab root cause

Previous local BoxCollider shrink (`0.192×0.082×0.216` → `0.111×0.048×0.125`) lived on the InnerGloves **root**, while the mesh sits on offset `Visual` (`z ≈ -0.06`). `PPEDirectGrabProximityFilter` ignores trigger `NearGrabAssist` and only `ClosestPoint`s that thin physical box. InnerGloves is also a child of **non-uniform** `PPE_Storage` `(3.16, 2.41, 2.60)`, so ClosestPoint was unreliable. Pinching the visible glove was often >6 cm from that box → Select rejected. XRI `m_Colliders` listed only the shrunk root box.

### Grab fix

- New `PPEPrimitiveGrabVolumes`: runtime `PhysicalCollider` (visual mesh bounds, non-trigger) + `NearGrabAssist` (same + 5 cm, trigger). Root box disabled. Both registered on `PPEHandGrabbable`.
- Stored display uses the authored assist (`generateAssistFromBounds: 0` on InnerGloves). No world-AABB assist.
- Filter still 6.5 cm to **physical** ClosestPoint; falls back to world AABB if scale is non-uniform.
- Applied to all wearable cabinet PPE except glove pair members (they already build primitives).

### Cabinet

PPE items **are children** of `PPE_Storage`. Did **not** `localScale *= 0.7` blindly. Changed Storage to **uniform 2.15** so shelf local positions stay on the same mesh shelves without double-scaling PPE separately. Decorative `PPECabinet1/2` `2.0 → 1.55`. Slot local positions unchanged.

### Per-item local scale (after cabinet 2.15)

| Object | Old local | New local |
|---|---|---|
| PPE_Storage | 3.16 / 2.41 / 2.60 | **2.15 uniform** |
| PPECabinet1/2 | 2.0 | **1.55** |
| InnerGloves Visual | 0.125 | **0.125** (world shrinks with cabinet) |
| ProtectiveGloves Visual | 0.65 | **0.38** |
| Worn chemical gloves | 0.65 | **0.80** |
| Suit ×2 | 0.65 | **0.72** |
| FaceShield | 0.145 | **0.18** |
| Respirator | 0.17 | **0.20** |
| Boots | 0.17 / 0.195 / 0.16 | **0.19 / 0.22 / 0.18** |
| ResponseBag / WasteContainer | unchanged | already independent, believable carry/bin scale |

## Scale calibration + equip guidance (2026-08-21)

### Inspection (actual scene values before correction)

| Item | Where scale lived | Before |
|---|---|---|
| InnerGloves `Visual` | mesh container under `PPE_InnerGloves` | 0.216 uniform (tripo `PPE_glrove`, no FBX scale override) |
| ProtectiveGloves `Visual` | mesh container under `PPE_Gloves` | 0.80 uniform |
| Wearable chemical gloves | controller-parented VrHand prefabs | 1.0 (shelf vs worn mismatch) |
| FaceShield prefab instance | imported FBX root | non-uniform 0.134 / 0.175 / 0.162 |
| FaceRespirator prefab instance | imported FBX root | non-uniform 0.161 / 0.211 / 0.195 |
| ChemicalBoots prefab instance | imported FBX root | 0.164 / 0.18 / 0.15 |
| ChemicalSuit hanging pair | prefab instance roots | 0.70 uniform |
| ResponseBag wearable / hard-shell visual | existing | 0.85 / ~0.63 — left unchanged |
| WasteContainer | no scale override | left unchanged |
| Helmet | not present in `04_PPE` | n/a |
| Cabinets | PPECabinet 2.0, PPE_Storage ~3.16 | not edited |

FBX `globalScale` is 1 on all PPE meshes. Oversize was scene-instance / Visual-container, not importer scale. InnerGloves grab box was authored on the root (scale 1) while the mesh lived under Visual 0.216 — the mesh looked giant vs the tracked hand.

### Applied corrections (scene transforms, not runtime hacks)

| Item | After | Level |
|---|---|---|
| InnerGloves Visual | 0.125 uniform (~42% smaller) | mesh container |
| InnerGloves BoxCollider | 0.111 / 0.048 / 0.125, center Y 0.026 | proportional to visual |
| ProtectiveGloves Visual | 0.65 uniform | mesh container |
| Worn chemical gloves | 0.65 on left/right wearable prefab instances | worn visual sync |
| FaceShield | 0.145 uniform | prefab root |
| FaceRespirator | 0.17 uniform | prefab root |
| ChemicalBoots | 0.17 / 0.195 / 0.16 | prefab root |
| ChemicalSuit (both hangers) | 0.65 uniform | prefab root |

Ghost guides copy grabbed visual local/lossy scale, so they inherit the corrected size. Pair member grab colliders regenerate from mesh bounds at runtime.

### Equip guidance (no new HUD)

- `PPEEquipGuidanceHints` shows copy on existing `PPEEquipFeedbackUI` (same subtitle canvas as TTS).
- First grab in a Play session: `PPE를 집은 뒤, 반투명 착용 위치로 가져가면 자동으로 장착됩니다.`
- Later grabs: item-specific Korean line (gloves = opposite hand). ResponseBag / WasteContainer do not get wear hints.
- Ready vs NotReady: `PPE_GuideReady` alpha 0.52→0.72, `PPE_GuideNotReady` 0.32→0.24. No arrows, trails, or extra panels.

### Intentionally not changed

PPETrainingPhase, Donning/Equipment lists, glove pair completion, ResponseBag Y inspect, WasteContainer Prepared, XR Origin/spawn/locomotion, Common XR, cabinet layout.

### Inspector / Quest still to tune

See the agent report: InnerGloves 0.125, outer Visual 0.65, face/boot/suit values, and first-use hint once per Play Mode.

## Training flow — Donning vs Equipment Preparation

Wearable PPE and response preparation are two explicit phases. Helmet was not added. InnerGloves / ProtectiveGloves remain one logical step each. Grab, ghost, XR Origin, and glove physics were not edited.

### 1. Files modified

- `Assets/1.JSR/Scripts/PPETrainingPhase.cs` (new)
- `Assets/1.JSR/Scripts/PPESequenceManager.cs`
- `Assets/1.JSR/Editor/PPESequenceManagerEditor.cs` (new)
- `Assets/1.JSR/Scripts/PPEChecklistPanel.cs`
- `Assets/1.JSR/Editor/PPEChecklistPanelSetup.cs`
- `Assets/1.JSR/Scripts/PPETTSManager.cs`
- `Assets/1.JSR/Scripts/PPETTSSubtitles.cs`
- `Assets/1.JSR/Scripts/PPELoadoutPresets.cs` (comment only)
- `Assets/Scenes/04_PPE.unity` (PPESequenceManager lists only)
- `docs/handoffs/ppe/LATEST.md`

### 2. Phase model

`PPETrainingPhase` { `Donning`, `EquipmentPreparation`, `Complete` } is the single source of truth via `PPESequenceManager.CurrentPhase`. Phase is not inferred from mixed indices.

### 3. Donning order

InnerGloves → ProtectiveSuit → ChemicalBoots → Respirator → FaceProtection → ProtectiveGloves

### 4. Equipment preparation order

ResponseBag → WasteContainer

### 5. Legacy `sequenceOrder`

Kept serialized (`HideInInspector`) on `04_PPE`. If the two new lists are empty, Awake/OnValidate splits the old mixed 8-step list: wearables keep relative order, ResponseBag/WasteContainer move to equipment preparation. Scene YAML now also stores the two phase lists so Inspector is already migrated.

### 6. Gating

Unchanged rule: grab/preview allowed, Equip/Prepare gated by `CanEquip` = current required step. ResponseBag still also requires Y inspect. WasteContainer before ResponseBag is rejected. Toast strings unchanged.

### 7. ResponseBag Y

`NotifyBagContentsChecked` / `bagContentsChecked` / `bagNotCheckedMessage` unchanged. Completing the bag step still cannot happen without Y.

### 8. WasteContainer

Still `Prepared` via `WasteContainerPrepareInteractable` + `CompleteEquip(WasteContainer)`. No body-anchor / ghost change.

### 9. Checklist

Two groups: **PPE 착용** then **대응 장비 준비**. ResponseBag/WasteContainer are no longer mixed into the wear list. Status: `PPE 착용 n/6 | 대응 장비 준비 n/2`.

### 10. TTS

`OnPhaseChanged(Donning → EquipmentPreparation)` enqueues existing cue `EssentialPPEComplete` before the ResponseBag prompt. Subtitle updated to the phase-transition meaning. `20_EssentialPPEComplete.wav` audio may still mention waste-container prep until re-recorded.

### 11. SequenceComplete

Fires only after WasteContainer completes (both phases done). `OnPPESequenceCompleted` contract unchanged. App `StageLifecycleAdapter` was not edited.

### 12. FieldReadyComplete

Unchanged: full wearable set + ResponseBag equipped + WasteContainer prepared + `sequenceComplete=true`. Donning complete does **not** commit this snapshot.

### 13. Inspector

`PPE Sequence Manager`: read-only Current Phase / Required Step, then Donning Order and Equipment Preparation Order. Legacy Sequence Order is disabled + explained.

### 14. External listeners

- App `StageLifecycleAdapter`: still `OnPPESequenceCompleted` only (full complete)
- `PPETTSManager`: `OnRequiredStepChanged`, `OnPhaseChanged`, `OnPPESequenceCompleted`
- `PPEEquipmentManager` / `ResponseEquipmentManager` / `WasteContainerPrepareInteractable` / `PPEGlovePairCoordinator`: still `CanEquip` / `CompleteEquip`
- `PPEChecklistPanel` / `PPELoadoutData`: `ContainsStep` / `IsSequenceComplete`

### 15. Risks

- Clip 20 audio may not match the new subtitle until Integration re-records.
- Checklist current-step `>` pointer was not added (existing checkmarks only).
- `PPEFinalFlowValidationTool` Helmet-only check still expects Helmet in the configured lists; production lists still omit Helmet.

## ChemicalGloves left/right physical pair + PPE grab proximity

ProtectiveGloves remains one sequence/checklist/loadout step (`ChemicalGloves`). InnerGloves was not redesigned. `PPESequenceManager.sequenceOrder` was not edited.

### 1. PPE-specific grab distance

- XRI 3.4.1 `IXRSelectFilter` on each `PPEHandGrabbable` (`PPEDirectGrabProximityFilter`).
- Flow: NearFar finds a candidate → filter measures hand/attach pose to `Collider.ClosestPoint` on **physical** (non-trigger) colliders → select allowed only if `<= MaxDirectGrabDistance`.
- Already-held selects stay valid so a held glove is not dropped.
- No attraction, lerp, telekinesis, distance grab, or movement before select.

### 2. Common XR

- **Not modified.** `01_App` NearFar `m_CastRadius = 0.16` is unchanged. No XR Origin / spawn / locomotion edits.

### 3. MaxDirectGrabDistance

- Default **0.06 m**. Inspector-tunable; intended band 0.05–0.07 m.

### 4. Glove colliders

- Dense convex glove MeshColliders stay disabled.
- Each physical glove gets a local-space `CapsuleCollider` (`PhysicalCollider`) plus a trigger `BoxCollider` (`NearGrabAssist`) padded by 0.05 m (4–7 cm Inspector range).
- ChemicalGloves no longer uses the rotated-root world-AABB assist box.

### 5. Hierarchy (runtime split on Play)

```
PPE_Gloves (PPEEquipable ChemicalGloves, PPEGlovePairCoordinator)
├── LeftGlovePhysical  (PPEHandGrabbable + PPEGlovePairMember)
└── RightGlovePhysical (PPEHandGrabbable + PPEGlovePairMember)
```

Logical root grab/rigidbody is disabled and destroyed at runtime so children are not nested rigidbodies.

### 6. Opposite-hand donning

- Right hand grabs Left glove → only Left tracked-hand ghost.
- Left hand grabs Right glove → only Right tracked-hand ghost.
- Same-hand hold is allowed; no valid equip target is shown (option A).

### 7. Tracked XR hand transforms

- Default source is **Unity XR Hands 1.7.3** `XRHandSubsystem` `Palm` (Inspector can switch to `Wrist`).
- Pose is tracking-origin relative (`TryGetPose`) converted with `XROrigin.Origin.TransformPoint` / `rotation *` so locomotion does not drift.
- `LeftGloveEquipTarget` / `RightGloveEquipTarget` remain PPE-owned copies. They are not parented under XR Origin.
- **Left/Right Controller are not used** while Tracking Source = XRHands. Those roots are `<XRController>/devicePosition` only and do not follow Quest hand tracking.
- Explicit `ControllerFallback` exists for Editor/controller tests. XR Hands never silently falls back to the controller.

### 8. Wearable visuals

- `ChemicalGlovesWearableVisual.ApplyPpeRoomState(left, right)` enables each wearable glove independently.
- Partial pair progress is OR'd in so App `PPEStateController` (still passing both=IsEquipped) cannot hide a single worn glove.
- `PPEStateController` was **not** edited (Integration-protected).
- Wearable meshes still live under `Left/Right Controller/Visuals`. This pass did not reparent them onto XR Hands joints.

### 9. Sequence completion

- `LeftGloveEquipped` / `RightGloveEquipped` are internal.
- Either side may be first. Sequence does not advance after one glove.
- Both true → existing `PPEEquipmentManager.Equip(ChemicalGloves)` → `CompleteEquip` → checklist/sequence advance.

### 10. Inspector references

Play Mode auto-wires if left empty:

- `PPE_Gloves` → `PPEGlovePairCoordinator` (already added). `Deactivate On Equip` is **false**.
- Optional: assign `Left Member` / `Right Member`, `Left/Right Tracked Target`, `Left/Right Guide Prefab`.
- `PPEGloveHandTargetFollower`: Tracking Source **XRHands**, Target Joint **Palm**, Position/Rotation Offset 0 (tune on Quest). `Draw Editor Debug Gizmos` optional.
- `PPEGlovePairCoordinator.Placement Distance` default **0.11 m** (Quest-tunable 0.10–0.12). Angle **40°**. Pair placement uses these, not the generic `PPEEquipable` 0.14 m.
- `PPEDirectGrabProximityFilter.Max Direct Grab Distance` = **0.06 m**. Distance uses selecting-hand XR Hands Palm when that joint is valid; otherwise interactor attach.
- Optional dedicated ghost prefabs; if empty, a single-glove semi-transparent mesh ghost is built from that glove only. Pair-mesh fallback is not used for a held member.
- `ChemicalGlovesWearableVisual.SetLeftGloveEquipped` / `SetRightGloveEquipped` exist. App `PPEStateController` was not edited.

### Tracking loss

- TargetValid requires `XRHand.isTracked` + joint `TryGetPose` + `XRHandJointTrackingState.Pose`.
- Untracked target: follower does not copy the last pose; guide hides; equip cannot fire.
- Tracking resume: guide and placement resume. Editor without XR Hands: set Tracking Source to ControllerFallback or placement stays disabled.

### 11. Quest test procedure

A. Stand ~15–16 cm from a shelf glove, grip: must not grab.  
B. Reach 5–7 cm to the glove surface: grab succeeds. No attraction.  
C. Hover without grip: glove must not move.  
D. Right hand grabs left glove: only left ghost; place on tracked left hand; left wearable on; right stays on shelf; required step stays ProtectiveGloves.  
E. Left hand grabs right glove: only right ghost; place on tracked right hand; if left already on, ProtectiveGloves completes once via `PPEEquipmentManager.Equip` → Donning → EquipmentPreparation.  
F. Right hand holds right glove: object stays held; no right-hand equip guide; no auto-equip / force-release / shelf teleport.  
G. While holding left glove, lose left-hand tracking: guide hides; equip cannot fire; restore tracking and guide resumes.  
H. Equip left only: right shelf glove remains stored/grabbable.

Tune grab 0.05–0.07 and placement 0.10–0.12 / angle 40° on device.

### 12. Remaining risks

- Tracking uses XR Hands `isTracked` + Palm `TryGetPose`. If OpenXR Hand Tracking is off, XRHands source disables placement instead of silently using the controller.
- App `StageXRProfileController` still deactivates `Camera Offset/VR Hand Pose Tool Tracking` at runtime. Equip targets read the subsystem directly and do not need those GameObjects.
- Wearable glove meshes remain parented under Left/Right Controller Visuals; they will not follow XR Hands if the controller pose is untracked. PPE did not reparent Common XR visuals.
- NearFar still lives under Left/Right Controller. Select already includes `MetaAimHand` pinch / `HandInteraction` grasp; XRI Position falls back to `XRHandDevice`. Quest-only grab must be verified on device.
- Placement still uses 40° vs tracked palm rotation; Quest may need a wider angle.
- Proximity filter also applies to other `PPEHandGrabbable` items (ClosestPoint to their own colliders). Far ray select of cabinet PPE will fail by design; App far-cast is already off.
- InnerGloves is still one combined object. The pair coordinator is reusable later; do not duplicate this pass onto InnerGloves yet.
- Runtime split is not persisted in YAML until Play; members are created on Awake.
- Dedicated left/right guide prefabs are still unassigned; single-glove mesh copy is the current silhouette.
- `ProjectSettings/Packages/com.unity.probuilder/Settings.json` is still local Unity dirt; not reverted.
- Helmet 3D object still absent from `04_PPE`.

## Files changed (this glove pass)

- `Assets/1.JSR/Scripts/PPEDirectGrabProximityFilter.cs` (+ meta)
- `Assets/1.JSR/Scripts/PPEGlovePairCoordinator.cs` (+ meta)
- `Assets/1.JSR/Scripts/PPEGlovePairMember.cs` (+ meta)
- `Assets/1.JSR/Scripts/PPEGloveHandTargetFollower.cs` (+ meta)
- `Assets/1.JSR/Scripts/PPEHandGrabbable.cs`
- `Assets/1.JSR/Scripts/PPEStoredDisplayState.cs`
- `Assets/1.JSR/Scripts/PPEPlacementGuideManager.cs`
- `Assets/1.JSR/Scripts/ChemicalGlovesWearableVisual.cs`
- `Assets/1.JSR/Editor/PPEHandGrabSetup.cs`
- `Assets/Scenes/04_PPE.unity` (`PPEGlovePairCoordinator`, `deactivateOnEquip: 0` only on ChemicalGloves)
- `docs/handoffs/ppe/LATEST.md`

## Integration 요청

- Optional later: persist kinematic stored rigidbodies and glove physical members in `04_PPE` YAML so Play is not the first split.
- Optional: `PPEStateController` could read `PPEGlovePairCoordinator` left/right flags directly. Not required now; wearable already merges pair progress.
- Optional: set `01_App` NearFar `m_CastRadius` only if other stages also need a shorter near sphere. PPE now validates locally.
- XR Hands: App currently disables `Camera Offset/VR Hand Pose Tool Tracking` and keeps wearable/default hands under Controller Visuals. For true hand-tracking visuals after glove equip, Integration needs those meshes to follow XR Hands joints (PPE did not edit Common XR / App).
- NearFar is still parented to Left/Right Controller. Hand-only grab depends on existing XRI `MetaAimHand`/`XRHandDevice` fallbacks. If Quest-only pinch does not select, Integration must bind NearFar attach to the tracked hand without changing the 0.16 m global radius unless other stages are checked.

## Cabinet stored display + near grab

- Root cause of falling PPE: `PPEHandGrabSetup.Configure` authored every wearable cabinet item as `Rigidbody.useGravity = true` and `isKinematic = false`. Scene YAML still has those values (`m_UseGravity: 1`, `m_IsKinematic: 0`) on FaceShield, both ChemicalSuits, ChemicalGloves, FaceRespirator, ChemicalSafetyBoots, and InnerGloves. No Awake/Start script was moving transforms; gravity started on Play.
- `PPEStoredDisplayState` now locks Stored items kinematic / no-gravity at authored pose until `PPEHandGrabbable.GrabStarting` (XRI `OnSelectEntering`, before base grab).
- Near grab assist is a child trigger `NearGrabAssistVolume` expanded by 0.05 m (Inspector 0.04–0.07). It is registered on the existing `XRGrabInteractable.colliders` list. It does not attract, lerp, or teleport PPE.
- Dense convex MeshColliders on baked chemical-glove meshes (~11822 verts) are disabled at runtime and replaced with a primitive box. Visual meshes are unchanged.
- Player spawn, XR Origin, camera, locomotion, and cabinet layout were not modified.
- Helmet 3D object is still absent from `04_PPE`.

## Files changed

- `Assets/1.JSR/Scripts/PPEStoredDisplayState.cs` (new)
- `Assets/1.JSR/Scripts/PPEStoredDisplayState.cs.meta` (new)
- `Assets/1.JSR/Scripts/PPEHandGrabbable.cs`
- `Assets/1.JSR/Editor/PPEHandGrabSetup.cs`
- `Assets/1.JSR/Scripts/ChemicalGlovesWearableVisual.cs` (earlier hand Visual rotation)
- `Assets/Scenes/04_PPE.unity` (earlier default-hand Visual identity only)
- `docs/handoffs/ppe/LATEST.md`

## Required Inspector and Scene work

- Open `04_PPE` and Play. Cabinet items should stay put without assigning fields.
- Optional: `Tools/PPE/Setup STEP 2 Hand Grab` writes `PPEStoredDisplayState` into the scene.
- Leave `Display Anchor` empty unless an item must restore to a specific transform.
- Near Grab Assist Distance default 0.05. Tune 0.04–0.07 only.
- `Optional Hover Feedback` stays off; existing `PPEInteractableOutline` is unchanged.

## Validation

- Static inspection of `04_PPE` rigidbodies, glove MeshColliders, and XRI 3.4.1 `XRBaseInteractable.Awake` override compatibility.
- Unity Play Mode / Quest headset not run in this pass.
- `ProjectSettings/Packages/com.unity.probuilder/Settings.json` is a local Unity edit; it was not reverted.

## Integration 요청

- None for this cabinet grab pass. Optional later: persist kinematic stored rigidbodies in `04_PPE` via the STEP 2 tool so YAML matches runtime before Awake.

## Chemical glove Visual rotation

- Entering the PPE room showed the XR Hands sample meshes with the wearable glove quaternion, so the default hands looked twisted.
- `ChemicalGlovesWearableVisual` now treats Visual rotation only:
  - default `PPE_DefaultHand_Left/Right` → identity while unequipped
  - `PPE_VrHand_*_Wearable` → captured chemical-glove Visual rotation when `ChemicalGloves` is equipped
- Controller tracking, Visuals parent, and ray origin are unchanged.
- `04_PPE` default-hand prefab overrides now match identity. Wearable glove overrides keep the existing grip * 70° model correction.
- App `01_App` was not edited. Runtime alignment covers the Common XR Origin copies.

## Files changed

- `Assets/1.JSR/Scripts/ChemicalGlovesWearableVisual.cs`
- `Assets/Scenes/04_PPE.unity`
- `docs/handoffs/ppe/LATEST.md`

## Required Inspector and Scene work

- None beyond Play Mode / Quest check of left/right default hands on PPE entry, then glove orientation after ChemicalGloves equip.

## Validation

- Static script and scene YAML edits only.
- Unity Play Mode / Quest headset not run in this pass.
- `01_App` authored default-hand rotations still contain the old glove quaternion; runtime now overrides them during PPE.

## Integration 요청

- Optional later cleanup: set `01_App` `PPE_DefaultHand_Left/Right` localRotation to identity so the authored App scene matches runtime. Do not copy this Visual correction onto controller tracking or ray origin.

## Helmet result

- Repository-wide inspection found no Helmet 3D GameObject, prefab, FBX, or mesh. Only a title-menu Helmet icon exists outside PPE ownership; it was not reused.
- The minimum runtime contract is now explicit: ItemId `Helmet`, `PPEType.Helmet`, slot `Head`, default distance `0.18m`, angle `35 degrees`.
- `PPESequenceStep.Helmet` and both ItemId mappings were added to the existing `PPESequenceManager`.
- Helmet is not added to the production eight-step default order. For isolated validation, configure `Sequence Order = [Helmet]`; this uses existing `CanEquip`, `CompleteEquip`, and `OnPPESequenceCompleted` without changing production order.
- `PPEChecklistPanel` creates/enables `HelmetRow` only when Helmet is in the configured sequence. It reads `PPEEquipmentManager.IsEquipped("Helmet")`; no duplicate state exists.
- Placement continues to call only `PPEEquipmentManager.Equip`, which owns local equipped state, manager state, XR selection cancellation, `EquipmentSucceeded`, `StateChanged`, loadout sync, checklist refresh, and sequence completion.
- Helmet authoring sets `Deactivate On Equip=false` so a future Helmet visual remains attached and visible at the Head anchor.
- The legacy Ray + Grip wearable fallback is restored behind `XRInteractionConfirmRouter.allowLegacyWearableEquipFallback` and routes through the same `Equip` method.
- Other PPE profiles and the production order were not redesigned.

## Existing XR placement architecture in this worktree

- `PPEHandGrabbable` supplies grab started/grabbed/released events and Left/Right handedness through XRI.
- `PPEEquipable` stores type, slot flags, distance, angle tolerance, Guide prefab, and equipped presentation.
- `PPEPlacementGuideManager` builds Not Ready/Ready guides and calls existing Equip after distance+rotation hold succeeds.
- `PPEPlayerBodyReference` and `PPEEquipmentAnchorSet` provide body-relative Head/Face/Hands/Body/Feet/Radio/GasDetector anchors without modifying Common XR.
- `PPEPlacementDebugGizmo` and `PPEFinalFlowValidationTool` provide editor diagnostics.

## Files changed for Helmet validation

- `Assets/1.JSR/Scripts/PPESequenceStep.cs`
- `Assets/1.JSR/Scripts/PPEEquipmentManager.cs`
- `Assets/1.JSR/Scripts/PPESequenceManager.cs`
- `Assets/1.JSR/Scripts/PPEChecklistPanel.cs`
- `Assets/1.JSR/Scripts/PPEEquipable.cs`
- `Assets/1.JSR/Scripts/PPESelectionManager.cs`
- `Assets/1.JSR/Scripts/XRInteractionConfirmRouter.cs`
- `Assets/1.JSR/Editor/PPEEquipmentSetup.cs`
- `Assets/1.JSR/Editor/PPEEquipmentSlotSetup.cs`
- `Assets/1.JSR/Editor/PPEFinalFlowValidationTool.cs`
- `docs/handoffs/ppe/LATEST.md`

Earlier uncommitted PPE STEP 2-8 files and Scene changes remain in the same worktree; they were not reverted.

## Required Inspector and Scene work

1. Add the approved PPE-owned Helmet root to `04_PPE` and name it `PPE_Helmet`.
2. Configure `PPEEquipable`: ItemId `Helmet`, Type `Helmet`, Equip Slots `Head`, distance `0.18`, angle `35`, Deactivate On Equip `false`, and optional Helmet Ghost Guide prefab.
3. Configure `PPEHandGrabbable`, a non-kinematic Rigidbody with gravity, and an appropriate convex Collider. Its XRI interaction layers must match the Common XR hand direct interactor and ray fallback.
4. Confirm `PPE_PlacementGuideManager` references the anchor set and Not Ready/Ready materials; keep Auto Equip enabled and tune Ready Hold Duration in Inspector.
5. Confirm `PPEEquipmentAnchorSet.Head` references `PPE_PlayerBodyReference/PlayerEquipmentAnchors/Head`.
6. For an isolated Helmet run, temporarily set `PPESequenceManager.Sequence Order` to Helmet only. Restore the production order afterward.
7. Run `Tools/PPE/Validate Helmet Reference Flow` before headset testing.

## Quest/OpenXR safety inspection

- Installed: XR Hands `1.7.3`, OpenXR `1.16.1`; XRI project assets are version `3.4.1`.
- Android OpenXR configuration has Hand Tracking Subsystem, Meta Hand Tracking Aim, and Hand Interaction Profile enabled.
- The protected PPE App XR profile references left/right `XRHandTrackingEvents`.
- `PPEHandGrabbable` uses standard XRI select events and `IXRInteractor.handedness`, so its runtime path is not Editor-simulator-specific.
- Common XR hand visual/direct-interactor activation is Integration-owned and is now configured as described in the update below.

## Validation

- `dotnet build Assembly-CSharp.csproj --no-restore`: 0 errors.
- Unity Editor auto-import log showed no new Helmet-related compiler error.
- Batchmode could not start because another Unity instance currently has the project open.
- Generated `Assembly-CSharp-Editor.csproj` omits the already-existing untracked `PPEPlayerBodyReference.cs`, so its two missing-type errors are a stale generated project-list issue rather than a Helmet diagnostic.
- Static checks confirm the Head anchor, placement manager/materials, XR Hands package, and Meta Quest OpenXR features exist.
- Real Helmet PlayMode/device flow is unverified because the repository has no Helmet 3D object.

## Quest device test procedure

1. On Quest, connect both controllers. Verify the XR Hands sample FBX models follow the controllers and both near/direct interactors can Grip a known XRI object without visible rays.
2. Start the Helmet-only sequence and grab `PPE_Helmet`. Confirm Left/Right handedness and that the object follows the hand.
3. Confirm one Not Ready ghost appears on the body-relative Head anchor and follows room-scale translation/HMD height without inheriting HMD pitch or roll.
4. Enter distance while keeping rotation invalid; the guide must remain Not Ready.
5. Match distance and rotation for Ready Hold Duration; confirm Ready, clean XRI select exit, and no stuck pinch/select state.
6. Confirm Helmet stays visible at Head, Rigidbody becomes kinematic with gravity/collision disabled, and `IsEquipped("Helmet")` is true.
7. Confirm Helmet checklist completion and one existing `OnPPESequenceCompleted` invocation in Helmet-only mode.
8. Confirm PPE never displays controller models or visible interaction rays; both hands' direct Grip must reach the same equipment state path.

## Remaining blockers and Integration requests

1. Supply or approve a PPE-owned Helmet 3D prefab/mesh. Do not substitute another PPE or the title-menu icon.
2. Confirm on Quest that the Integration-owned two-hand near/direct profile works without visible rays.
3. Perform Quest tuning for Head anchor pose, attach pose, collider, distance, angle, and ready hold duration.
4. Decide later whether Helmet joins the production completion order. This pass intentionally leaves that order unchanged.

## Integration update — PPE controller-tracked hand visual swap

- Updated: 2026-08-20
- PPE shared XR requirements now use `BothHands`, near interaction on, and far interaction off.
- Before chemical gloves are equipped, the App shows `LeftHand.fbx` and `RightHand.fbx` from XR Hands `1.7.3` as controller-tracked visuals. This does not enable optical hand tracking.
- Both default hands use the existing left/right Grip animator controllers and `Hand` input driver.
- After `ChemicalGloves` is equipped, `ChemicalGlovesWearableVisual` hides the default sample hands and shows the existing left/right chemical-glove hand meshes.
- The App XR policy hides both controller models and disables both standard rays throughout PPE.
- Both near/far interactors remain active in near-only mode so either Grip can interact without rendering a ray.
- PPE state post-processing now preserves the correct default-hand/glove-hand state instead of restoring controller meshes.
- `04_PPE` mirrors the initial authoring state: both sample hands on, glove hands off, controller models off, and ray objects off.
- Start/Complete and the other stages keep their existing controller policies.
- PreEntry and LeakResponse were verified to already load through `LoadSceneMode.Additive`; Full flow keeps both loaded and transfers active ownership without reloading the plant environment.
- Updated files: `StageXRRequirements.cs`, `StageXRProfileController.cs`, `PPEStateController.cs`, `ChemicalGlovesWearableVisual.cs`, PPE shared requirements/profile assets, `01_App.unity`, `04_PPE.unity`, and `AppFlowStateTests.cs`.
- Validation: Unity scripts recompiled with 0 C# errors. Quest runtime/Play Mode interaction remains for the scene owner to verify.

## Scene duplicate fileID cleanup

- Updated: 2026-08-21
- Removed duplicated serialized blocks from `Assets/Scenes/04_PPE.unity` for `GasDetector`, `LeftHand`, and `PPE_PlayerBodyReference`.
- Preserved one complete declaration for each affected GameObject, Transform, and MonoBehaviour fileID; existing scene rotation edits were not changed.
- Static validation found 624 declared YAML object IDs and 0 duplicate ID groups.
- `git diff --check -- Assets/Scenes/04_PPE.unity` passed.
- Unity Editor / Play Mode validation was not run in this cleanup pass.

## Glove EquipReference alignment + unreachable dropped-PPE recovery

- Updated: 2026-08-24
- Root cause: the Ghost uses `TargetPalm * GhostPoseCalibration`, while release validation measures the separately authored physical `EquipReference`. Inner-glove references still used old mesh-relative offsets, and both protective gloves had no authored reference, so runtime generated the full visual AABB center.
- Four independent `EquipReference` children are now authored so the physical glove pose that visually matches its calibrated Ghost maps the reference to the target Palm:
  - Left Inner: previous `(-0.0230000, 0.0260000, -0.0440000)`, new `(0.0272355, 0.0068711, 0.0321682)`
  - Right Inner: previous `(0.0240000, 0.0260000, -0.0440000)`, new `(-0.0269729, 0.0069609, 0.0324536)`
  - Left Protective: previous runtime visual-center approximately `(-0.0085592, 0.1172689, -0.0101589)`, new `(-0.0155140, 0.1022527, -0.0281869)`
  - Right Protective: previous runtime visual-center approximately `(0.0095076, 0.1172690, -0.0101588)`, new `(0.0155955, 0.1019178, -0.0280463)`
- No `GhostPoseCalibration` position/rotation, glove scale, target tracking, sequence, pair completion, wearable visual, TTS, XR Origin, or cabinet geometry was changed.
- Inner and Protective release distances remain `0.15 m`.
- Editor preview now draws the reference Palm, translucent Ghost, and amber EquipReference. Selecting `EquipReference` enables a separate position handle. `Tools/HCl Training/PPE/Align EquipReference To Ghost Palm` performs only the semantic reference alignment.
- Editor/Development release diagnostics log member, holding hand, Palm/reference world positions, distance/max, visual alignment, target validity, and result. Cyan Palm and amber EquipReference crosses plus their distance line persist for five seconds. These diagnostics are compiled out of production players.

### Dropped-PPE recovery behavior

- Authored point: `PPE_StorageRoot/PPE_DroppedItemRecoveryPoint`, local `(-0.95, 0.75, 0.45)` / world approximately `(0.95, 0.75, 1.99)`, outside and in front of the cabinet collider.
- Only a PPE entering `PPEStoredDisplayState.Released` starts its own low-cost coroutine; there is no per-frame PPE scene scan.
- The object's cached lowest non-trigger collider point is compared with `active XR Origin floor Y + 0.22 m`. If no active origin is available, the authored point's `0.75 m` floor offset is used.
- The object must remain below the threshold and unselected for `1.75 s`. Grabbing, equipping, disabling, or resetting cancels recovery.
- Recovery sets `StorageState.Recovered`, moves directly to the authored point, clears velocity, sets `isKinematic=true`, `useGravity=false`, keeps grab enabled, and adds no VFX/audio/UI reward.
- Eligible: all physical pair members (InnerGloves, ProtectiveGloves, Boots) independently, plus ChemicalSuit, FaceRespirator, and ChemicalFaceShield. ResponseBag, WasteContainer, and Helmet are excluded.
- Recovery does not call pair coordinator methods or equipment reset, so the other side's equipped state and current sequence step are preserved.
- The original stored parent/local pose remains captured separately. `ResetPpeRoomTraining()` still calls `RestoreToStoredPose()` and returns recovered objects to their cabinet/hanger pose.

### Files changed

- `Assets/1.JSR/Editor/PPEGhostPosePreview.cs`
- `Assets/1.JSR/Scripts/PPEInnerGloveDonning.cs`
- `Assets/1.JSR/Scripts/PPEReleaseEquip.cs`
- `Assets/1.JSR/Scripts/PPEStoredDisplayState.cs`
- `Assets/1.JSR/Scripts/PPEDroppedItemRecoveryPoint.cs` + `.meta`
- `Assets/Scenes/04_PPE.unity`
- `docs/handoffs/ppe/LATEST.md`

### Validation and remaining device checks

- `dotnet build Assembly-CSharp.csproj --no-restore`: 0 errors.
- `dotnet build Assembly-CSharp-Editor.csproj --no-restore`: 0 errors.
- Unity Editor imported, authored, and saved all four explicit references and the recovery point without new compiler errors.
- Static ideal-overlap calibration maps each EquipReference to Palm distance approximately `0 m`, inside the unchanged `0.15 m` release threshold.
- DEVICE TEST REQUIRED: capture the new release log for all four gloves and confirm `TargetValid=true`, finite distance `<=0.15`, and side-specific equip result.
- DEVICE TEST REQUIRED: verify reachable drops remain in place, floor drops recover after `1.75 s`, pre-delay grabs cancel, recovered items remain directly grabbable, pair progress is preserved, and full reset restores authored storage poses.
- Integration request: none. If the active shared XR Origin's transform Y is not the tracking-floor datum on Quest, Integration must expose the shared floor datum without changing PPE's XR Origin.

## Helmet head release + runtime glove palm root + boot facing

- Updated: 2026-08-24

### Helmet

- `04_PPE` still contains no Helmet GameObject, prefab, FBX, mesh, `PPEEquipable`, grab component, EquipReference, or equipped visual. Repository search found only a title-menu icon. Therefore there is no physical Helmet release event to equip in the current scene.
- `PPESequenceStep.Helmet` and ItemId mapping exist, but the authored/default donning order remains `InnerGloves -> ProtectiveSuit -> ChemicalBoots -> Respirator -> FaceProtection -> ProtectiveGloves`. `ContainsStep(Helmet)=false`, so a future Helmet object would currently produce `CanEquip(Helmet)=false` / `SEQUENCE_BLOCKED`.
- No Helmet step was inserted. Product/SOP must explicitly choose its production order before sequence configuration changes.
- The technical path remains ready for a future PPE-owned Helmet object: Head-slot release resolves the invisible `PPEEquipmentAnchorSet.Head`, which follows the active HMD using `Helmet Head Position Offset = (0, 0.08, 0.04)`.
- After a successful Helmet-only release equip, a non-deactivating Helmet root is reparented to the live Head anchor with world pose preserved. It therefore follows subsequent HMD movement without an equip Ghost or visible target.
- The authoring profile remains `Helmet Release Equip Distance = 0.18 m`, `deactivateOnEquip=false`. A future Helmet must author its semantic `EquipReference` and equipped visual.
- Helmet release now has a dedicated Editor/Development-only `[PPE Helmet Release]` diagnostic with target/sequence validity, reference/Head positions, finite distance/max, and `EQUIPPED`, `TOO_FAR`, `SEQUENCE_BLOCKED`, or `TARGET_INVALID`.
- Helmet has no Ghost or visible Head marker. Guidance text is `안전모를 머리 위로 가져간 뒤 놓으세요.`

### Glove runtime position separation

- Root cause: `GhostPoseCalibration.localPosition` combined two different concepts: large generated-mesh origin compensation and the small runtime XR Palm visual offset. Inner-glove values therefore appeared as approximately `0.20 m` offsets even though that distance primarily compensated the OBJ coordinate origin.
- Runtime hierarchy now separates them:
  - Guide root: live Target XR Palm plus `GhostPoseCalibration.localPosition`
  - `GhostVisualOrigin`: generated-mesh origin compensation
  - Visual rotation: `TargetPalm.rotation * GhostPoseCalibration.localRotation`
- New independent runtime position offsets are all `(0, 0, 0)`:
  - Left Inner, Right Inner, Left Protective, Right Protective
- Previous large values were preserved only as non-user `ghostVisualOriginOffset` mesh-pivot compensation:
  - Left Inner `(0.0554254, 0.0010908, -0.2083801)`
  - Right Inner `(-0.0601459, -0.0027404, -0.2074570)`
  - Left Protective `(-0.0036303, -0.0271679, 0.0076292)`
  - Right Protective `(-0.1288192, 0.0167177, 0.0275691)`
- All four authored rotation quaternions are unchanged. EquipReference, release distance, pair state, and glove scale are unchanged.
- Development Runtime Calibration Mode is exposed per glove. In Editor/Development builds, hold that glove, select its `GhostPoseCalibration`, and tune the small local position while the tracked hand and Ghost stay visible. Cyan Target Palm and magenta semantic Ghost-palm markers show their separation. Production builds ignore the mode.
- Static semantic Palm-root distance is `0 m`. Visible XR hand surface overlap and any final `0-3 cm` side-specific correction require Quest observation; no unmeasured device offset was invented.

### Boot body-facing correction

- Mesh analysis found both generated boot toes point mainly along local `+X`, with mirrored Z components:
  - Left normalized toe `(0.9860346, 0, 0.1665409)`
  - Right normalized toe `(0.9860046, 0, -0.1667183)`
- Side-specific model-axis corrections are authored on the existing boot `GhostPoseCalibration`:
  - Left yaw `-80.413241 degrees`, quaternion `(0, -0.64554595, 0, 0.76372147)`
  - Right yaw `-99.597067 degrees`, quaternion `(0, -0.76377950, 0, 0.64547724)`
- Runtime remains `BodyYawRotation * BootModelAxisCorrection`. `PPEPlayerBodyReference` derives Body Yaw from HMD forward projected onto XZ with smoothing, and `PPELowerLegTargetFollower` applies that yaw without HMD pitch/roll.
- Existing left/right lower-leg positions were not changed.
- `Tools > HCl Training > PPE > Preview Boot Ghost Facing` draws Body Forward, both boot meshes, corrected toe arrows, and allows independent rotation handles by selecting each boot `GhostPoseCalibration`.

### Files changed in this pass

- `Assets/1.JSR/Editor/PPEBootGhostFacingPreview.cs` + `.meta`
- `Assets/1.JSR/Editor/PPEGhostPosePreview.cs`
- `Assets/1.JSR/Editor/PPEGlovePairMemberEditor.cs`
- `Assets/1.JSR/Scripts/PPEEquipGuidanceHints.cs`
- `Assets/1.JSR/Scripts/PPEGlovePairMember.cs`
- `Assets/1.JSR/Scripts/PPEPlacementGuideManager.cs`
- `Assets/1.JSR/Scripts/PPEPlayerBodyReference.cs`
- `Assets/1.JSR/Scripts/PPEReleaseEquip.cs`
- `Assets/Scenes/04_PPE.unity`
- `docs/handoffs/ppe/LATEST.md`

### Validation / device requirements

- Static mesh PCA plus independent shaft/sole heuristics agreed that both toes are local `+X`; corrected vectors map to Body Forward.
- Static inspection confirms the existing lower-leg targets use horizontal Body Yaw only and keep their previous positions.
- Static inspection confirms the production donning order is unchanged and excludes Helmet.
- Unity Editor completed a current Asset Pipeline script compile/domain reload with no C# errors after importing these files. Older Editor.log entries from earlier revisions are stale.
- `git diff --check` reaches only pre-existing scene-YAML trailing spaces outside these scoped additions; no new scoped C# whitespace issue was reported.
- DEVICE TEST REQUIRED: use a Development Quest build to tune/confirm each glove visible palm/wrist overlap within `0-3 cm`.
- DEVICE TEST REQUIRED: hold each boot, turn Body Yaw by 90 degrees, and confirm both toes rotate together while looking further down does not tilt them.
- DEVICE TEST BLOCKED: Helmet cannot be physically tested until an approved PPE-owned Helmet object is authored and an explicit SOP sequence position is chosen.

## 2026-08-28 HCl whiteboard integration

- Moved the existing HCl process whiteboard content from the PPE worktree into `Assets/Scenes/04_PPE.unity` without replacing the current integrated scene.
- Added `HCl_Whiteboard_WallMount` as a child of `Wall_Left`; the board, diagram layers, description panel, and runtime PPE preparation-order board all use this shared wall-relative mount.
- Added only the whiteboard-owned art and controller under `Assets/1.JSR/Art/Whiteboard` and `Assets/1.JSR/Scripts/Whiteboard`.
- Unity reimported and reopened `04_PPE.unity` successfully. Scene file IDs, asset GUID references, hierarchy links, and conflict markers were checked.
- A current `Assembly-CSharp` compile completed with exit code 0. Existing deprecation and unused-field warnings remain outside this transfer.
- Runtime headset viewing and final wall-offset confirmation remain for the scene owner.

## 2026-08-28 PPE board/start/bag integration correction

- The preparation board now copies only the active left/right physical members for InnerGloves, ChemicalBoots, and ChemicalGloves. The original combined shelf mesh is excluded, so items 01, 03, and 07 show one pair rather than two pairs.
- Ghost, preview, highlight, equipped, and wearable renderers are excluded from all eight preparation-board model copies.
- All copied models inherit the `HCl_Whiteboard_WallMount` rotation. This converts the former +Z display basis to the `Wall_Left` -X viewing direction so glove backs/fingers, boot toes, response-bag front, and waste-container front use the wall-facing orientation.
- PPE standalone XR Origin and `Resources/SharedXR/PPE.asset` now start at yaw `-90` so the initial view faces the Wall_Left whiteboard.
- Packing the third response item no longer auto-completes the bag. After all three items are stored, the local bag becomes Grip-selectable and one additional Grip confirms collection and hides the PPE bag.
- Response-bag completion no longer enables `ResponseBag_Wearable`; the legacy wearable API is retained only for serialized compatibility and always hides the body-following bag. PreEntry and LeakResponse continue to use their own local scene bags.
- Changed files: `PPEProcessDiagramController.cs`, `ResponseBagPackingController.cs`, `ResponseBagPrepareInteractable.cs`, `ResponseEquipmentManager.cs`, `ResponseBagWearableVisual.cs`, `PPE.asset`, and `04_PPE.unity`.
- Validation: current Assembly-CSharp Roslyn compile completed with 0 errors; `04_PPE.unity` has 0 duplicate YAML document IDs. Quest visual orientation and the final extra-Grip flow remain for the scene owner to verify.
## 2026-08-28 PPE board model-facing and room collision correction

- Whiteboard preparation models 02, 04, 05, 06, and 08 now receive the required 180-degree front-face correction.
- Inner-glove model axes are normalized to match the outer-glove presentation; both glove pairs are re-centered to remove authored shelf staggering.
- `PPE_DeskTop_TopCollider` now uses the actual renderer-bounds center and retries CharacterController collision exclusion after the App XR Origin becomes available.
- `PPE_StorageRoot` uses one full visual-bounds-fitted solid BoxCollider as a trainee blocker while ignoring cabinet-owned PPE/shelf collider pairs.

## 2026-08-28 PPE VR wall comfort and response-bag Grip correction

- Disabled the PC renderer's screen-space ambient occlusion feature. Its one-sample screen-space shadow pattern was producing camera-relative dark speckling on the PPE room walls and corners, which appeared to swim as the headset moved.
- The mobile renderer was already free of this feature; the PC/Link and Editor render path now matches that stable wall presentation.
- After the detector, absorbent pad, and absorbent sock are packed, the bag's final interaction collider, prepare callback, and XRI interactable are enabled in registration-safe order.
- A single Grip while a hand/Near-Far interactor is hovering the fully packed bag now bypasses the generic PPE sticky-selection delay and immediately confirms the bag. The existing XRI select callback remains as a safe duplicate path.
- The packing controller also consumes the next shared XR Grip directly after all three items are packed. This is the authoritative fallback for the PPE NearFar hand rig and guarantees bag collection even when XRI does not promote the highlighted bag from hover to select.
- Kinematic-body velocity writes in the packing snap path were guarded, removing the repeated `Setting linear/angular velocity of a kinematic body is not supported` warnings from this flow.
- Changed files: `Assets/Settings/PC_Renderer.asset`, `ResponseBagPackingController.cs`, `ResponseBagPackableItem.cs`, `ResponseBagPrepareInteractable.cs`, `XRInteractionConfirmRouter.cs`, and this handoff.

## 2026-08-28 PPE interaction review refactor

- All logical pair roots (`InnerGloves`, `ChemicalGloves`, `ChemicalBoots`) now keep one explicit disabled dummy collider in `XRSimpleInteractable.colliders`. An empty list can no longer make XRI auto-collect the two physical member colliders and steal their Grab ownership.
- Member `PPEHandGrabbable` objects retain their authored physical and near-assist colliders. Pair interaction distance, opposite-hand rules, and sequence order are unchanged.
- Held Ghost readiness and release-to-equip now use the same `PPEPlacementValidation` calculation for target validity, distance, optional rotation, and sequence state. Existing items with rotation validation disabled remain distance-only.
- The extra active `ChemicalSuit` instance under `SuitSlot` is disabled in the scene, leaving one interactive logical `ChemicalSuit` item.
- XR startup settings were inspected but not changed. In the installed XR Management version, `XRGeneralSettings.InitManagerOnStart` owns loader initialization and subsystem start while manager `automaticLoading/automaticRunning` remain serialized false by package design.
- Changed files: `PPEGlovePairCoordinator.cs`, `PPEOutlineHighlight.cs`, `PPEPlacementGuideManager.cs`, `PPEReleaseEquip.cs`, new `PPEPlacementValidation.cs`, `04_PPE.unity`, and this handoff.
- Validation: Unity Editor Asset Pipeline completed script compilation/domain reload with no C# errors. Standalone `dotnet build` is not authoritative for this Unity version and stops inside the unmodified URP package source when using .NET SDK 10. Play Mode and Quest interaction remain device checks.
