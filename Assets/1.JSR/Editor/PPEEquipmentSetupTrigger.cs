using System.IO;
using UnityEditor;

/// <summary>
/// Auto-runs equipment setup when the trigger file appears (agent / batch apply).
/// </summary>
[InitializeOnLoad]
public static class PPEEquipmentSetupTrigger
{
    const string TriggerPath = "Assets/1.JSR/Editor/RunPPEEquipmentSetup.trigger.txt";

    static PPEEquipmentSetupTrigger()
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

        PPEEquipmentSetup.SetupEquipmentSystem();
    }
}
