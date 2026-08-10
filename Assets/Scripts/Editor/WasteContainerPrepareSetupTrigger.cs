using System.IO;
using UnityEditor;

[InitializeOnLoad]
public static class WasteContainerPrepareSetupTrigger
{
    const string TriggerPath = "Assets/Scripts/Editor/RunWasteContainerPrepareSetup.trigger.txt";

    static WasteContainerPrepareSetupTrigger()
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

        WasteContainerPrepareSetup.SetupWasteContainerTakeOut();
    }
}
