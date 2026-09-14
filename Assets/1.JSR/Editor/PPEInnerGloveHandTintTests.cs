#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;

public sealed class PPEInnerGloveHandTintTests
{
    [Test]
    public void ApplyToHand_TintsThenRestoresDefaultHandColor()
    {
        var hand = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            var renderer = hand.GetComponent<Renderer>();
            Assert.IsNotNull(renderer);

            PPEInnerGloveHandTint.ApplyToHand(hand, true);
            var worn = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(worn);
            AssertColor(PPEInnerGloveHandTint.WornHandColor, worn.GetColor("_BaseColor"), "_BaseColor");
            AssertColor(PPEInnerGloveHandTint.WornHandColor, worn.GetColor("_Color"), "_Color");

            PPEInnerGloveHandTint.ApplyToHand(hand, false);
            var restored = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(restored);
            Assert.AreEqual(Color.clear, restored.GetColor("_BaseColor"));
        }
        finally
        {
            Object.DestroyImmediate(hand);
        }
    }

    static void AssertColor(Color expected, Color actual, string property)
    {
        // MaterialPropertyBlock color-space round trips can alter the last float
        // bits. Check every channel tightly without requiring bitwise equality.
        const float tolerance = 0.00001f;
        Assert.That(actual.r, Is.EqualTo(expected.r).Within(tolerance), property + ".r");
        Assert.That(actual.g, Is.EqualTo(expected.g).Within(tolerance), property + ".g");
        Assert.That(actual.b, Is.EqualTo(expected.b).Within(tolerance), property + ".b");
        Assert.That(actual.a, Is.EqualTo(expected.a).Within(tolerance), property + ".a");
    }
}
#endif
