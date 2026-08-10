using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
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
        public void SampleScene_AbsorbentSocksHaveCompleteGrabAndSnapWiring()
        {
            string scenePath = Path.Combine(Application.dataPath, "Scenes", "SampleScene.unity");
            string[] documents = File.ReadAllText(scenePath)
                .Replace("\r\n", "\n")
                .Split(new[] { "--- " }, StringSplitOptions.RemoveEmptyEntries);

            AssertSceneObjectWiring(documents, "INT_AbsorbentSock_Straight_01", "absorbent-sock-straight", 1);
            AssertSceneObjectWiring(documents, "INT_AbsorbentSock_Curved_01", "absorbent-sock-curved", 2);
            AssertSceneObjectWiring(documents, "INT_AbsorbentSock_Curved_02", "absorbent-sock-curved", 2);
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

        static void AssertSceneObjectWiring(
            string[] documents,
            string objectName,
            string compatibilityId,
            int expectedColliderCount)
        {
            string gameObjectDocument = FindDocumentContaining(
                documents, "!u!1 ", $"  m_Name: {objectName}\n");
            Assert.That(gameObjectDocument, Is.Not.Null, $"Missing Scene GameObject {objectName}.");

            var componentIds = new List<string>();
            foreach (string line in gameObjectDocument.Split('\n'))
            {
                const string componentPrefix = "  - component: {fileID: ";
                if (!line.StartsWith(componentPrefix, StringComparison.Ordinal))
                    continue;

                int idEnd = line.IndexOf('}', componentPrefix.Length);
                componentIds.Add(line.Substring(componentPrefix.Length, idEnd - componentPrefix.Length));
            }

            int rigidbodyCount = 0;
            int colliderCount = 0;
            int grabCount = 0;
            int snappableCount = 0;
            foreach (string componentId in componentIds)
            {
                string componentDocument = FindDocumentById(documents, componentId);
                Assert.That(componentDocument, Is.Not.Null, $"Missing component {componentId} on {objectName}.");

                if (componentDocument.StartsWith("!u!54 ", StringComparison.Ordinal))
                    ++rigidbodyCount;
                else if (componentDocument.StartsWith("!u!65 ", StringComparison.Ordinal) ||
                         componentDocument.StartsWith("!u!136 ", StringComparison.Ordinal))
                    ++colliderCount;
                else if (componentDocument.Contains("XRGrabInteractable"))
                {
                    ++grabCount;
                    Assert.That(CountColliderReferences(componentDocument), Is.EqualTo(expectedColliderCount));
                }
                else if (componentDocument.Contains("GhostSnappableObject"))
                {
                    ++snappableCount;
                    Assert.That(componentDocument, Does.Contain($"compatibilityId: {compatibilityId}"));
                    Assert.That(componentDocument, Does.Not.Contain("grabInteractable: {fileID: 0}"));
                    Assert.That(componentDocument, Does.Not.Contain("targetRigidbody: {fileID: 0}"));
                    Assert.That(componentDocument, Does.Not.Contain("scalableRoot: {fileID: 0}"));
                    Assert.That(componentDocument, Does.Not.Contain("snapAlignment: {fileID: 0}"));
                }
            }

            Assert.That(rigidbodyCount, Is.EqualTo(1));
            Assert.That(colliderCount, Is.EqualTo(expectedColliderCount));
            Assert.That(grabCount, Is.EqualTo(1));
            Assert.That(snappableCount, Is.EqualTo(1));
        }

        static string FindDocumentContaining(string[] documents, string typePrefix, string value)
        {
            foreach (string document in documents)
            {
                if (document.StartsWith(typePrefix, StringComparison.Ordinal) && document.Contains(value))
                    return document;
            }

            return null;
        }

        static string FindDocumentById(string[] documents, string componentId)
        {
            string suffix = $" &{componentId}\n";
            foreach (string document in documents)
            {
                int headerEnd = document.IndexOf('\n');
                if (headerEnd >= 0 && document.Substring(0, headerEnd + 1).EndsWith(suffix, StringComparison.Ordinal))
                    return document;
            }

            return null;
        }

        static int CountColliderReferences(string grabDocument)
        {
            int start = grabDocument.IndexOf("  m_Colliders:\n", StringComparison.Ordinal);
            int end = grabDocument.IndexOf("  m_InteractionLayers:\n", start, StringComparison.Ordinal);
            string colliderList = grabDocument.Substring(start, end - start);
            int count = 0;
            foreach (string line in colliderList.Split('\n'))
            {
                if (line.StartsWith("  - {fileID: ", StringComparison.Ordinal))
                    ++count;
            }

            return count;
        }

        static void AssertVector(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f));
        }
    }
}
