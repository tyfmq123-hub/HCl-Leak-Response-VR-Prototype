#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;
using VRProject.LeakResponse.Narration;

public sealed class P102WasteContainerPhysicsTests
{
    GameObject container, body, lid;

    [SetUp]
    public void SetUp()
    {
        container = new GameObject("Waste physics test");
        body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        body.name = "HazWasteContainer_Body";
        body.transform.SetParent(container.transform, false);
        body.transform.localPosition = Vector3.up * 0.5f;
        body.transform.localScale = new Vector3(0.7f, 0.5f, 0.7f);
        Object.DestroyImmediate(body.GetComponent<Collider>());
        lid = new GameObject("HazWasteContainer_Lid", typeof(Rigidbody));
        lid.transform.SetParent(container.transform, false);
        lid.transform.SetLocalPositionAndRotation(new Vector3(0, 1.1f, 0), Quaternion.Euler(-90, 0, 0));
    }

    [TearDown] public void TearDown() => Object.DestroyImmediate(container);

    [Test]
    public void EarlyPreparation_FreezesAuthoredLidBeforeStageActivation_WithoutMovingIt()
    {
        var position = lid.transform.position;
        var rotation = lid.transform.rotation;
        var rb = lid.GetComponent<Rigidbody>();
        rb.useGravity = true;
        rb.isKinematic = false;
        P102WasteContainerPhysics.PrepareBeforeSimulation(container);
        Assert.That(rb.useGravity, Is.False);
        Assert.That(rb.isKinematic, Is.True);
        Assert.That(lid.transform.position, Is.EqualTo(position));
        Assert.That(Quaternion.Angle(lid.transform.rotation, rotation), Is.LessThan(0.001f));
    }

    [TestCase(0f, 1f)]
    [TestCase(137f, 1f)]
    [TestCase(45f, 0.8f)]
    public void HollowBody_SupportsRimAndWalls_ButLeavesCenterIntakeOpen(float yaw, float scale)
    {
        container.transform.SetPositionAndRotation(new Vector3(2, 0.1f, 3), Quaternion.Euler(0, yaw, 0));
        container.transform.localScale = Vector3.one * scale;
        var root = P102WasteContainerPhysics.EnsureBodyCollision(container);
        Assert.That(root, Is.Not.Null);
        var walls = root.GetComponentsInChildren<BoxCollider>();
        Assert.That(walls.Length, Is.EqualTo(17));
        Physics.SyncTransforms();
        int wallsHit = 0;
        int rimSupports = 0;
        foreach (var wall in walls)
        {
            Assert.That(wall.isTrigger, Is.False);
            Assert.That(wall.attachedRigidbody, Is.Null);
            if (wall.name == "Bottom") continue;
            var mid = wall.transform.position;
            var center = container.transform.TransformPoint(Vector3.up * 0.5f);
            var outward = (mid - center).normalized;
            if (wall.Raycast(new Ray(mid + outward * scale, -outward), out _, scale * 2)) wallsHit++;
            if (wall.Raycast(new Ray(mid + container.transform.up * scale, -container.transform.up), out _, scale * 2)) rimSupports++;
            var intakeRay = new Ray(container.transform.TransformPoint(Vector3.up * 1.2f), -container.transform.up);
            Assert.That(wall.Raycast(intakeRay, out _, scale), Is.False, "No solid ceiling/filled intake");
        }
        Assert.That(wallsHit, Is.EqualTo(16));
        Assert.That(rimSupports, Is.EqualTo(16));
        Assert.That(P102WasteContainerPhysics.EnsureBodyCollision(container), Is.SameAs(root));
        Assert.That(root.GetComponentsInChildren<Collider>().Length, Is.EqualTo(17), "No duplicate colliders on rebind");
    }

    [Test]
    public void BodyCollision_DoesNotOverwriteExistingAuthoredSolidCollider()
    {
        var authored = body.AddComponent<BoxCollider>();
        Assert.That(P102WasteContainerPhysics.EnsureBodyCollision(container), Is.Null);
        Assert.That(authored.enabled, Is.True);
        Assert.That(body.GetComponents<Collider>().Length, Is.EqualTo(1));
    }
}
#endif
