using System;

/// <summary>
/// Extensible registry of PPE items that should receive ray-select interaction.
/// Add a new <see cref="Entry"/> when new PPE equipment is introduced later.
/// Matching uses case-insensitive substring on GameObject names (FBX suffixes OK).
/// </summary>
public static class PPERayInteractableCatalog
{
    /// <summary>One registrable PPE target pattern.</summary>
    public readonly struct Entry
    {
        /// <summary>Stable id used in setup reports.</summary>
        public readonly string Id;

        /// <summary>Partial name match (e.g. "PPE_ChemicalSuit").</summary>
        public readonly string NameContains;

        /// <summary>Human-readable label for logs.</summary>
        public readonly string DisplayName;

        public Entry(string id, string nameContains, string displayName)
        {
            Id = id;
            NameContains = nameContains;
            DisplayName = displayName;
        }
    }

    /// <summary>
    /// Edit this list to support additional PPE objects later.
    /// The setup tool configures only top-level roots matching these patterns
    /// (not child mesh parts, slots, cabinet, or room geometry).
    /// </summary>
    public static readonly Entry[] Targets =
    {
        new Entry("chemical_suit", "PPE_ChemicalSuit", "Chemical suit"),
        new Entry("chemical_boots", "PPE_ChemicalSafetyBoots", "Chemical safety boots (pair)"),
        new Entry("face_respirator", "PPE_FaceRespirator", "Face respirator"),
        new Entry("chemical_face_shield", "PPE_ChemicalFaceShield", "Chemical face shield"),
        new Entry("inner_gloves", "PPE_InnerGloves", "Inner chemical gloves (first)"),
        new Entry("chemical_gloves", "PPE_ChemicalGloves", "Chemical gloves"),
        // Scene alias used by the current gloves root (PPE_Gloves).
        new Entry("gloves_alias", "PPE_Gloves", "Gloves (scene alias)"),
    };

    /// <summary>Exact names that must never receive PPE ray interactables.</summary>
    public static readonly string[] ExcludeExactNames =
    {
        "PPE_Storage",
        "PPE_Cabinet",
        "PPE_System",
        "PPEManager",
        "PPE_EquipmentManager",
        "PPE_SelectionManager",
        "PPE_EquipFeedbackCanvas",
        "PPE_LayoutRoot",
        "SuitSlot",
        "BootsSlot",
        "RespiratorSlot",
        "FaceShieldSlot",
        "GlovesSlot",
        "InnerGlovesSlot",
        "Floor",
        "Ceiling",
        "Directional Light",
        "XR Interaction Manager",
        "XR Origin",
        "Main Camera",
        "Camera Offset",
        "Left Controller",
        "Right Controller",
    };

    /// <summary>Name prefixes treated as environment / non-PPE.</summary>
    public static readonly string[] ExcludeNamePrefixes =
    {
        "Wall_",
        "ENV_",
    };

    public static bool MatchesAnyTarget(string objectName, out Entry matched)
    {
        matched = default;
        if (string.IsNullOrEmpty(objectName))
            return false;

        for (var i = 0; i < Targets.Length; i++)
        {
            var entry = Targets[i];
            if (objectName.IndexOf(entry.NameContains, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                matched = entry;
                return true;
            }
        }

        return false;
    }

    public static bool IsExcludedEnvironmentName(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return true;

        for (var i = 0; i < ExcludeExactNames.Length; i++)
        {
            if (string.Equals(objectName, ExcludeExactNames[i], StringComparison.Ordinal))
                return true;
        }

        for (var i = 0; i < ExcludeNamePrefixes.Length; i++)
        {
            if (objectName.StartsWith(ExcludeNamePrefixes[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
