# Shared XR architecture

## Ownership

The integrated runtime has one XR infrastructure owner:

```text
App
├─ Common XR Origin
│  ├─ Main Camera
│  ├─ Left Controller      (grip/device tracking)
│  │  ├─ Visuals           (model-only correction)
│  │  ├─ Ray Interactor    (pointer/aim pose)
│  │  └─ NearFarInteractor (pointer/aim pose)
│  └─ Right Controller
├─ XR Interaction Manager
├─ Input Action Manager
├─ SharedXRInputContract
├─ StageXRProfileController
├─ SessionEquipmentInventory (data only)
└─ TrainingTelemetryService
```

Stage scenes own gameplay, interactables, handlers, UI, and completion logic. Their
authored XR rigs are retained only as migration/test sources and are disabled when
the App loads a Stage. They must not become active in an integrated run.

## Tracking and pointer pose

- Controller roots continue to use grip/device tracking. This is the physical
  controller/model pose.
- Model orientation corrections stay below `Visuals`.
- The existing interactor transform is driven by the XRI `Position` and
  `Rotation` actions. Those actions prefer `pointerPosition` /
  `pointerRotation` and fall back to device pose.
- No second Ray, LineRenderer, Line Visual, or offset line is created.
- Near/Far already provides far casting. When Near/Far is requested, the standard
  Ray visual is off, which enforces one pointer visual per hand.

## Stage-owned XR requirements

Each Stage owns one `StageXRRequirements` asset under its own Resources folder:

| Stage | Asset | Pointer policy |
|---|---|---|
| PPE | `Assets/1.JSR/Resources/SharedXR/PPE.asset` | Standard Ray |
| PreEntry | `Assets/2.YJH/Resources/SharedXR/PreEntry.asset` | No pointer; movement/turning enabled |
| LeakResponse | `Assets/3.SSH/Resources/SharedXR/LeakResponse.asset` | Near only; far ray hidden |

The App selects the declaration by Scene path. It does not compare a Stage name to
choose Ray/Near-Far behavior. A future Stage supplies its own asset and does not
add another XR Origin or edit `StageXRProfileController`.

The declaration also contains the fallback App spawn pose, so a Stage without an
authored XR Origin can still use the shared rig. Existing Stage rigs are not
deleted until Quest feature-equivalence testing is complete.

## Shared input contract

`SharedXRInputContract` reuses actions from the App's existing XRI Default Input
Actions asset:

| Contract control | Existing action |
|---|---|
| Position / Rotation | `XRI Left|Right/Position|Rotation` |
| Grip / Select | `XRI Left|Right Interaction/Select Value|Select` |
| Trigger / Activate | `XRI Left|Right Interaction/Activate Value|Activate` |
| UI Press | `XRI Left|Right Interaction/UI Press` |
| Thumbstick | `XRI Left|Right/Thumbstick` |
| Thumbstick Click | Contract-owned physical Primary2DAxisClick action |
| Primary / Secondary | Contract-owned face-button actions (not present in Starter Actions) |
| Haptic | `SendHaptic(hand, amplitude, duration)` |

Stage code subscribes to the common event without changing App code:

```csharp
using HClTraining.SharedXR;

void OnEnable()
{
    SharedXRInputContract.Instance.Performed += OnInput;
}

void OnInput(SharedXRHand hand, SharedXRControl control)
{
    if (hand == SharedXRHand.Right && control == SharedXRControl.PrimaryButton)
        ConfirmStageAction();
}
```

Stage assemblies that currently exist reference
`HClTraining.SharedXR.Runtime`. PPE scripts are in the predefined runtime
assembly and can use the same API directly.

## Existing input-to-function audit

### PPE

| Input | Stage owner | Preserved behavior |
|---|---|---|
| Left/Right Grip (Select) | `XRInteractionConfirmRouter` | sticky select / confirm PPE or equipment |
| Right B | `PPESelectionManager` | cancel sticky selection |
| Left X | `PPEChecklistPanel` | checklist open/close |
| Left Y | `ResponseEquipmentManager` | selected response-bag contents |
| Right A after completion | existing lifecycle completion adapter | advance only after narration |
| Ray/UI | XRI interactors + PPE interactables | closest-hit selection and UI press |

### PreEntry

| Input | Stage owner | Preserved behavior |
|---|---|---|
| Left Y / Right B | `EntrySafetyInputAdapter` | equip / store detector |
| Left X / Right A | `EntrySafetyInputAdapter` | repeat guidance / advance to PT |
| Left Trigger | `EntrySafetyInputAdapter` | detector measurement hold |
| Right Trigger | `EntrySafetyInputAdapter` | pump action |
| Haptic | `EntrySafetyInputAdapter` | right-hand feedback |

Simulator actions remain available in Editor. Standalone execution without the App
retains the existing direct-input fallbacks.

## Session equipment and Stage bags

`SessionEquipmentInventory` persists only equipment IDs and stored/checked-out counts.
It never owns a bag Transform, Collider, prefab instance, or Stage progression.

- PPE stores equipment IDs after successful Grip-to-bag interactions.
- PreEntry owns `PreEntry_ResponseBag`, its local access zone/anchor, and the detector object.
- LeakResponse owns `LeakResponse_ResponseBag`, its local access zone/anchor, and its absorbent objects.
- Standalone Stage play seeds only that Stage's development items when no resolved Inventory exists.
- PPE or field-ready results always take priority over standalone seeds.

The App does not move or scale a Stage bag. Existing App-owned bag services remain only
as a temporary compatibility layer while all Stage branches migrate; Stage gameplay uses
the new data contract. See `docs/integration/SESSION_INVENTORY_CONTRACT.md`.

## Shared incident state and telemetry

`SharedIncidentState` exposes only the fixed P-102 run/stop state and leak
active/isolated state. PreEntry writes the stop fact;
LeakResponse maps them to its own Scene-local valves, effects, sound, and colliders.

`SharedTrainingTelemetry` is the Stage-facing, server-independent event contract.
Stage assemblies report only meaningful steps and content/equipment usage. The App's
`TrainingTelemetryService` adds session metadata and writes JSON Lines below
`Application.persistentDataPath/TrainingTelemetry`. Missing storage is a warning and
never blocks training. A future HTTP/DB sink can replace local storage without
changing Stage gameplay code.

### LeakResponse

| Input | Stage owner | Preserved behavior |
|---|---|---|
| Grip/Select | Near/Far + `XRGrabInteractable` | valves, absorbents, container, tongs |
| Trigger/Activate | Stage interactables | activation while held |
| Haptic | selecting interactor / shared contract | valve and interaction feedback |
| UI | Stage UI | shared Near/Far UI press |
| Left Y near bag | `P102SharedBagItemHandler` | retrieve the currently allowed existing Sock or Pad |

## Validation

`Tools/HCl Training/Validate Integrated App` checks Scene serialization,
requirement uniqueness, Scene paths, and pointer policy declarations.

`Tools/HCl Training/Run App PlayMode Smoke` checks every full and standalone
flow for:

- one active XR Origin, Main Camera, AudioListener, EventSystem,
  XR Interaction Manager, and Input Action Manager;
- one data-only session inventory and no active App-owned bag visual;
- camera plus exactly two active controller tracking drivers;
- active pointers only below the common rig;
- exactly two standard rays, two Near/Far pointers, or no pointer according to the
  active Stage declaration;
- a shared aim-pose driver on every active pointer;
- no possibility of both pointer systems being active per hand;
- unchanged Stage order, persistence, and completion transitions.

Game View is not proof of controller alignment. Final pose, visual alignment, and
HMD readability must be accepted on Quest with the user wearing the headset.
