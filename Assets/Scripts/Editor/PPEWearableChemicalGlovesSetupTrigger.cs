using System.IO;
using UnityEditor;

[InitializeOnLoad]
public static class PPEWearableChemicalGlovesSetupTrigger
{
    const string TriggerPath = "Assets/Scripts/Editor/RunPPEWearableChemicalGlovesSetup.trigger.txt";

    static PPEWearableChemicalGlovesSetupTrigger()
    {
        EditorApplication.delayCall += TryRun;
    }

    static void TryRun()
    {
        if (!File.Exists(TriggerPath))
            return;

        var mode = "vrhands";
        try
        {
            mode = File.ReadAllText(TriggerPath).Trim();
            File.Delete(TriggerPath);
            if (File.Exists(TriggerPath + ".meta"))
                File.Delete(TriggerPath + ".meta");
        }
        catch
        {
            // ignored
        }

        if (mode.IndexOf("align", System.StringComparison.OrdinalIgnoreCase) >= 0)
            PPEWearableChemicalGlovesSetup.AlignWearableGlovesToXrHands();
        else if (mode.IndexOf("chemical", System.StringComparison.OrdinalIgnoreCase) >= 0)
            PPEWearableChemicalGlovesSetup.RestoreWearableChemicalGloveFbx();
        else
            PPEWearableChemicalGlovesSetup.ReplacePpeWearableWithVrHands();
    }
}
