using UnityEngine;

/// <summary>
/// Toggles a highlight visual. Contains no XR interaction logic.
/// Prefer assigning a child outline / highlight GameObject in the Inspector.
/// </summary>
public class PPEHighlight : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Child outline mesh, dedicated highlight GameObject, or similar visual.")]
    GameObject highlightObject;

    bool warnedMissing;

    void Awake()
    {
        if (highlightObject == null)
            WarnMissing();

        SetHighlighted(false);
    }

    void OnDisable()
    {
        SetHighlighted(false);
    }

    public void SetHighlighted(bool highlighted)
    {
        if (highlightObject == null)
        {
            if (highlighted)
                WarnMissing();
            return;
        }

        if (highlightObject.activeSelf != highlighted)
            highlightObject.SetActive(highlighted);
    }

    void WarnMissing()
    {
        if (warnedMissing)
            return;

        warnedMissing = true;
        Debug.LogWarning(
            $"PPEHighlight on '{name}' has no Highlight Object assigned. Assign a child highlight visual.",
            this);
    }
}
