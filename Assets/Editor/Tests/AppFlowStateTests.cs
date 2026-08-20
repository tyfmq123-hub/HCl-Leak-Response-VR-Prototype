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
    public void IncidentState_UsesGenericFacilityAndKeepsP102CompatibilityDefault()
    {
        var state = root.AddComponent<TrainingSessionState>();
        state.Begin(TrainingMode.Full);

        Assert.AreEqual(FacilityId.P102, state.Incident.SelectedFacility);
        Assert.IsTrue(state.SelectFacility(FacilityId.P103));
        Assert.AreEqual(FacilityId.P103, state.Incident.SelectedFacility);
        Assert.AreEqual(FacilityRunState.Running, state.Incident.FacilityRunState);
        Assert.AreEqual(LeakState.Active, state.Incident.LeakState);

        Assert.IsTrue(state.SetFacilityRunState(FacilityRunState.Stopped));
        Assert.AreEqual(FacilityRunState.Stopped, state.Incident.FacilityRunState);
        Assert.IsTrue(state.SelectFacility(FacilityId.P104));
        Assert.AreEqual(FacilityRunState.Running, state.Incident.FacilityRunState);
    }

    [Test]
    public void Telemetry_RecordsMeaningfulEventsWithoutServerDependency()
    {
        var state = root.AddComponent<TrainingSessionState>();
        var sink = new RecordingTelemetrySink();
        var telemetry = root.AddComponent<TrainingTelemetryService>();
        telemetry.Initialize(state, sink);
        state.Begin(TrainingMode.Full, FacilityId.P103);

        telemetry.StartSession(TrainingMode.Full, state.Incident.SelectedFacility);
        state.EnterStage(TrainingStage.PreEntry);
        telemetry.StageEntered(TrainingStage.PreEntry);
        telemetry.StepEntered(TrainingStepId.PreEntry);
        telemetry.StepCompleted(TrainingStepId.PreEntry, attemptCount: 2, rejectedCount: 1);
        telemetry.EndSession(TrainingSessionStatus.Completed, TrainingExitReason.None);

        Assert.IsFalse(string.IsNullOrWhiteSpace(sink.SessionId));
        Assert.AreEqual(5, sink.Records.Count);
        Assert.AreEqual("SessionStarted", sink.Records[0].event_type);
        Assert.AreEqual("P103", sink.Records[0].facility_id);
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
        Assert.AreEqual("Assets/Scenes/PPE_PreparationRoom.unity", SceneFlowController.PpeScenePath);
        Assert.AreEqual("Assets/Scenes/JH_Test_Scene.unity", SceneFlowController.PreEntryScenePath);
        Assert.AreEqual("Assets/Scenes/SSH_VR_Project.unity", SceneFlowController.LeakResponseScenePath);
        Assert.IsFalse(new[]
        {
            SceneFlowController.AppScenePath,
            SceneFlowController.StartScenePath,
            SceneFlowController.CompleteScenePath,
        }.Any(string.IsNullOrWhiteSpace));
    }

    [Test]
    public void CommonXrPolicy_KeepsOneVisibleControllerRigAcrossStageOrder()
    {
        var xrRoot = CreatePath(root.transform, "Common XR Origin");
        var leftVisuals = CreatePath(xrRoot, "Camera Offset/Left Controller/Visuals");
        var leftModel = CreatePath(leftVisuals, "XR Controller Left");
        var leftLegacyVisual = CreatePath(xrRoot, "Camera Offset/Left Controller/Visual");
        var leftRay = CreatePath(xrRoot, "Camera Offset/Left Controller/Ray Interactor");
        var leftNearFar = CreatePath(xrRoot, "Camera Offset/Left Controller/Left_NearFarInteractor");
        CreatePath(leftNearFar, "LineVisual");

        var rightVisuals = CreatePath(xrRoot, "Camera Offset/Right Controller/Visuals");
        var rightModel = CreatePath(rightVisuals, "XR Controller Right");
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

        var ppeRequirements = CreateRequirements(far: true, near: false, ui: true, grab: true);
        var preEntryRequirements = CreateRequirements(far: false, near: false, ui: false, grab: false);
        var leakRequirements = CreateRequirements(far: true, near: true, ui: true, grab: true);

        policy.Invoke(null, new object[] { xrRoot, ppeRequirements });
        AssertCommonControllerState(leftVisuals, leftModel, leftLegacyVisual, leftRay, leftNearFar, true, false);
        AssertCommonControllerState(rightVisuals, rightModel, rightLegacyVisual, rightRay, rightNearFar, true, false);

        policy.Invoke(null, new object[] { xrRoot, preEntryRequirements });
        AssertCommonControllerState(leftVisuals, leftModel, leftLegacyVisual, leftRay, leftNearFar, false, false);
        AssertCommonControllerState(rightVisuals, rightModel, rightLegacyVisual, rightRay, rightNearFar, false, false);

        policy.Invoke(null, new object[] { xrRoot, leakRequirements });
        AssertCommonControllerState(leftVisuals, leftModel, leftLegacyVisual, leftRay, leftNearFar, false, true);
        AssertCommonControllerState(rightVisuals, rightModel, rightLegacyVisual, rightRay, rightNearFar, false, true);

        Object.DestroyImmediate(ppeRequirements);
        Object.DestroyImmediate(preEntryRequirements);
        Object.DestroyImmediate(leakRequirements);
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

    static StageXRRequirements CreateRequirements(bool far, bool near, bool ui, bool grab)
    {
        var requirements = ScriptableObject.CreateInstance<StageXRRequirements>();
        requirements.EditorConfigure(string.Empty, Vector3.zero, Quaternion.identity,
            far, near, ui, grab, useHaptics: true);
        return requirements;
    }

    static void AssertCommonControllerState(
        Transform visuals,
        Transform model,
        Transform legacyVisual,
        Transform ray,
        Transform nearFar,
        bool expectedRay,
        bool expectedNearFar)
    {
        Assert.IsTrue(visuals.gameObject.activeSelf);
        Assert.IsTrue(model.gameObject.activeSelf);
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
