using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ResponseBagUnityReadyBindSetupTrigger
{
    const string TriggerPath = "Assets/Scripts/Editor/RunResponseBagUnityReadyBind.trigger.txt";

    static ResponseBagUnityReadyBindSetupTrigger()
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
            "[Response Bag] Auto-bind on domain reload is disabled. " +
            "Run Tools > PPE > Bind ResponseBag UnityReady Model manually.");
    }
}
