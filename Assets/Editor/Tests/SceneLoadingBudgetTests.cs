#if UNITY_INCLUDE_TESTS
using HClTraining.App;
using NUnit.Framework;
using UnityEngine;

public sealed class SceneLoadingBudgetTests
{
    ThreadPriority original;
    [SetUp] public void SetUp() => original = Application.backgroundLoadingPriority;
    [TearDown] public void TearDown() => Application.backgroundLoadingPriority = original;

    [TestCase(ThreadPriority.Low)]
    [TestCase(ThreadPriority.BelowNormal)]
    [TestCase(ThreadPriority.Normal)]
    [TestCase(ThreadPriority.High)]
    public void Scope_UsesLowBudgetAndRestoresExactOriginal(ThreadPriority priority)
    {
        Application.backgroundLoadingPriority = priority;
        using (new SceneLoadingBudget())
            Assert.That(Application.backgroundLoadingPriority, Is.EqualTo(ThreadPriority.Low));
        Assert.That(Application.backgroundLoadingPriority, Is.EqualTo(priority));
    }

    [Test]
    public void Scope_ExceptionRestoresPriority()
    {
        Application.backgroundLoadingPriority = ThreadPriority.Normal;
        Assert.Throws<System.InvalidOperationException>(() =>
        {
            using var scope = new SceneLoadingBudget();
            throw new System.InvalidOperationException("Simulated load failure");
        });
        Assert.That(Application.backgroundLoadingPriority, Is.EqualTo(ThreadPriority.Normal));
    }

    [Test]
    public void Scope_DisposeIsIdempotentAndDoesNotOverwriteAnotherOwner()
    {
        using var scope = new SceneLoadingBudget();
        Application.backgroundLoadingPriority = ThreadPriority.High;
        scope.Dispose();
        scope.Dispose();
        Assert.That(Application.backgroundLoadingPriority, Is.EqualTo(ThreadPriority.High));
    }

    [Test]
    public void Mirror_NoPostRenderEyeTextureOverwriteIsInstalled()
    {
        // The previous synthetic shader tests never exercised an actual XR provider.
        // Keep the unsupported Editor override removed; rendering belongs to Unity's XR mirror.
        Assert.That(System.Type.GetType("XRGameViewFullFrame, Assembly-CSharp-Editor"), Is.Null);
        Assert.That(System.IO.File.Exists("Assets/Editor/XRGameViewFullFrame.shader"), Is.False);
    }
}
#endif
