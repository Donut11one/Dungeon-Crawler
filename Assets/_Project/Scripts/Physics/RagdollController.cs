using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Manages ragdoll state transitions for Rigidbody-based characters.
/// When triggered, removes movement constraints and lets physics take over.
/// After a recovery period (cooldown), automatically gets the character
/// back up to the default upright position safely without floor clipping.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class RagdollController : NetworkBehaviour
{
    [Header("Ragdoll Settings")]
    [SerializeField] private float recoveryTime = 2.5f;
    [SerializeField] private float ragdollDrag = 1.5f;

    public NetworkVariable<bool> IsRagdolled = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<float> RemainingRecoveryTime = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Rigidbody rb;
    private RigidbodyConstraints normalConstraints;
    private float normalDrag;
    private float ragdollTimer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            normalConstraints = rb.constraints;
            normalDrag = rb.linearDamping;
        }
    }

    public override void OnNetworkSpawn()
    {
        IsRagdolled.OnValueChanged += OnRagdollStateChanged;
    }

    public override void OnNetworkDespawn()
    {
        IsRagdolled.OnValueChanged -= OnRagdollStateChanged;
    }

    private void Update()
    {
        if (!IsServer)
        {
            return;
        }

        if (IsRagdolled.Value)
        {
            ragdollTimer -= Time.deltaTime;
            RemainingRecoveryTime.Value = Mathf.Max(0f, ragdollTimer);

            if (ragdollTimer <= 0f)
            {
                // Don't auto-recover if dead
                PlayerHealth health = GetComponent<PlayerHealth>();
                if (health != null && health.IsDead.Value)
                {
                    return;
                }

                EnemyHealth enemyHealth = GetComponent<EnemyHealth>();
                if (enemyHealth != null && enemyHealth.IsDead.Value)
                {
                    return;
                }

                IsRagdolled.Value = false;
                RemainingRecoveryTime.Value = 0f;
            }
        }
    }

    /// <summary>
    /// Server-only: Trigger ragdoll with a knockback impulse and recovery cooldown.
    /// </summary>
    /// <param name="force">Impulse force to apply.</param>
    /// <param name="duration">How long before recovery. Uses default recoveryTime if &lt;= 0.</param>
    public void TriggerRagdoll(Vector3 force, float duration = -1f)
    {
        if (!IsServer)
        {
            return;
        }

        ragdollTimer = duration > 0f ? duration : recoveryTime;
        RemainingRecoveryTime.Value = ragdollTimer;
        IsRagdolled.Value = true;

        ApplyRagdollForceClientRpc(force);
    }

    /// <summary>
    /// Force exit ragdoll immediately (e.g. on respawn).
    /// </summary>
    public void RecoverImmediately()
    {
        if (!IsServer)
        {
            return;
        }

        ragdollTimer = 0f;
        RemainingRecoveryTime.Value = 0f;
        IsRagdolled.Value = false;
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void ApplyRagdollForceClientRpc(Vector3 force)
    {
        if (rb != null)
        {
            rb.AddForce(force, ForceMode.Impulse);
        }
    }

    private void OnRagdollStateChanged(bool previous, bool current)
    {
        if (current)
        {
            EnterRagdoll();
        }
        else
        {
            ExitRagdoll();
        }
    }

    private void EnterRagdoll()
    {
        if (rb != null)
        {
            rb.constraints = RigidbodyConstraints.None;
            rb.linearDamping = ragdollDrag;
        }

        // Disable NavMeshAgent if present (enemies)
        NavMeshAgent agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.enabled = false;
        }
    }

    private void ExitRagdoll()
    {
        // 1. Calculate upright rotation (keep yaw facing direction, clear pitch & roll)
        float currentYaw = transform.eulerAngles.y;
        Quaternion uprightRotation = Quaternion.Euler(0f, currentYaw, 0f);
        transform.rotation = uprightRotation;

        // 2. Prevent ground/floor clipping:
        // Raycast straight down to place feet cleanly on top of the ground
        CapsuleCollider capsule = GetComponent<CapsuleCollider>();
        float halfHeight = capsule != null ? capsule.height * 0.5f : 1f;

        Vector3 rayStart = transform.position + Vector3.up * 1.5f;
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore))
        {
            transform.position = new Vector3(transform.position.x, hit.point.y + halfHeight + 0.05f, transform.position.z);
        }
        else
        {
            // Nudge up if raycast didn't hit ground
            transform.position += Vector3.up * 0.5f;
        }

        // 3. Reset physics velocity and restore constraints
        if (rb != null)
        {
            rb.position = transform.position;
            rb.rotation = uprightRotation;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.linearDamping = normalDrag;
            rb.constraints = normalConstraints;
        }

        // 4. Reset player camera and mouse look
        PlayerController playerController = GetComponent<PlayerController>();
        if (playerController != null)
        {
            playerController.ResetAfterRagdoll(currentYaw);
        }

        // 5. Re-enable NavMeshAgent if present (enemies)
        NavMeshAgent agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.Warp(transform.position);
            agent.enabled = true;
        }
    }
}
