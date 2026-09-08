#if UNITY_INCLUDE_TESTS
using System.Linq;
using HClTraining.App;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;

public sealed class SharedXrHeightContractTests
{
    SceneSetup[] previousScenes;
    GameObject root, service;
    SharedCharacterGravityProvider gravity;
    ContinuousMoveProvider move;

    [SetUp]
    public void OpenApp()
    {
        previousScenes = EditorSceneManager.GetSceneManagerSetup();
        var scene = EditorSceneManager.OpenScene(SceneFlowController.AppScenePath);
        root = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<XROrigin>(true))
            .Single().gameObject;
        gravity = root.GetComponentInChildren<SharedCharacterGravityProvider>(true);
        move = root.GetComponent<ContinuousMoveProvider>();
        service = new GameObject("Height contract services");
    }

    [TearDown]
    public void CloseApp()
    {
        Object.DestroyImmediate(service);
        if (previousScenes.Any(s => s.isLoaded && s.isActive && !string.IsNullOrEmpty(s.path)))
            EditorSceneManager.RestoreSceneManagerSetup(previousScenes);
        else
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
    }

    [Test]
    public void App_UsesFloorAndOneCapsuleGravity_NotLegacyHeadProbe()
    {
        Assert.That(root.GetComponent<XROrigin>().RequestedTrackingOriginMode, Is.EqualTo(XROrigin.TrackingOriginMode.Floor));
        Assert.That(root.GetComponentsInChildren<SharedCharacterGravityProvider>(true).Length, Is.EqualTo(1));
        Assert.That(root.GetComponentsInChildren<GravityProvider>(true), Is.Empty);
        Assert.That(gravity.mediator, Is.SameAs(root.GetComponent<LocomotionMediator>()));
        Assert.That(root.layer, Is.EqualTo(LayerMask.NameToLayer("Ignore Raycast")));
        Assert.That(gravity.enabled, Is.False, "No gravity before a Stage profile is applied");
        Assert.That(move.gravityProvider, Is.Null);
#pragma warning disable CS0618
        Assert.That(move.useGravity, Is.False, "Only one owner applies gravity");
#pragma warning restore CS0618
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Transition_FreezesGravityAndMovementBeforeFloorUnload(bool restore)
    {
        gravity.enabled = move.enabled = true;
        var transition = service.AddComponent<TransitionInputLock>();
        transition.Initialize(root.transform);
        Assert.That(transition.TryLock(), Is.True);
        Assert.That(gravity.enabled || move.enabled, Is.False);
        Assert.That(transition.TryLock(), Is.False);
        transition.Unlock(restore);
        Assert.That(gravity.enabled, Is.EqualTo(restore));
        Assert.That(move.enabled, Is.EqualTo(restore));
    }

    // Pause/resume is exercised in IntegrationXrHeightValidation while playing.
    // Calling PauseTraining from Edit Mode would serialize Time.timeScale settings.
}
#endif
