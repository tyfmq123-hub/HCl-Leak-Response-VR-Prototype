using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Table-to-bag packing item. Uses XRI grab on the existing content root and
/// reports Release to <see cref="ResponseBagPackingController"/>. Not wearable PPE.
/// </summary>
[DisallowMultipleComponent]
public sealed class ResponseBagPackableItem : MonoBehaviour
{
    [SerializeField] string itemId;
    [SerializeField] XRGrabInteractable grab;
    [SerializeField] Rigidbody body;

    ResponseBagPackingController packing;
    bool packed;

    public string ItemId => itemId;
    public bool IsPacked => packed;
    public XRGrabInteractable Grab => grab;
    public Rigidbody Body => body;

    public void EditorWire(string id, ResponseBagPackingController controller)
    {
        itemId = id;
        packing = controller;
        EnsureGrab();
    }

    void Awake()
    {
        EnsureGrab();
    }

    void OnEnable()
    {
        EnsureGrab();
        if (grab != null)
        {
            grab.selectExited.RemoveListener(OnSelectExited);
            grab.selectExited.AddListener(OnSelectExited);
        }
    }

    void Start()
    {
        if (!packed)
            ApplyRestingPhysics();
    }

    void OnDisable()
    {
        if (grab != null)
            grab.selectExited.RemoveListener(OnSelectExited);
    }

    public void SetPacked(bool value)
    {
        packed = value;
        if (grab != null)
            grab.enabled = !value;
        if (value)
        {
            ApplyRestingPhysics();
            StopAllCoroutines();
            gameObject.SetActive(false);
        }
        else
        {
            gameObject.SetActive(true);
        }
    }

    public void ApplyRestingPhysics()
    {
        EnsureGrab();
        if (body == null)
            return;
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.isKinematic = true;
        body.useGravity = false;
        IgnorePlayerLocomotionCollisions();
    }

    public void IgnorePlayerLocomotionCollisions()
    {
        var origin = PPEActiveXrOrigin.Resolve();
        CharacterController controller = null;
        if (origin != null)
        {
            controller = origin.GetComponent<CharacterController>();
            if (controller == null)
                controller = origin.GetComponentInChildren<CharacterController>(true);
            if (controller == null && origin.Origin != null)
            {
                controller = origin.Origin.GetComponent<CharacterController>();
                if (controller == null)
                    controller = origin.Origin.GetComponentInChildren<CharacterController>(true);
            }
        }

        var colliders = GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            var col = colliders[i];
            if (col == null || !col.enabled || col.isTrigger)
                continue;
            // Keep the authored Physics layer: Near/Far discovers candidates
            // through its ray/cast mask. Only locomotion collision is suppressed.
            if (controller != null)
                Physics.IgnoreCollision(col, controller, true);
        }
    }

    public void ApplyFreePhysics()
    {
        if (packed)
            return;
        EnsureGrab();
        if (body == null)
            return;
        body.isKinematic = false;
        body.useGravity = true;
        IgnorePlayerLocomotionCollisions();
    }

    void OnSelectExited(SelectExitEventArgs _)
    {
        if (packed)
            return;
        if (packing == null)
            packing = ResponseBagPackingController.Instance;
        packing?.NotifyItemReleased(this);
        if (!packed)
            ApplyFreePhysics();
    }

    void EnsureGrab()
    {
        if (grab == null)
            grab = GetComponent<XRGrabInteractable>();
        if (grab == null)
            grab = gameObject.AddComponent<XRGrabInteractable>();

        if (body == null)
            body = GetComponent<Rigidbody>();
        if (body == null)
            body = gameObject.AddComponent<Rigidbody>();

        grab.throwOnDetach = false;
        grab.useDynamicAttach = true;
        grab.movementType = XRGrabInteractable.MovementType.Kinematic;
        var col = GetComponent<Collider>();
        if (col != null && !grab.colliders.Contains(col))
        {
            grab.colliders.Clear();
            grab.colliders.Add(col);
        }

        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
    }
}
