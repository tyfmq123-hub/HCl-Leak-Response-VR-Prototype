using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Auto-runs checklist panel setup when the trigger file appears.</summary>
[InitializeOnLoad]
public static class PPEChecklistPanelSetupTrigger
{
    const string TriggerPath = "Assets/1.JSR/Editor/RunPPEChecklistPanelSetup.trigger.txt";

    static PPEChecklistPanelSetupTrigger()
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
        catch
        {
            // ignored
        }

        Debug.Log("[PPE Checklist] Trigger detected ??running SetupChecklistPanel.");
        PPEChecklistPanelSetup.SetupChecklistPanel();
    }
}

