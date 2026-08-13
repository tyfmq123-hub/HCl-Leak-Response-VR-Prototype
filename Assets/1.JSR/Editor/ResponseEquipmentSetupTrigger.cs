using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Trigger file watcher is intentionally disabled for automatic scene mutation.
/// Use Tools > PPE > Setup Response Bag Integration instead.
/// </summary>
[InitializeOnLoad]
public static class ResponseEquipmentSetupTrigger
{
    const string TriggerPath = "Assets/1.JSR/Editor/RunResponseEquipmentSetup.trigger.txt";

    static ResponseEquipmentSetupTrigger()
    {
        EditorApplication.delayCall += CleanupStaleTriggerOnly;
    }

    static void CleanupStaleTriggerOnly()
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

        Debug.LogWarning(
            "[Response Bag] Auto-setup on domain reload is disabled. " +
            "Run Tools > PPE > Setup Response Bag Integration manually.");
    }
}
