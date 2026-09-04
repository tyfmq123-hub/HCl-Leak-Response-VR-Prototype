using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace VRProject.ObjectInstallation.Tests
{
    public sealed class GhostObjectSnapTests
    {
        readonly List<GameObject> createdObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = createdObjects.Count - 1; i >= 0; --i)
            {
                if (createdObjects[i] != null)
                    UnityEngine.Object.DestroyImmediate(createdObjects[i]);
            }

            createdObjects.Clear();
        }

        [Test]
        public void P102Scene_SockSourceHasCompleteGrabAndInstallWiring()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/06_LeakResponse.unity", OpenSceneMode.Single);
            var sceneObjects = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Select(transform => transform.gameObject)
                .ToArray();
            var sockSources = sceneObjects
                .Select(gameObject => gameObject.GetComponent<PreplacedGhostInstallSource>())
                .Where(source => source != null && source.ItemTypeId == "Sock")
                .ToArray();

            Assert.That(sockSources, Has.Length.EqualTo(1));
            var sockSource = sockSources[0];
            Assert.That(sockSource.GrabInteractable, Is.Not.Null);
            Assert.That(sockSource.GrabInteractable.gameObject, Is.SameAs(sockSource.gameObject));
            Assert.That(sockSource.GetComponents<Rigidbody>(), Has.Length.EqualTo(1));
            Assert.That(sockSource.GetComponents<Collider>(), Has.Length.EqualTo(1));
            Assert.That(sockSource.ConsumeSourceOnSuccess, Is.True);

            string[] expectedGhosts =
            {
                "INT_AbsorbentSock_Straight_01_ObjectGhost1",
                "INT_AbsorbentSock_Straight_01_ObjectGhost2",
                "INT_AbsorbentSock_Curved_01_ObjectGhost1",
                "INT_AbsorbentSock_Curved_01_ObjectGhost2",
            };
            foreach (string expectedGhost in expectedGhosts)
                Assert.That(sceneObjects.Count(gameObject => gameObject.name == expectedGhost), Is.EqualTo(1));
        }

        [Test]
        public void Straight_SelectsOnlyStraightSlot()
        {
            GhostObjectSnapSlot curved = CreateSlot("curved", "absorbent-sock-curved", Vector3.zero, 1f);
            GhostObjectSnapSlot straight = CreateSlot("straight", "absorbent-sock-straight", Vector3.right, 2f);

            GhostObjectSnapSlot result = GhostObjectSnapRegistry.FindNearestAvailable(
                new[] { curved, straight }, "absorbent-sock-straight", Vector3.zero);

            Assert.That(result, Is.SameAs(straight));
        }

        [Test]
        public void Straight_DoesNotSelectCurvedSlot()
        {
            GhostObjectSnapSlot curved = CreateSlot("curved", "absorbent-sock-curved", Vector3.zero, 2f);

            GhostObjectSnapSlot result = GhostObjectSnapRegistry.FindNearestAvailable(
                new[] { curved }, "absorbent-sock-straight", Vector3.zero);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void Curved_SelectsOnlyCurvedSlot()
        {
            GhostObjectSnapSlot straight = CreateSlot("straight", "absorbent-sock-straight", Vector3.zero, 2f);
            GhostObjectSnapSlot curved = CreateSlot("curved", "absorbent-sock-curved", Vector3.right, 2f);

            GhostObjectSnapSlot result = GhostObjectSnapRegistry.FindNearestAvailable(
                new[] { straight, curved }, "absorbent-sock-curved", Vector3.zero);

            Assert.That(result, Is.SameAs(curved));
        }

        [Test]
        public void Curved_SelectsNearestAvailableSlot()
        {
            GhostObjectSnapSlot far = CreateSlot("far", "absorbent-sock-curved", new Vector3(0.2f, 0f, 0f), 1f);
            GhostObjectSnapSlot near = CreateSlot("near", "absorbent-sock-curved", new Vector3(0.05f, 0f, 0f), 1f);

            GhostObjectSnapSlot result = GhostObjectSnapRegistry.FindNearestAvailable(
                new[] { far, near }, "absorbent-sock-curved", Vector3.zero);

            Assert.That(result, Is.SameAs(near));
        }

        [Test]
        public void OccupiedSlot_IsNotSelectedAgain()
        {
            GhostObjectSnapSlot occupied = CreateSlot("occupied", "absorbent-sock-curved", Vector3.zero, 1f);
            GhostObjectSnapSlot free = CreateSlot("free", "absorbent-sock-curved", Vector3.right * 0.1f, 1f);
            GhostSnappableObject first = CreateSnappable("first", "absorbent-sock-curved", Vector3.zero);

            Assert.That(first.TrySnapToSlot(occupied, true), Is.True);
            GhostObjectSnapSlot result = GhostObjectSnapRegistry.FindNearestAvailable(
                new[] { occupied, free }, "absorbent-sock-curved", Vector3.zero);

            Assert.That(occupied.IsOccupied, Is.True);
            Assert.That(result, Is.SameAs(free));
        }

        [Test]
        public void OutsideSnapDistance_ReturnsNoSlot()
        {
            GhostObjectSnapSlot slot = CreateSlot("slot", "absorbent-sock-straight", Vector3.right, 0.25f);

            GhostObjectSnapSlot result = GhostObjectSnapRegistry.FindNearestAvailable(
                new[] { slot }, "absorbent-sock-straight", Vector3.zero);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void SnapAttempt_RequiresLastInteractorToHaveExited()
        {
            Assert.That(GhostSnappableObject.CanAttemptSnap(1, false, false), Is.False);
            Assert.That(GhostSnappableObject.CanAttemptSnap(2, false, false), Is.False);
            Assert.That(GhostSnappableObject.CanAttemptSnap(0, false, false), Is.True);
            Assert.That(GhostSnappableObject.CanAttemptSnap(0, true, false), Is.False);
            Assert.That(GhostSnappableObject.CanAttemptSnap(0, false, true), Is.False);
        }

        [Test]
        public void ImmediateSnap_AlignsPoseAndWorldScale_HidesOnlySlotGhost_AndReleaseRestoresState()
        {
            GameObject originalParent = CreateObject("original-parent");
            originalParent.transform.localScale = new Vector3(0.5f, 2f, 4f);

            GameObject slotObject = CreateObject("slot");
            slotObject.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(10f, 35f, 20f));
            slotObject.transform.localScale = new Vector3(2f, 3f, 4f);
            MeshRenderer ghostRenderer = slotObject.AddComponent<MeshRenderer>();
            GhostObjectSnapSlot slot = slotObject.AddComponent<GhostObjectSnapSlot>();
            slot.Configure("absorbent-sock-straight", slotObject.transform, 10f, 0f,
                new Renderer[] { ghostRenderer }, true);

            GameObject unrelatedGhost = CreateObject("unrelated-ghost");
            MeshRenderer unrelatedRenderer = unrelatedGhost.AddComponent<MeshRenderer>();
            unrelatedRenderer.enabled = true;

            GhostSnappableObject snappable = CreateSnappable(
                "snappable", "absorbent-sock-straight", Vector3.zero, originalParent.transform);
            Rigidbody body = snappable.TargetRigidbody;
            int rigidbodyCountBefore = snappable.GetComponents<Rigidbody>().Length;
            bool originalUseGravity = body.useGravity;
            bool originalIsKinematic = body.isKinematic;

            Assert.That(snappable.TrySnapToSlot(slot, true), Is.True);
            AssertVector(snappable.SnapAlignment.position, slot.SnapPose.position);
            Assert.That(Quaternion.Angle(snappable.SnapAlignment.rotation, slot.SnapPose.rotation), Is.LessThan(0.001f));
            AssertVector(snappable.ScalableRoot.lossyScale, slot.TargetWorldScale);
            Assert.That(ghostRenderer.enabled, Is.False);
            Assert.That(unrelatedRenderer.enabled, Is.True);
            Assert.That(snappable.transform.parent, Is.SameAs(slot.SnapPose));
            Assert.That(body.isKinematic, Is.True);
            Assert.That(snappable.GetComponents<Rigidbody>().Length, Is.EqualTo(rigidbodyCountBefore));

            Assert.That(snappable.ReleaseFromSlot(), Is.True);
            Assert.That(slot.IsAvailable, Is.True);
            Assert.That(ghostRenderer.enabled, Is.True);
            Assert.That(snappable.transform.parent, Is.SameAs(originalParent.transform));
            Assert.That(body.useGravity, Is.EqualTo(originalUseGravity));
            Assert.That(body.isKinematic, Is.EqualTo(originalIsKinematic));
        }

        GhostObjectSnapSlot CreateSlot(string name, string compatibilityId, Vector3 position, float distance)
        {
            GameObject slotObject = CreateObject(name);
            slotObject.transform.position = position;
            GhostObjectSnapSlot slot = slotObject.AddComponent<GhostObjectSnapSlot>();
            slot.Configure(compatibilityId, slotObject.transform, distance, 0f, new Renderer[0], true);
            return slot;
        }

        GhostSnappableObject CreateSnappable(
            string name,
            string compatibilityId,
            Vector3 position,
            Transform parent = null)
        {
            GameObject snappableObject = CreateObject(name);
            snappableObject.transform.SetParent(parent, false);
            snappableObject.transform.position = position;
            Rigidbody body = snappableObject.AddComponent<Rigidbody>();
            XRGrabInteractable grab = snappableObject.AddComponent<XRGrabInteractable>();
            GhostSnappableObject snappable = snappableObject.AddComponent<GhostSnappableObject>();
            snappable.Configure(compatibilityId, grab, body, snappableObject.transform, snappableObject.transform);
            return snappable;
        }

        GameObject CreateObject(string name)
        {
            var created = new GameObject(name);
            createdObjects.Add(created);
            return created;
        }

        static void AssertVector(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f));
        }
    }
}
