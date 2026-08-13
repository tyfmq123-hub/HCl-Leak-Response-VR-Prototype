using System.IO;
using UnityEditor;

/// <summary>
/// Auto-runs ray interactable setup when the trigger file appears (agent / batch apply).
/// Domain reload picks up the trigger on the next Editor delayCall.
/// </summary>
[InitializeOnLoad]
public static class PPERayInteractableSetupTrigger
{
    const string TriggerPath = "Assets/1.JSR/Editor/RunPPERayInteractableSetup.trigger.txt";

    static PPERayInteractableSetupTrigger()
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

        PPERayInteractableSetup.SetupRayInteractables();
    }
}
