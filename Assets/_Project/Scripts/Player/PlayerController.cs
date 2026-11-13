using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// First-person player controller using Rigidbody-based movement.
/// Rigidbody allows seamless ragdoll transitions — when ragdolled,
/// physics takes over; when recovered, player regains control.
/// Only processes input on the owning client.
/// Supports walking, sprinting, jumping with input buffering, and air control.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class PlayerController : NetworkBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float sprintMultiplier = 1.5f;
    [SerializeField] private float airAcceleration = 25f;

    [Header("Jumping")]
    [SerializeField] private float jumpForce = 5.5f;
    [SerializeField] private float jumpCooldown = 0.2f;
    [SerializeField] private LayerMask groundMask = ~0;

    [Header("Look")]
    [SerializeField] private float lookSensitivity = 0.15f;
    [SerializeField] private float maxLookAngle = 85f;

    [Header("References")]
    [SerializeField] private Transform cameraHolder;

    public NetworkVariable<float> CameraPitch = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    public NetworkVariable<bool> IsReadyToDive = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void SetReadyServerRpc(bool ready)
    {
        IsReadyToDive.Value = ready;
        Debug.Log($"[PlayerController] Player {OwnerClientId} Ready state set to {ready}");
    }

    public bool IsGrounded => CheckGrounded();

    private Rigidbody rb;
    private RagdollController ragdollController;
    private Camera playerCamera;
    private AudioListener audioListener;
    private float xRotation;

    private float jumpBufferTimer;
    private float jumpCooldownTimer;
    private readonly RaycastHit[] groundHits = new RaycastHit[8];

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        ragdollController = GetComponent<RagdollController>();
        playerCamera = GetComponentInChildren<Camera>();
        audioListener = GetComponentInChildren<AudioListener>();
    }

    public override void OnNetworkSpawn()
    {
        CameraPitch.OnValueChanged += OnCameraPitchChanged;

        if (!IsOwner)
        {
            // Apply current pitch from network on spawn
            if (cameraHolder != null)
            {
                cameraHolder.localRotation = Quaternion.Euler(CameraPitch.Value, 0f, 0f);
            }

            // Disable camera and audio on non-owner instances
            if (playerCamera != null)
            {
                playerCamera.enabled = false;
            }
            if (audioListener != null)
            {
                audioListener.enabled = false;
            }

            // Non-owners are moved purely by NetworkTransform, so disable local physics
            if (rb != null)
            {
                rb.isKinematic = true;
            }
            return;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Set up rigidbody for FPS movement on owner
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearDamping = 0f; // Zero damping so gravity and jumping are crisp and realistic
        }
    }

    public override void OnNetworkDespawn()
    {
        CameraPitch.OnValueChanged -= OnCameraPitchChanged;
    }

    private void OnCameraPitchChanged(float previous, float current)
    {
        if (!IsOwner && cameraHolder != null)
        {
            cameraHolder.localRotation = Quaternion.Euler(current, 0f, 0f);
        }
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }
        if (IsRagdolled())
        {
            return;
        }

        HandleLook();

        // Capture jump input in Update so key presses are never missed between physics ticks
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            jumpBufferTimer = 0.15f; // 150ms input buffer
        }
    }

    private void FixedUpdate()
    {
        if (!IsOwner)
        {
            return;
        }
        if (IsRagdolled())
        {
            return;
        }

        HandleJump();
        HandleMovement();
    }

    private bool IsRagdolled()
    {
        return ragdollController != null && ragdollController.IsRagdolled.Value;
    }

    private void HandleJump()
    {
        if (jumpCooldownTimer > 0f)
        {
            jumpCooldownTimer -= Time.fixedDeltaTime;
        }

        if (jumpBufferTimer > 0f)
        {
            jumpBufferTimer -= Time.fixedDeltaTime;

            if (CheckGrounded() && jumpCooldownTimer <= 0f)
            {
                // Crisp vertical jump velocity
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpForce, rb.linearVelocity.z);
                jumpCooldownTimer = jumpCooldown;
                jumpBufferTimer = 0f;
            }
        }
    }

    private void HandleMovement()
    {
        Vector2 input = Vector2.zero;

        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed)
            {
                input.y += 1f;
            }
            if (Keyboard.current.sKey.isPressed)
            {
                input.y -= 1f;
            }
            if (Keyboard.current.aKey.isPressed)
            {
                input.x -= 1f;
            }
            if (Keyboard.current.dKey.isPressed)
            {
                input.x += 1f;
            }
        }

        float speed = moveSpeed;
        if (Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed)
        {
            speed *= sprintMultiplier;
        }

        Vector3 moveDir = (transform.right * input.x + transform.forward * input.y).normalized;
        Vector3 targetVelocity = moveDir * speed;

        bool grounded = CheckGrounded();

        if (grounded)
        {
            // Crisp, instantaneous ground movement with no sliding
            rb.linearVelocity = new Vector3(targetVelocity.x, rb.linearVelocity.y, targetVelocity.z);
        }
        else
        {
            // Mid-air: preserve momentum, allow smooth air steering
            if (input.sqrMagnitude > 0.01f)
            {
                Vector3 currentHoriz = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
                Vector3 newHoriz = Vector3.MoveTowards(currentHoriz, targetVelocity, airAcceleration * Time.fixedDeltaTime);
                rb.linearVelocity = new Vector3(newHoriz.x, rb.linearVelocity.y, newHoriz.z);
            }
        }
    }

    private bool CheckGrounded()
    {
        Vector3 origin = transform.position + Vector3.up * 0.35f;
        float radius = 0.3f;
        float castDistance = 0.25f;

        int count = Physics.SphereCastNonAlloc(
            origin,
            radius,
            Vector3.down,
            groundHits,
            castDistance,
            groundMask,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < count; i++)
        {
            Collider col = groundHits[i].collider;
            if (col != null && !col.isTrigger && !col.transform.IsChildOf(transform))
            {
                return true;
            }
        }

        // Direct center raycast fallback for narrow ledges or edges
        if (Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit, 0.35f, groundMask, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != null && !hit.collider.isTrigger && !hit.collider.transform.IsChildOf(transform))
            {
                return true;
            }
        }

        return false;
    }

    private void HandleLook()
    {
        if (Mouse.current == null)
        {
            return;
        }

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        float mouseX = mouseDelta.x * lookSensitivity;
        float mouseY = mouseDelta.y * lookSensitivity;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -maxLookAngle, maxLookAngle);

        if (cameraHolder != null)
        {
            cameraHolder.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        }

        transform.Rotate(Vector3.up * mouseX);

        if (IsSpawned)
        {
            CameraPitch.Value = xRotation;
        }
    }

    /// <summary>
    /// Resets orientation, physics constraints, and camera after recovering from ragdoll.
    /// </summary>
    public void ResetAfterRagdoll(float currentYaw)
    {
        xRotation = 0f;
        jumpBufferTimer = 0f;
        jumpCooldownTimer = 0.2f;

        if (IsOwner && IsSpawned)
        {
            CameraPitch.Value = 0f;
        }

        if (cameraHolder != null)
        {
            cameraHolder.localRotation = Quaternion.identity;
        }

        transform.rotation = Quaternion.Euler(0f, currentYaw, 0f);

        if (rb != null)
        {
            rb.freezeRotation = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.linearDamping = 0f;
        }
    }
}
