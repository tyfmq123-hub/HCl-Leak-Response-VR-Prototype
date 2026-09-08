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
            Assert.AreEqual(PPEInnerGloveHandTint.WornHandColor, worn.GetColor("_BaseColor"));
            Assert.AreEqual(PPEInnerGloveHandTint.WornHandColor, worn.GetColor("_Color"));

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
}
#endif
