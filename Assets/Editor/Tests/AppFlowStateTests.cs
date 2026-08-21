#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using HClTraining.App;
using HClTraining.SharedXR;
using NUnit.Framework;
using UnityEngine;

public sealed class AppFlowStateTests
{
    GameObject root;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("AppFlowStateTests");
        var existing = PPELoadoutData.Instance;
        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);
    }

    [TearDown]
    public void TearDown()
    {
        if (root != null)
            Object.DestroyImmediate(root);
        var existing = PPELoadoutData.Instance;
        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);
    }

    [Test]
    public void FullTraining_UsesAuthoredThreeStageOrder()
    {
        var state = root.AddComponent<TrainingSessionState>();
        state.Begin(TrainingMode.Full);

        CollectionAssert.AreEqual(
            new[] { TrainingStage.PPE, TrainingStage.PreEntry, TrainingStage.LeakResponse },
            state.StageSequence);
        Assert.AreEqual(TrainingStage.PPE, state.GetNextStage());

        state.EnterStage(TrainingStage.PPE);
        Assert.IsTrue(state.MarkCurrentStageComplete());
        Assert.AreEqual(TrainingStage.PreEntry, state.GetNextStage());
        state.EnterStage(TrainingStage.PreEntry);
        Assert.IsTrue(state.MarkCurrentStageComplete());
        Assert.AreEqual(TrainingStage.LeakResponse, state.GetNextStage());
        state.EnterStage(TrainingStage.LeakResponse);
        Assert.IsTrue(state.MarkCurrentStageComplete());
        Assert.AreEqual(TrainingStage.Complete, state.GetNextStage());
    }

    [TestCase(TrainingMode.PPEOnly, TrainingStage.PPE)]
    [TestCase(TrainingMode.PreEntryOnly, TrainingStage.PreEntry)]
    [TestCase(TrainingMode.LeakResponseOnly, TrainingStage.LeakResponse)]
    public void IndividualTraining_CompletesAfterSelectedStage(TrainingMode mode, TrainingStage expected)
    {
        var state = root.AddComponent<TrainingSessionState>();
        state.Begin(mode);
        Assert.AreEqual(expected, state.GetNextStage());
        state.EnterStage(expected);
        state.MarkCurrentStageComplete();
        Assert.AreEqual(TrainingStage.Complete, state.GetNextStage());
    }

    [Test]
    public void EquipmentAvailability_RequiresAcquisition()
    {
        var state = root.AddComponent<TrainingSessionState>();
        state.Begin(TrainingMode.Full);
        state.SetPpeResult(true, true, new[] { PersistentEquipmentInventory.AbsorbentSock });

        state.SetEquipmentAvailable(PersistentEquipmentInventory.AbsorbentPad, true);
        Assert.IsFalse(state.IsEquipmentAvailable(PersistentEquipmentInventory.AbsorbentPad));

        state.SetEquipmentAvailable(PersistentEquipmentInventory.AbsorbentSock, true);
        Assert.IsTrue(state.IsEquipmentAvailable(PersistentEquipmentInventory.AbsorbentSock));
        state.ClearScenarioAvailability();
        Assert.IsFalse(state.IsEquipmentAvailable(PersistentEquipmentInventory.AbsorbentSock));
    }

    [Test]
    public void IncidentState_ResetsFixedP102RunAndLeakFacts()
    {
        var state = root.AddComponent<TrainingSessionState>();
        state.Begin(TrainingMode.Full);

        Assert.AreEqual(FacilityRunState.Running, state.Incident.FacilityRunState);
        Assert.AreEqual(LeakState.Active, state.Incident.LeakState);

        Assert.IsTrue(state.SetFacilityRunState(FacilityRunState.Stopped));
        Assert.AreEqual(FacilityRunState.Stopped, state.Incident.FacilityRunState);
        state.Begin(TrainingMode.Full);
        Assert.AreEqual(FacilityRunState.Running, state.Incident.FacilityRunState);
        Assert.AreEqual(LeakState.Active, state.Incident.LeakState);
    }

    [Test]
    public void Telemetry_RecordsMeaningfulEventsWithoutServerDependency()
    {
        var state = root.AddComponent<TrainingSessionState>();
        var sink = new RecordingTelemetrySink();
        var telemetry = root.AddComponent<TrainingTelemetryService>();
        telemetry.Initialize(state, sink);
        state.Begin(TrainingMode.Full);

        telemetry.StartSession(TrainingMode.Full);
        state.EnterStage(TrainingStage.PreEntry);
        telemetry.StageEntered(TrainingStage.PreEntry);
        telemetry.StepEntered(TrainingStepId.PreEntry);
        telemetry.StepCompleted(TrainingStepId.PreEntry, attemptCount: 2, rejectedCount: 1);
        telemetry.EndSession(TrainingSessionStatus.Completed, TrainingExitReason.None);

        Assert.IsFalse(string.IsNullOrWhiteSpace(sink.SessionId));
        Assert.AreEqual(5, sink.Records.Count);
        Assert.AreEqual("SessionStarted", sink.Records[0].event_type);
        Assert.AreEqual("P102", sink.Records[0].facility_id);
        Assert.AreEqual("StepCompleted", sink.Records[3].event_type);
        Assert.AreEqual(2, sink.Records[3].attempt_count);
        Assert.AreEqual(1, sink.Records[3].rejected_count);
        Assert.AreEqual("SessionCompleted", sink.Records[4].event_type);
    }

    [Test]
    public void SharedBag_UsesFixedStageAnchorAndRestoresLegacyPlacement()
    {
        var xrRoot = CreatePath(root.transform, "Common XR Origin");
        var bodyAnchor = CreatePath(xrRoot, "PlayerBodyAnchor");
        var bag = CreatePath(bodyAnchor, "ResponseBag_Wearable");
        CreatePath(xrRoot, "Camera Offset/Left Controller");
        bag.localPosition = new Vector3(0.1f, 0.2f, 0.3f);

        var stageAnchor = CreatePath(root.transform, SharedPlayerEquipment.StageBagAnchorName);
        stageAnchor.SetPositionAndRotation(new Vector3(4f, 1f, -2f), Quaternion.Euler(0f, 90f, 0f));

        var equipment = root.AddComponent<SharedPlayerEquipment>();
        equipment.Initialize(xrRoot);

        Assert.IsTrue(equipment.TryPlaceAtStageAnchor(root.scene));
        Assert.IsTrue(equipment.IsUsingStageAnchor);
        Assert.Less(Vector3.Distance(stageAnchor.position, bag.position), 0.001f);
        Assert.Less(Quaternion.Angle(stageAnchor.rotation, bag.rotation), 0.01f);

        equipment.RestoreBodyRelativePlacement();
        Assert.IsFalse(equipment.IsUsingStageAnchor);
        Assert.AreSame(bodyAnchor, bag.parent);
        Assert.AreEqual(new Vector3(0.1f, 0.2f, 0.3f), bag.localPosition);
    }

    [Test]
    public void StandaloneFieldPreset_SeedsGlovesAndResponseEquipment()
    {
        var state = root.AddComponent<TrainingSessionState>();
        state.Begin(TrainingMode.LeakResponseOnly);
        var loadout = new GameObject(nameof(PPELoadoutData)).AddComponent<PPELoadoutData>();
        typeof(PPELoadoutData).GetProperty(
            nameof(PPELoadoutData.Instance),
            BindingFlags.Static | BindingFlags.Public)?.SetValue(null, loadout);
        PPELoadoutPresets.BeginFieldStandaloneWithOfficialPreset();

        var inventory = root.AddComponent<PersistentEquipmentInventory>();
        inventory.Initialize(state, null);
        inventory.SyncFromLoadout();

        Assert.IsTrue(state.LeftChemicalGloveWorn);
        Assert.IsTrue(state.RightChemicalGloveWorn);
        Assert.IsTrue(state.IsEquipmentAcquired(PersistentEquipmentInventory.AbsorbentSock));
        Assert.IsTrue(state.IsEquipmentAcquired(PersistentEquipmentInventory.AbsorbentPad));
        Assert.IsTrue(state.IsEquipmentAcquired(PersistentEquipmentInventory.WasteContainer));
        Assert.IsTrue(state.IsEquipmentAcquired(PersistentEquipmentInventory.RecoveryTongs));
    }

    [Test]
    public void Inventory_DeployRecallAndRecover_ReusesOneAuthoredInstance()
    {
        var state = root.AddComponent<TrainingSessionState>();
        state.Begin(TrainingMode.Full);
        state.SetPpeResult(
            true,
            true,
            new[] { PersistentEquipmentInventory.AbsorbentSock });

        var inventory = root.AddComponent<PersistentEquipmentInventory>();
        inventory.Initialize(state, root.transform);

        var authoredInstance = new GameObject("INT_AbsorbentSock_Straight_01");
        authoredInstance.transform.SetParent(root.transform, false);
        authoredInstance.transform.localPosition = new Vector3(1f, 2f, 3f);
        var instanceId = authoredInstance.GetInstanceID();

        inventory.BeginStage(root.scene, TrainingStage.LeakResponse);
        Assert.IsFalse(authoredInstance.activeSelf);
        Assert.IsTrue(inventory.SetAvailable(PersistentEquipmentInventory.AbsorbentSock, true));

        Assert.IsTrue(inventory.DeployOrRecall(PersistentEquipmentInventory.AbsorbentSock));
        Assert.IsTrue(inventory.IsDeployed(PersistentEquipmentInventory.AbsorbentSock));
        Assert.AreEqual(instanceId, authoredInstance.GetInstanceID());

        Assert.IsTrue(inventory.DeployOrRecall(PersistentEquipmentInventory.AbsorbentSock));
        Assert.IsFalse(inventory.IsDeployed(PersistentEquipmentInventory.AbsorbentSock));
        Assert.IsFalse(authoredInstance.activeSelf);

        authoredInstance.transform.localPosition = Vector3.zero;
        Assert.IsTrue(inventory.Recover(PersistentEquipmentInventory.AbsorbentSock));
        Assert.IsTrue(authoredInstance.activeSelf);
        Assert.AreEqual(new Vector3(1f, 2f, 3f), authoredInstance.transform.localPosition);
        Assert.AreEqual(
            1,
            root.GetComponentsInChildren<Transform>(true)
                .Count(item => item.name == "INT_AbsorbentSock_Straight_01"));
    }

    [Test]
    public void TransitionInputLock_RejectsDoubleLock()
    {
        var inputLock = root.AddComponent<TransitionInputLock>();
        inputLock.Initialize(root.transform);
        Assert.IsTrue(inputLock.TryLock());
        Assert.IsFalse(inputLock.TryLock());
        inputLock.Unlock();
        Assert.IsFalse(inputLock.IsLocked);
    }

    [Test]
    public void ScenePaths_KeepOfficialStageAssets()
    {
        Assert.AreEqual("Assets/Scenes/04_PPE.unity", SceneFlowController.PpeScenePath);
        Assert.AreEqual("Assets/Scenes/05_PreEntry.unity", SceneFlowController.PreEntryScenePath);
        Assert.AreEqual("Assets/Scenes/06_LeakResponse.unity", SceneFlowController.LeakResponseScenePath);
        Assert.IsFalse(new[]
        {
            SceneFlowController.AppScenePath,
            SceneFlowController.StartScenePath,
            SceneFlowController.CompleteScenePath,
        }.Any(string.IsNullOrWhiteSpace));
    }

    [Test]
    public void CommonXrPolicy_UsesPpeBothHandsWithoutRaysAndKeepsOtherStageControllers()
    {
        var xrRoot = CreatePath(root.transform, "Common XR Origin");
        var leftVisuals = CreatePath(xrRoot, "Camera Offset/Left Controller/Visuals");
        var leftModel = CreatePath(leftVisuals, "XR Controller Left");
        var leftHand = CreatePath(leftVisuals, "PPE_DefaultHand_Left");
        var leftGlove = CreatePath(leftVisuals, "PPE_VrHand_Left_Wearable");
        var leftLegacyVisual = CreatePath(xrRoot, "Camera Offset/Left Controller/Visual");
        var leftRay = CreatePath(xrRoot, "Camera Offset/Left Controller/Ray Interactor");
        var leftNearFar = CreatePath(xrRoot, "Camera Offset/Left Controller/Left_NearFarInteractor");
        CreatePath(leftNearFar, "LineVisual");

        var rightVisuals = CreatePath(xrRoot, "Camera Offset/Right Controller/Visuals");
        var rightModel = CreatePath(rightVisuals, "XR Controller Right");
        var rightHand = CreatePath(rightVisuals, "PPE_DefaultHand_Right");
        var rightGlove = CreatePath(rightVisuals, "PPE_VrHand_Right_Wearable");
        var rightLegacyVisual = CreatePath(xrRoot, "Camera Offset/Right Controller/Visual");
        var rightRay = CreatePath(xrRoot, "Camera Offset/Right Controller/Ray Interactor");
        var rightNearFar = CreatePath(xrRoot, "Camera Offset/Right Controller/Right_NearFarInteractor");
        CreatePath(rightNearFar, "LineVisual");

        leftVisuals.gameObject.SetActive(false);
        leftModel.gameObject.SetActive(false);
        rightVisuals.gameObject.SetActive(false);
        rightModel.gameObject.SetActive(false);
        leftLegacyVisual.gameObject.SetActive(true);
        rightLegacyVisual.gameObject.SetActive(true);
        leftRay.localRotation = Quaternion.Euler(70f, 0f, 0f);
        rightRay.localRotation = Quaternion.Euler(70f, 0f, 0f);

        var policy = typeof(StageXRProfileController).GetMethod(
            "ApplyCommonControllerPolicy",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(policy);

        var ppeRequirements = CreateRequirements(
            far: false,
            near: true,
            ui: true,
            grab: true,
            handVisualMode: StageXRHandVisualMode.BothHands);
        var preEntryRequirements = CreateRequirements(far: false, near: false, ui: false, grab: false);
        var leakRequirements = CreateRequirements(far: true, near: true, ui: true, grab: true);

        policy.Invoke(null, new object[] { xrRoot, ppeRequirements });
        AssertCommonControllerState(
            leftVisuals, leftModel, leftHand, leftLegacyVisual, leftRay, leftNearFar,
            expectedModel: false, expectedHand: true, expectedRay: false, expectedNearFar: true);
        AssertCommonControllerState(
            rightVisuals, rightModel, rightHand, rightLegacyVisual, rightRay, rightNearFar,
            expectedModel: false, expectedHand: true, expectedRay: false, expectedNearFar: true);
        Assert.IsFalse(leftGlove.gameObject.activeSelf);
        Assert.IsFalse(rightGlove.gameObject.activeSelf);

        policy.Invoke(null, new object[] { xrRoot, preEntryRequirements });
        AssertCommonControllerState(leftVisuals, leftModel, leftHand, leftLegacyVisual, leftRay, leftNearFar, true, false, false, false);
        AssertCommonControllerState(rightVisuals, rightModel, rightHand, rightLegacyVisual, rightRay, rightNearFar, true, false, false, false);
        Assert.IsFalse(leftGlove.gameObject.activeSelf);
        Assert.IsFalse(rightGlove.gameObject.activeSelf);

        policy.Invoke(null, new object[] { xrRoot, leakRequirements });
        AssertCommonControllerState(leftVisuals, leftModel, leftHand, leftLegacyVisual, leftRay, leftNearFar, true, false, false, true);
        AssertCommonControllerState(rightVisuals, rightModel, rightHand, rightLegacyVisual, rightRay, rightNearFar, true, false, false, true);
        Assert.IsFalse(leftGlove.gameObject.activeSelf);
        Assert.IsFalse(rightGlove.gameObject.activeSelf);

        Object.DestroyImmediate(ppeRequirements);
        Object.DestroyImmediate(preEntryRequirements);
        Object.DestroyImmediate(leakRequirements);
    }

    [Test]
    public void PpeStatePostProcessing_KeepsBothHandsVisibleAndControllerModelsHidden()
    {
        var session = root.AddComponent<TrainingSessionState>();
        session.Begin(TrainingMode.PPEOnly);
        session.EnterStage(TrainingStage.PPE);

        var xrRoot = CreatePath(root.transform, "Common XR Origin");
        var leftVisuals = CreatePath(xrRoot, "Camera Offset/Left Controller/Visuals");
        var leftModel = CreatePath(leftVisuals, "XR Controller Left").gameObject;
        var leftHand = CreatePath(leftVisuals, "PPE_DefaultHand_Left").gameObject;
        var leftGlove = CreatePath(leftVisuals, "PPE_VrHand_Left_Wearable").gameObject;
        var rightVisuals = CreatePath(xrRoot, "Camera Offset/Right Controller/Visuals");
        var rightModel = CreatePath(rightVisuals, "XR Controller Right").gameObject;
        var rightHand = CreatePath(rightVisuals, "PPE_DefaultHand_Right").gameObject;
        var rightGlove = CreatePath(rightVisuals, "PPE_VrHand_Right_Wearable").gameObject;

        var wearableHost = CreatePath(xrRoot, "ChemicalGloves_WearableHost").gameObject;
        var wearable = wearableHost.AddComponent<ChemicalGlovesWearableVisual>();
        wearable.EditorWire(
            leftHand, rightHand, leftGlove, rightGlove, new[] { leftModel, rightModel });

        var ppeState = root.AddComponent<PPEStateController>();
        ppeState.Initialize(session, null, xrRoot);
        ppeState.ApplyPersistentState();

        Assert.IsTrue(leftHand.activeSelf);
        Assert.IsTrue(rightHand.activeSelf);
        Assert.IsFalse(leftGlove.activeSelf);
        Assert.IsFalse(rightGlove.activeSelf);
        Assert.IsFalse(leftModel.activeSelf);
        Assert.IsFalse(rightModel.activeSelf);

        wearable.ApplyPpeRoomState(true, true);
        Assert.IsFalse(leftHand.activeSelf);
        Assert.IsFalse(rightHand.activeSelf);
        Assert.IsTrue(leftGlove.activeSelf);
        Assert.IsTrue(rightGlove.activeSelf);
    }

    [TestCase("<XRController>{RightHand}/gripPressed", "Right")]
    [TestCase("<OculusTouchController>{LeftHand}/gripPressed", "Left")]
    [TestCase("unassigned-device", "Unknown")]
    public void ConfirmRouter_ResolvesTheHandThatPressedGrip(string bindingPath, string expectedHand)
    {
        var resolver = typeof(XRInteractionConfirmRouter).GetMethod(
            "ResolveHandText",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(resolver);
        var resolved = resolver.Invoke(null, new object[] { bindingPath });
        Assert.AreEqual(expectedHand, resolved.ToString());
    }

    static Transform CreatePath(Transform parent, string path)
    {
        var current = parent;
        foreach (var segment in path.Split('/'))
        {
            var child = current.Find(segment);
            if (child == null)
            {
                child = new GameObject(segment).transform;
                child.SetParent(current, false);
            }
            current = child;
        }
        return current;
    }

    static StageXRRequirements CreateRequirements(
        bool far,
        bool near,
        bool ui,
        bool grab,
        StageXRHandVisualMode handVisualMode = StageXRHandVisualMode.Controllers)
    {
        var requirements = ScriptableObject.CreateInstance<StageXRRequirements>();
        requirements.EditorConfigure(string.Empty, Vector3.zero, Quaternion.identity,
            far, near, ui, grab, useHaptics: true, visualMode: handVisualMode);
        return requirements;
    }

    static void AssertCommonControllerState(
        Transform visuals,
        Transform model,
        Transform hand,
        Transform legacyVisual,
        Transform ray,
        Transform nearFar,
        bool expectedModel,
        bool expectedHand,
        bool expectedRay,
        bool expectedNearFar)
    {
        Assert.IsTrue(visuals.gameObject.activeSelf);
        Assert.AreEqual(expectedModel, model.gameObject.activeSelf);
        Assert.AreEqual(expectedHand, hand.gameObject.activeSelf);
        Assert.IsFalse(legacyVisual.gameObject.activeSelf);
        Assert.AreEqual(expectedRay, ray.gameObject.activeSelf);
        Assert.Less(Quaternion.Angle(Quaternion.Euler(70f, 0f, 0f), ray.localRotation), 0.01f);
        Assert.AreEqual(expectedNearFar, nearFar.gameObject.activeSelf);
    }

    sealed class RecordingTelemetrySink : ITrainingTelemetrySink
    {
        public string SessionId { get; private set; }
        public List<TrainingTelemetryRecord> Records { get; } =
            new List<TrainingTelemetryRecord>();

        public bool TryBeginSession(string sessionId, out string error)
        {
            SessionId = sessionId;
            error = null;
            return true;
        }

        public bool TryWrite(TrainingTelemetryRecord record, out string error)
        {
            Records.Add(record);
            error = null;
            return true;
        }
    }
}
#endif
