# PPE Integration Handoff

## Work metadata

- Role: PPE
- Branch: `feature/ppe`
- Commit: uncommitted (release-to-equip for wearable PPE + debug visuals off)
- Push target: `origin/feature/ppe`
- Base develop commit: `d445493 feat: decouple stage equipment inventory and bags`
- Updated: 2026-08-21
- Current focus: InnerGloves preserved. Debug spheres off. ProtectiveGloves/Boots/Suit/Respirator/FaceProtection use release-to-equip. DEVICE TEST REQUIRED.

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
