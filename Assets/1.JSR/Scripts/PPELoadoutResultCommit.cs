using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// PPE-room helper: wire a Confirm / Proceed button to
/// <see cref="CommitResult"/> so the loadout is finalized once as
/// <see cref="PPELoadoutSource.PpeSceneResult"/>.
/// Do not place <see cref="PPELoadoutSceneBootstrap"/> in the PPE room.
/// Do not Clear the loadout on scene transition (Integration owns scene flow).
/// </summary>
[DisallowMultipleComponent]
public class PPELoadoutResultCommit : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Invoked after a successful commit (Source=PpeSceneResult).")]
    UnityEvent onCommitted;

    [SerializeField]
    bool logCommit = true;

    /// <summary>
    /// Builds one canonical snapshot from current PPE managers and replaces DDOL state.
    /// Call exactly once when the trainee confirms leaving the PPE room.
    /// </summary>
    public void CommitResult()
    {
        var data = PPELoadoutPresets.CommitPpeSceneResult();
        if (data == null)
        {
            Debug.LogWarning("[PPE Loadout Commit] Commit skipped (loadout unavailable).", this);
            return;
        }

        if (logCommit)
        {
            Debug.Log(
                $"[PPE Loadout Commit] source={data.Source}, " +
                $"equipped={data.EquippedItemIds.Count}, prepared={data.PreparedItemIds.Count}, " +
                $"sequenceComplete={data.IsSequenceComplete}, bagChecked={data.BagContentsChecked}",
                this);
        }

        onCommitted?.Invoke();
    }
}
