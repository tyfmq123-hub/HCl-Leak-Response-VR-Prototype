#if UNITY_EDITOR
using System.Linq;
using HClTraining.App;
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
    public void StandaloneFieldPreset_SeedsGlovesAndResponseEquipment()
    {
        var state = root.AddComponent<TrainingSessionState>();
        state.Begin(TrainingMode.LeakResponseOnly);
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
        Assert.AreEqual("Assets/3.JH/Scenes/Prototype/JH_Test_Scene.unity", SceneFlowController.PreEntryScenePath);
        Assert.AreEqual("Assets/Scenes/SSH_VR_Project.unity", SceneFlowController.LeakResponseScenePath);
        Assert.IsFalse(new[]
        {
            SceneFlowController.AppScenePath,
            SceneFlowController.StartScenePath,
            SceneFlowController.CompleteScenePath,
        }.Any(string.IsNullOrWhiteSpace));
    }
}
#endif
