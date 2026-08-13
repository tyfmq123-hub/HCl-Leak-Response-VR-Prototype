using System.IO;
using UnityEditor;

/// <summary>
/// Auto-runs PPE room locomotion setup when the trigger file appears.
/// </summary>
[InitializeOnLoad]
public static class PPERoomLocomotionSetupTrigger
{
    const string TriggerPath = "Assets/1.JSR/Editor/RunPPERoomLocomotionSetup.trigger.txt";

    static PPERoomLocomotionSetupTrigger()
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

        PPERoomLocomotionSetup.SetupRoomLocomotion();
    }
}
