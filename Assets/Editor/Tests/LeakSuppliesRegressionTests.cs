#if UNITY_INCLUDE_TESTS
using System.Reflection;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using VRProject.LeakResponse;
using VRProject.LeakResponse.Narration;

public sealed class LeakSuppliesRegressionTests
{
    GameObject player, item, floor;
    CharacterController capsule;
    BoxCollider solid;

    [SetUp]
    public void SetUp()
    {
        player = new GameObject("XR player collision regression");
        player.AddComponent<XROrigin>();
        capsule = player.AddComponent<CharacterController>();
        item = new GameObject("Absorbent source");
        solid = item.AddComponent<BoxCollider>();
        item.AddComponent<Rigidbody>().isKinematic = true;
        floor = new GameObject("Floor must still collide");
        floor.AddComponent<BoxCollider>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(item);
        Object.DestroyImmediate(player);
        Object.DestroyImmediate(floor);
    }

    [Test]
    public void SourceExcludesXrCapsuleButKeepsGrabLayerAndFloorCollisions()
    {
        P102AbsorbentPlayerCollisionGuard.Ensure(item);
        Assert.That(Physics.GetIgnoreCollision(solid, capsule), Is.True);
        Assert.That(Physics.GetIgnoreCollision(solid, floor.GetComponent<Collider>()), Is.False);
        Assert.That(item.layer, Is.Zero);
        Assert.That(solid.enabled && !solid.isTrigger, Is.True);
    }

    [Test]
    public void ReenableAndLostPhysicsPairAreReappliedBeforeSimulation()
    {
        P102AbsorbentPlayerCollisionGuard.Ensure(item);
        item.SetActive(false);
        item.SetActive(true);
        Assert.That(Physics.GetIgnoreCollision(solid, capsule), Is.True);
        Physics.IgnoreCollision(solid, capsule, false);
        var guard = item.GetComponent<P102AbsorbentPlayerCollisionGuard>();
        typeof(P102AbsorbentPlayerCollisionGuard).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(guard, null);
        Assert.That(Physics.GetIgnoreCollision(solid, capsule), Is.True);
    }

    [Test]
    public void RepeatedPresentationAddsOnlyOneGuardAndKeepsTriggerZones()
    {
        var trigger = item.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        P102AbsorbentPlayerCollisionGuard.Ensure(item);
        P102AbsorbentPlayerCollisionGuard.Ensure(item);
        Assert.That(item.GetComponents<P102AbsorbentPlayerCollisionGuard>().Length, Is.EqualTo(1));
        Assert.That(Physics.GetIgnoreCollision(trigger, capsule), Is.False);
        Assert.That(trigger.isTrigger && trigger.enabled, Is.True);
    }

    [Test]
    public void StoppedDropsResumeAtUnchangedPositiveIntensityButNotAtZero()
    {
        var particles = item.AddComponent<ParticleSystem>();
        var emission = particles.emission;
        emission.rateOverTime = 10;
        var controller = item.AddComponent<HClAbsorptionVisualController>();
        controller.ConfigureRuntime(floor.transform, null, particles, null, item.transform, null, null);
        controller.SetLeakIntensity01(.7f);
        particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        var synchronize = typeof(HClAbsorptionVisualController).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
        synchronize.Invoke(controller, null);
        Assert.That(particles.isPlaying, Is.True, "No new valve event should be necessary after activation.");
        Assert.That(particles.emission.rateOverTimeMultiplier, Is.EqualTo(7).Within(.001f));
        controller.SetLeakIntensity01(0);
        synchronize.Invoke(controller, null);
        Assert.That(particles.isEmitting, Is.False, "A closed valve must not be restarted by synchronization.");
        Assert.That(particles.emission.rateOverTimeMultiplier, Is.Zero);
    }
}
#endif
