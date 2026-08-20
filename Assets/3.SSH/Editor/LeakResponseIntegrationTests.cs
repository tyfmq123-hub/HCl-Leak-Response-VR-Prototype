#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Reflection;
using HClTraining.SharedXR;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRProject.LeakResponse.Narration;
using VRProject.Valves;

namespace VRProject.LeakResponse.EditorTests
{
    public sealed class LeakResponseIntegrationTests
    {
        const string LeakScenePath = "Assets/Scenes/06_LeakResponse.unity";

        [Test]
        public void LeakResponseScene_HasRequiredObjectsAndNoMissingOrHandPoseToolComponents()
        {
            Scene scene = EditorSceneManager.OpenPreviewScene(LeakScenePath);
            try
            {
                Assert.That(scene.IsValid() && scene.isLoaded, Is.True);
                Assert.That(Find(scene, "INT"), Is.Not.Null);
                Transform bagAnchor = Find(scene, SharedPlayerEquipment.StageBagAnchorName);
                Assert.That(bagAnchor, Is.Not.Null);
                Assert.That(bagAnchor.IsChildOf(Find(scene, "INT")), Is.True);
                Assert.That(Find(scene, "ENV_LeakPipeGateValveWheel_suc")
                    .GetComponent<ValveWheelClosureController>(), Is.Not.Null);
                Assert.That(Find(scene, "ENV_LeakPipeGateValveWheel_dis")
                    .GetComponent<ValveWheelClosureController>(), Is.Not.Null);

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                    {
                        Assert.That(
                            GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                                candidate.gameObject),
                            Is.Zero,
                            $"Missing script at {GetPath(candidate)}.");
                    }

                    foreach (Component component in root.GetComponentsInChildren<Component>(true))
                    {
                        if (component == null)
                            continue;
                        string typeName = component.GetType().FullName ?? string.Empty;
                        Assert.That(typeName, Does.Not.Contain("VRHandPoseTool"),
                            $"Removed Hand Pose Tool component remains: {typeName}.");
                    }
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void P102EnvironmentSetup_KeepsAuthoredRigFixedAndCreatesBagAnchor()
        {
            Scene scene = EditorSceneManager.NewPreviewScene();

            try
            {
                Transform functionalRig = CreateRoot(scene, "INT", new Vector3(1f, 0f, 2f));
                Transform p102Pump = CreateRoot(scene, "Pump_102", new Vector3(4f, 0f, 0f));
                Transform runtimeRoot = CreateRoot(scene, "LeakResponse Runtime", Vector3.zero);
                P102LeakEnvironmentSetup setup =
                    runtimeRoot.gameObject.AddComponent<P102LeakEnvironmentSetup>();

                Assert.That(setup.Configure(scene), Is.True);
                Assert.That(setup.FunctionalRig, Is.SameAs(functionalRig));
                Assert.That(setup.P102PumpRoot, Is.SameAs(p102Pump));
                Assert.That(functionalRig.position, Is.EqualTo(new Vector3(1f, 0f, 2f)));
                Assert.That(p102Pump.position, Is.EqualTo(new Vector3(4f, 0f, 0f)));
                Assert.That(setup.StageBagAnchor, Is.Not.Null);
                Assert.That(setup.StageBagAnchor.IsChildOf(functionalRig), Is.True);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void SpatialLeakVolume_IsSilentAtZeroAndMonotonic()
        {
            float silent = LeakResponseSpatialLeakAudio.ResolveVolume(0f);
            float medium = LeakResponseSpatialLeakAudio.ResolveVolume(0.25f);
            float full = LeakResponseSpatialLeakAudio.ResolveVolume(1f);

            Assert.That(silent, Is.Zero);
            Assert.That(medium, Is.GreaterThan(silent));
            Assert.That(full, Is.GreaterThan(medium));
            Assert.That(LeakResponseSpatialLeakAudio.ResolveVolume(5f), Is.EqualTo(full));
        }

        [Test]
        public void RuntimeGhostMaterial_IsTransparentDoubleSidedAndNotCustomHandShader()
        {
            Material material = P102RuntimeUtility.CreateCyanMaterial(
                "Stereo Safe Ghost Test", new Color(0.1f, 0.9f, 1f, 0.38f));
            try
            {
                Assert.That(material, Is.Not.Null);
                Assert.That(material.renderQueue,
                    Is.GreaterThanOrEqualTo((int)UnityEngine.Rendering.RenderQueue.Transparent));
                if (material.HasProperty("_ZWrite"))
                    Assert.That(material.GetFloat("_ZWrite"), Is.Zero);
                if (material.HasProperty("_Cull"))
                    Assert.That(material.GetFloat("_Cull"), Is.Zero);
                Assert.That(material.shader.name, Does.Not.Contain("HandGhost"));
            }
            finally
            {
                if (material != null)
                    UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void TelemetryReporter_DeduplicatesStepAndContentEvents()
        {
            var telemetry = new TelemetryProbe();
            var incident = new IncidentProbe();
            SharedTrainingTelemetry.Register(telemetry);
            SharedIncidentState.Register(incident);
            GameObject reporterObject = new GameObject("LeakResponse Telemetry Test");
            LeakResponseTelemetryReporter reporter =
                reporterObject.AddComponent<LeakResponseTelemetryReporter>();

            try
            {
                Invoke(reporter, "OnCueStarted",
                    P102LeakNarrationCue.AbsorbentSockPlacementActivated);
                Invoke(reporter, "OnCueStarted",
                    P102LeakNarrationCue.AbsorbentSockPlacementActivated);
                Invoke(reporter, "OnCueCompleted",
                    P102LeakNarrationCue.AllAbsorbentSocksPlaced);
                Invoke(reporter, "OnCueCompleted",
                    P102LeakNarrationCue.AllAbsorbentSocksPlaced);
                Invoke(reporter, "OnCueCompleted", P102LeakNarrationCue.BothValvesClosed);

                Assert.That(telemetry.Entered, Is.EquivalentTo(new[]
                {
                    TrainingStepId.AbsorbentSock,
                    TrainingStepId.DischargeValve,
                }));
                Assert.That(telemetry.Completed, Is.EquivalentTo(new[]
                {
                    TrainingStepId.AbsorbentSock,
                    TrainingStepId.DischargeValve,
                }));
                Assert.That(telemetry.ContentIds.Count, Is.EqualTo(2));
                Assert.That(incident.LeakState, Is.EqualTo(LeakState.Isolated));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(reporterObject);
                SharedTrainingTelemetry.Unregister(telemetry);
                SharedIncidentState.Unregister(incident);
            }
        }

        static Transform CreateRoot(Scene scene, string name, Vector3 position)
        {
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = position;
            return root.transform;
        }

        static Transform Find(Scene scene, string exactName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
                {
                    if (string.Equals(candidate.name, exactName, StringComparison.Ordinal))
                        return candidate;
                }
            }
            return null;
        }

        static string GetPath(Transform target)
        {
            string path = target.name;
            while (target.parent != null)
            {
                target = target.parent;
                path = target.name + "/" + path;
            }
            return path;
        }

        static void Invoke(object target, string methodName, object argument)
        {
            MethodInfo method = target.GetType().GetMethod(
                methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Missing method {methodName}.");
            method.Invoke(target, new[] { argument });
        }

        sealed class IncidentProbe : ISharedIncidentState
        {
            public FacilityRunState FacilityRunState { get; private set; } =
                FacilityRunState.Running;
            public LeakState LeakState { get; private set; } = LeakState.Active;
            public bool SetFacilityRunState(FacilityRunState state)
            {
                FacilityRunState = state;
                return true;
            }
            public bool SetLeakState(LeakState state)
            {
                LeakState = state;
                return true;
            }
        }

        sealed class TelemetryProbe : IStageTrainingTelemetry
        {
            public readonly List<TrainingStepId> Entered = new List<TrainingStepId>();
            public readonly List<TrainingStepId> Completed = new List<TrainingStepId>();
            public readonly List<string> ContentIds = new List<string>();
            public void StepEntered(TrainingStepId step) => Entered.Add(step);
            public void StepCompleted(TrainingStepId step, int attemptCount = 0,
                int rejectedCount = 0) => Completed.Add(step);
            public void ContentViewed(string contentId) => ContentIds.Add(contentId);
            public void BagItemRequested(string equipmentId, bool success) { }
            public void EquipmentUsed(string equipmentId) { }
        }
    }
}
#endif
