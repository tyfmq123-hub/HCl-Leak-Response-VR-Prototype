using UnityEditor;
using UnityEngine;

public static class PPEInnerGloveDebugCommands
{
    public static void ForceCompleteLeftInnerGloveMember()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[PPE InnerGlove] Enter Play Mode before Force Complete.");
            return;
        }

        var coordinators = Object.FindObjectsByType<PPEGlovePairCoordinator>(FindObjectsInactive.Include);
        for (var i = 0; i < coordinators.Length; i++)
        {
            var coordinator = coordinators[i];
            if (coordinator == null || coordinator.LogicalItem == null)
                continue;
            if (!string.Equals(
                    coordinator.LogicalItem.ItemId,
                    PPEEquipmentManager.IdInnerGloves,
                    System.StringComparison.Ordinal))
                continue;

            coordinator.DebugForceCompleteLeftInnerGloveMember();
            return;
        }

        Debug.LogWarning("[PPE InnerGlove] No InnerGloves pair coordinator found.");
    }

    public static void AuthorInnerGloveEquipReferences()
    {
        var coordinators = Object.FindObjectsByType<PPEGlovePairCoordinator>(FindObjectsInactive.Include);
        var authored = 0;
        for (var i = 0; i < coordinators.Length; i++)
        {
            var coordinator = coordinators[i];
            if (coordinator == null || coordinator.LogicalItem == null)
                continue;
            if (!string.Equals(
                    coordinator.LogicalItem.ItemId,
                    PPEEquipmentManager.IdInnerGloves,
                    System.StringComparison.Ordinal))
                continue;

            authored += AuthorMember(coordinator.LeftMember);
            authored += AuthorMember(coordinator.RightMember);
        }

        Debug.Log("[PPE InnerGlove] Authored EquipReference on " + authored + " members.");
    }

    static int AuthorMember(PPEGlovePairMember member)
    {
        if (member == null)
            return 0;
        Undo.RecordObject(member, "Author InnerGlove EquipReference");
        var reference = member.EnsureEquipReference();
        if (reference == null)
            return 0;
        EditorUtility.SetDirty(member);
        EditorUtility.SetDirty(reference.gameObject);
        return 1;
    }
}
