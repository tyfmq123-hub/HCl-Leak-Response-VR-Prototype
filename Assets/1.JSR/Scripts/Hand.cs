using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

public class Hand : MonoBehaviour
{
    public InputDeviceCharacteristics inputDeviceCharacteristics;

    [SerializeField] private Animator _handAnimator;

    private InputAction _gripAction;

    private void Awake()
    {
        if (_handAnimator == null)
            _handAnimator = GetComponent<Animator>();
    }

    private void OnEnable()
    {
        _gripAction = CreateGripAction(ResolveHandUsage());
        _gripAction.Enable();
    }

    private void OnDisable()
    {
        _gripAction?.Disable();
        _gripAction?.Dispose();
        _gripAction = null;
    }

    private void Update()
    {
        if (_handAnimator == null || _gripAction == null)
            return;

        _handAnimator.SetFloat("Grip", _gripAction.ReadValue<float>());
    }

    private static InputAction CreateGripAction(string handUsage)
    {
        return new InputAction(
            name: $"HandGrip_{handUsage}",
            type: InputActionType.Value,
            binding: $"<XRController>{{{handUsage}}}/grip",
            expectedControlType: "Axis");
    }

    private string ResolveHandUsage()
    {
        if (inputDeviceCharacteristics.HasFlag(InputDeviceCharacteristics.Left))
            return "LeftHand";
        if (inputDeviceCharacteristics.HasFlag(InputDeviceCharacteristics.Right))
            return "RightHand";

        for (Transform t = transform; t != null; t = t.parent)
        {
            if (t.name.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "LeftHand";
            if (t.name.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return "RightHand";
        }

        Debug.LogWarning($"[Hand] '{name}' could not resolve hand side; defaulting to LeftHand.", this);
        return "LeftHand";
    }
}
