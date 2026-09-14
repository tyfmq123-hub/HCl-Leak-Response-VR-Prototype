#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HClTraining.App;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class ProjectorFloorCompletionBoardRegressionTests
{
    const string FloorMaterialPath = "Assets/3.SSH/Materials/M_LeakPumpFloor_Matte.mat";
    const string LeakResponseScenePath = "Assets/Scenes/06_LeakResponse.unity";
    const string CompleteMenuPresenterPath = "Assets/Scripts/App/CompleteMenuPresenter.cs";

    [Test]
    public void CompletionUi_UsesPhysicalWhiteboardWithoutLegacyOpaqueFrame()
    {
        var source = File.ReadAllText(CompleteMenuPresenterPath);

        Assert.That(source, Does.Not.Contain("\"CompletionFrame\""),
            "The physical Title whiteboard replaces the old full-canvas completion frame.");
        Assert.That(source, Does.Contain("inset: 0.99f"),
            "Completion content must use nearly the full authored whiteboard surface.");
        Assert.That(source, Does.Contain("\"CompletionPanel\"").And.Contain("Color.clear"),
            "The completion layout root must remain transparent over the physical whiteboard.");
    }

    [TestCase(
        SceneFlowController.StartScenePath,
        TrainingStage.None,
        SceneFlowController.TitleScenePath,
        SceneFlowController.StartScenePath)]
    [TestCase(
        SceneFlowController.CompleteScenePath,
        TrainingStage.Complete,
        SceneFlowController.TitleScenePath,
        SceneFlowController.CompleteScenePath)]
    [TestCase(
        SceneFlowController.PreEntryScenePath,
        TrainingStage.PreEntry,
        SceneFlowController.LeakResponseScenePath,
        SceneFlowController.PreEntryScenePath)]
    [TestCase(
        SceneFlowController.LeakResponseScenePath,
        TrainingStage.LeakResponse,
        SceneFlowController.PreEntryScenePath,
        SceneFlowController.LeakResponseScenePath)]
    public void SceneLoadPlan_KeepsAuthoredDependenciesBeforePrimaryScene(
        string primaryScenePath,
        TrainingStage stage,
        string expectedDependency,
        string expectedPrimary)
    {
        var method = typeof(SceneFlowController).GetMethod(
            "BuildLoadPlan",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null,
            "Scene flow must retain its centralized load-plan builder.");

        var plan = method.Invoke(null, new object[] { primaryScenePath, stage });
        Assert.That(plan, Is.Not.Null);
        var buildLoadOrder = plan.GetType().GetMethod(
            "BuildLoadOrder",
            BindingFlags.Instance | BindingFlags.Public);
        Assert.That(buildLoadOrder, Is.Not.Null);

        var loadOrder = buildLoadOrder.Invoke(plan, null) as List<string>;
        Assert.That(loadOrder, Is.Not.Null);
        CollectionAssert.AreEqual(
            new[] { expectedDependency, expectedPrimary },
            loadOrder,
            "Dependency scenes must load before the requested primary scene.");
    }

    [Test]
    public void PumpFloorMaterial_IsOpaqueDarkMatteAndDisablesReflections()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);
        Assert.That(material, Is.Not.Null, FloorMaterialPath);
        Assert.That(material.shader, Is.Not.Null);
        Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));

        AssertFloat(material, "_Surface", 0f);
        AssertFloat(material, "_Metallic", 0f);
        AssertFloat(material, "_Smoothness", 0f);
        AssertFloat(material, "_SpecularHighlights", 0f);
        AssertFloat(material, "_EnvironmentReflections", 0f);
        Assert.That(material.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"), Is.True);
        Assert.That(material.IsKeywordEnabled("_ENVIRONMENTREFLECTIONS_OFF"), Is.True);

        Assert.That(material.HasProperty("_BaseColor"), Is.True);
        var baseColor = material.GetColor("_BaseColor");
        Assert.That(baseColor.r, Is.EqualTo(0.35f).Within(0.001f));
        Assert.That(baseColor.g, Is.EqualTo(0.37f).Within(0.001f));
        Assert.That(baseColor.b, Is.EqualTo(0.38f).Within(0.001f));
        Assert.That(baseColor.a, Is.EqualTo(1f).Within(0.001f));
    }

    [Test]
    public void LeakResponseDikeFloor_UsesDedicatedMaterialAndKeepsAuthoredCollisionGeometry()
    {
        var existingScene = SceneManager.GetSceneByPath(LeakResponseScenePath);
        var wasLoaded = existingScene.IsValid() && existingScene.isLoaded;
        var scene = wasLoaded
            ? existingScene
            : EditorSceneManager.OpenScene(LeakResponseScenePath, OpenSceneMode.Additive);
        var wasDirty = scene.isDirty;

        try
        {
            var floor = FindTransform(scene, "ENV_DikeFloor");
            Assert.That(floor, Is.Not.Null);
            AssertVector3(floor.localPosition, new Vector3(-0.39262f, 0.074f, 0.0054303f));
            Assert.That(Quaternion.Angle(floor.localRotation, Quaternion.identity),
                Is.LessThan(0.001f), "ENV_DikeFloor local rotation");
            AssertVector3(floor.localScale, new Vector3(15.392902f, 0.15579803f, 8.9615345f));

            var renderer = floor.GetComponent<MeshRenderer>();
            Assert.That(renderer, Is.Not.Null);
            Assert.That(renderer.sharedMaterials, Has.Length.EqualTo(1));
            Assert.That(
                AssetDatabase.GetAssetPath(renderer.sharedMaterial),
                Is.EqualTo(FloorMaterialPath));

            var collider = floor.GetComponent<BoxCollider>();
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.enabled, Is.True);
            Assert.That(collider.isTrigger, Is.False);
            AssertVector3(collider.center, Vector3.zero);
            AssertVector3(collider.size, Vector3.one);
            Assert.That(scene.isDirty, Is.EqualTo(wasDirty),
                "The regression test must not modify the authored scene.");
        }
        finally
        {
            if (!wasLoaded && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    static void AssertFloat(Material material, string propertyName, float expected)
    {
        Assert.That(material.HasProperty(propertyName), Is.True, propertyName);
        Assert.That(material.GetFloat(propertyName), Is.EqualTo(expected).Within(0.0001f),
            propertyName);
    }

    static void AssertVector3(Vector3 actual, Vector3 expected)
    {
        Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.0001f),
            $"Expected {expected}, but was {actual}.");
    }

    static Transform FindTransform(Scene scene, string exactName)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
                if (transforms[i] != null && transforms[i].name == exactName)
                    return transforms[i];
        }

        return null;
    }
}
#endif
