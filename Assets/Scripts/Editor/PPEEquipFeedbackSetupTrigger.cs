using System.IO;
using UnityEditor;

[InitializeOnLoad]
public static class PPEEquipFeedbackSetupTrigger
{
    const string TriggerPath = "Assets/Scripts/Editor/RunPPEEquipFeedbackSetup.trigger.txt";

    static PPEEquipFeedbackSetupTrigger()
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

        PPEEquipFeedbackSetup.SetupEquipFeedbackUI();
    }
}
