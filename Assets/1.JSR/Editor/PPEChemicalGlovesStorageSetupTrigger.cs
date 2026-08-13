using System.IO;
using UnityEditor;

/// <summary>
/// Auto-runs chemical gloves → PPE_Storage install when the trigger file appears.
/// </summary>
[InitializeOnLoad]
public static class PPEChemicalGlovesStorageSetupTrigger
{
    const string TriggerPath = "Assets/1.JSR/Editor/RunPPEChemicalGlovesStorageSetup.trigger.txt";

    static PPEChemicalGlovesStorageSetupTrigger()
    {
        EditorApplication.delayCall += TryRun;
    }

    static void TryRun()
    {
        if (!File.Exists(TriggerPath))
            return;

        var mode = "install";
        try
        {
            mode = File.ReadAllText(TriggerPath).Trim();
        }
        catch
        {
            // ignored
        }

        try
        {
            File.Delete(TriggerPath);
            if (File.Exists(TriggerPath + ".meta"))
                File.Delete(TriggerPath + ".meta");
        }
        catch
        {
            // ignored
        }

        if (mode.IndexOf("fix-mesh", System.StringComparison.OrdinalIgnoreCase) >= 0)
            PPEChemicalGlovesStorageSetup.FixGlovesMeshColliderAndOutline();
        else
            PPEChemicalGlovesStorageSetup.InstallChemicalGlovesIntoStorage();
    }
}
