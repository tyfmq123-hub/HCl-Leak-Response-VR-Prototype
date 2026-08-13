using System.IO;
using UnityEditor;

/// <summary>
/// One-shot trigger for Tools > PPE > Setup Wearable Response Bag Visual States.
/// </summary>
public static class WearableResponseBagVisualStatesSetupTrigger
{
    const string TriggerPath =
        "Assets/1.JSR/Editor/RunWearableResponseBagVisualStatesSetup.trigger.txt";

    [InitializeOnLoadMethod]
    static void AutoRunIfTriggered()
    {
        if (!File.Exists(TriggerPath))
            return;

        EditorApplication.delayCall += () =>
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

            WearableResponseBagVisualStatesSetup.SetupVisualStates();
        };
    }
}
