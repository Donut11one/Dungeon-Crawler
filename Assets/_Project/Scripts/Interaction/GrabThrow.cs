using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Player grab and throw mechanic.
/// Left-Click / E: Grab / Pick up object (or gentle drop when held)
/// Right-Click: THROW object forward with force
/// </summary>
public class GrabThrow : NetworkBehaviour
{
    [Header("Grab Settings")]
    [SerializeField] private float grabRange = 5f;
    [SerializeField] private float throwForce = 26f;
    [SerializeField] private Transform holdPoint;

    private GrabbableObject heldObject;
    private Camera playerCamera;
    private RagdollController ragdollController;
    private Collider playerCollider;

    /// <summary>The position where held items float in front of the player.</summary>
    public Transform HoldPoint
    {
        get
        {
            return holdPoint;
        }
    }

    /// <summary>Whether the player is currently carrying a grabbed heavy object (e.g. barrel).</summary>
    public bool IsHoldingObject
    {
        get
        {
            return heldObject != null;
        }
    }

    private void Awake()
    {
        playerCamera = GetComponentInChildren<Camera>();
        ragdollController = GetComponent<RagdollController>();
        playerCollider = GetComponent<Collider>();
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        // Can't interact while ragdolled
        if (ragdollController != null && ragdollController.IsRagdolled.Value)
        {
            if (heldObject != null)
            {
                ForceDrop();
            }
            return;
        }

        if (Mouse.current == null)
        {
            return;
        }

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        bool rightClicked = Mouse.current.rightButton.wasPressedThisFrame;
        bool ePressed = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;

        // RIGHT-CLICK: Throw held object
        if (rightClicked && heldObject != null)
        {
            Throw();
            return;
        }

        // E: Grab if empty, or Drop if already holding
        if (ePressed)
        {
            if (heldObject == null)
            {
                TryGrab();
            }
            else
            {
                Drop();
            }
        }
    }

    private void LateUpdate()
    {
        if (!IsOwner)
        {
            return;
        }

        // Keep held object pinned to hold point in LateUpdate (after camera look updates)
        if (heldObject != null && holdPoint != null)
        {
            heldObject.transform.position = holdPoint.position;
            heldObject.transform.rotation = holdPoint.rotation;
        }
    }

    private void TryGrab()
    {
        if (playerCamera == null)
        {
            return;
        }

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        float maxRayDist = grabRange + 6f;

        RaycastHit[] hits = Physics.RaycastAll(ray, maxRayDist, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.transform.IsChildOf(transform))
            {
                continue;
            }

            GrabbableObject grabbable = hit.collider.GetComponentInParent<GrabbableObject>();
            if (grabbable != null && !grabbable.IsGrabbed.Value)
            {
                float distToPlayer = Vector3.Distance(transform.position + Vector3.up * 1.2f, grabbable.transform.position);
                if (distToPlayer <= grabRange + 0.8f)
                {
                    heldObject = grabbable;

                    // Disable collision with the player while held so it doesn't push the player
                    if (playerCollider != null && grabbable.Col != null)
                    {
                        Physics.IgnoreCollision(playerCollider, grabbable.Col, true);
                    }

                    grabbable.GrabServerRpc(OwnerClientId);
                    return;
                }
            }
            else
            {
                // If solid geometry without damageable or grabbable is hit, line of sight is obstructed
                if (hit.collider.GetComponentInParent<IDamageable>() == null &&
                    hit.collider.GetComponentInParent<GrabbableObject>() == null)
                {
                    break;
                }
            }
        }
    }

    private void Throw()
    {
        if (heldObject == null || playerCamera == null)
        {
            return;
        }

        GrabbableObject toThrow = heldObject;
        heldObject = null;

        Vector3 throwOrigin = transform.position + Vector3.up * 1.3f + transform.forward * 0.85f;

        Ray aimRay = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        Vector3 aimTarget;
        if (Physics.Raycast(aimRay, out RaycastHit aimHit, 60f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!aimHit.transform.IsChildOf(transform))
            {
                aimTarget = aimHit.point;
            }
            else
            {
                aimTarget = aimRay.GetPoint(35f);
            }
        }
        else
        {
            aimTarget = aimRay.GetPoint(35f);
        }

        Vector3 throwDir = (aimTarget - throwOrigin).normalized;
        Vector3 throwVelocity = throwDir * throwForce + Vector3.up * 1.2f;

        toThrow.ReleaseServerRpc(throwOrigin, throwVelocity);

        // Re-enable collision with player after a short delay so the barrel can leave the player's space
        if (playerCollider != null && toThrow.Col != null)
        {
            StartCoroutine(ReEnableCollisionAfterDelay(playerCollider, toThrow.Col, 0.4f));
        }
    }

    private void Drop()
    {
        if (heldObject == null)
        {
            return;
        }

        GrabbableObject toDrop = heldObject;
        heldObject = null;

        Vector3 dropOrigin = transform.position + Vector3.up * 1.2f + transform.forward * 0.9f;
        Vector3 dropVelocity = transform.forward * 2.5f;

        toDrop.ReleaseServerRpc(dropOrigin, dropVelocity);

        if (playerCollider != null && toDrop.Col != null)
        {
            StartCoroutine(ReEnableCollisionAfterDelay(playerCollider, toDrop.Col, 0.3f));
        }
    }

    /// <summary>
    /// Forcibly drop any held object (used on ragdoll, death, or round reset).
    /// </summary>
    public void ForceDrop()
    {
        if (heldObject == null)
        {
            return;
        }

        GrabbableObject toDrop = heldObject;
        heldObject = null;

        toDrop.ReleaseServerRpc(toDrop.transform.position, Vector3.zero);

        if (playerCollider != null && toDrop.Col != null)
        {
            Physics.IgnoreCollision(playerCollider, toDrop.Col, false);
        }
    }

    private IEnumerator ReEnableCollisionAfterDelay(Collider colA, Collider colB, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (colA != null && colB != null)
        {
            Physics.IgnoreCollision(colA, colB, false);
        }
    }
}
