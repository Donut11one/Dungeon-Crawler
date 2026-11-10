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
    public Transform HoldPoint => holdPoint;

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

        bool leftClicked = Mouse.current.leftButton.wasPressedThisFrame;
        bool rightClicked = Mouse.current.rightButton.wasPressedThisFrame;
        bool ePressed = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;

        // RIGHT-CLICK: Throw held object
        if (rightClicked && heldObject != null)
        {
            Throw();
            return;
        }

        // LEFT-CLICK or E: Grab if empty, or drop if already holding
        if (leftClicked || ePressed)
        {
            if (heldObject == null)
            {
                TryGrab();
            }
            else if (leftClicked)
            {
                // Left click while holding drops gently
                Drop();
            }
        }

        // Keep held object pinned to hold point locally for instant responsiveness
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
        if (Physics.Raycast(ray, out RaycastHit hit, grabRange))
        {
            GrabbableObject grabbable = hit.collider.GetComponentInParent<GrabbableObject>();
            if (grabbable != null && !grabbable.IsGrabbed.Value)
            {
                heldObject = grabbable;

                // Disable collision with the player while held so it doesn't push the player
                if (playerCollider != null && grabbable.Col != null)
                {
                    Physics.IgnoreCollision(playerCollider, grabbable.Col, true);
                }

                grabbable.GrabServerRpc(OwnerClientId);
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

        // Launch from slightly ahead of the camera so it doesn't clip
        Vector3 throwOrigin = playerCamera.transform.position + playerCamera.transform.forward * 0.9f;
        Vector3 throwVelocity = playerCamera.transform.forward * throwForce + Vector3.up * 1.5f;

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

        Vector3 dropOrigin = playerCamera != null
            ? playerCamera.transform.position + playerCamera.transform.forward * 0.8f
            : transform.position + transform.forward * 0.8f;

        Vector3 dropVelocity = playerCamera != null
            ? playerCamera.transform.forward * 2f
            : transform.forward * 2f;

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
