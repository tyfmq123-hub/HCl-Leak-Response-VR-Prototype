using System.IO;
using UnityEditor;

/// <summary>
/// Auto-runs outline setup when trigger file appears (for agent-driven apply).
/// </summary>
[InitializeOnLoad]
public static class PPEInteractableOutlineSetupTrigger
{
    const string TriggerPath = "Assets/Scripts/Editor/RunPPEOutlineSetup.trigger.txt";

    static PPEInteractableOutlineSetupTrigger()
    {
        EditorApplication.delayCall += TryRun;
    }

    static void TryRun()
    {
        if (!File.Exists(TriggerPath))
            return;

        try
        {
            File.Delete(TriggerPath);
            if (File.Exists(TriggerPath + ".meta"))
                File.Delete(TriggerPath + ".meta");
        }
        catch { /* ignored */ }

        PPEInteractableOutlineSetup.SetupInteractableOutlines();
    }
}
