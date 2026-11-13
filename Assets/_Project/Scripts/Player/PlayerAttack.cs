using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles player melee attacks on Left-Click.
/// If an item/weapon is selected in the hotbar, uses the item's stats and range.
/// If no item is selected (empty slot), attacks with Fists!
/// Hits enemies (EnemyHealth), players (PlayerHealth), or launches physics props (Barrels).
/// Server-authoritative hit validation and damage application.
/// </summary>
public class PlayerAttack : NetworkBehaviour
{
    [Header("Fist Default Stats (Unarmed)")]
    [SerializeField] private int fistDamage = 15;
    [SerializeField] private float fistCooldown = 0.38f;
    [SerializeField] private float fistRange = 2.2f;
    [SerializeField] private float fistKnockback = 6f;

    [Header("Hit Detection")]
    [SerializeField] private float hitSphereRadius = 0.35f;
    [SerializeField] private LayerMask attackMask = ~0;

    private PlayerInventory inventory;
    private RagdollController ragdollController;
    private GrabThrow grabThrow;
    private Camera playerCamera;
    private float attackTimer;

    private void Awake()
    {
        inventory = GetComponent<PlayerInventory>();
        ragdollController = GetComponent<RagdollController>();
        grabThrow = GetComponent<GrabThrow>();
        playerCamera = GetComponentInChildren<Camera>();
    }

    private void Update()
    {
        if (!IsOwner)
        {
            return;
        }

        if (attackTimer > 0f)
        {
            attackTimer -= Time.deltaTime;
        }

        // Cannot attack while knocked down in ragdoll
        if (ragdollController != null && ragdollController.IsRagdolled.Value)
        {
            return;
        }

        // Cannot attack while carrying a heavy grabbed object (like a barrel)
        if (grabThrow != null && grabThrow.IsHoldingObject)
        {
            return;
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (attackTimer <= 0f)
            {
                PerformAttack();
            }
        }
    }

    private void PerformAttack()
    {
        ItemData equipped = inventory != null ? inventory.GetSelectedItem() : null;

        int damage = equipped != null ? equipped.Damage : fistDamage;
        float cooldown = equipped != null ? equipped.AttackCooldown : fistCooldown;
        float range = equipped != null ? equipped.AttackRange : fistRange;
        float knockback = equipped != null ? equipped.KnockbackForce : fistKnockback;
        string attackName = equipped != null ? equipped.DisplayName : "Fist";

        attackTimer = cooldown;

        // Visual / Audio feedback
        PlayAttackFeedback(attackName, hit: false);

        // Third-person / First-person hit raycast through crosshair
        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>();
            if (playerCamera == null)
            {
                return;
            }
        }

        Ray aimRay = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        float castDist = range + 4.5f; // Generous reach from 3rd-person camera to find target under crosshair

        RaycastHit[] hits = Physics.SphereCastAll(aimRay, hitSphereRadius, castDist, attackMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        Vector3 playerPos = transform.position + Vector3.up * 1.2f;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null || hit.transform.IsChildOf(transform))
            {
                continue; // Skip self
            }

            // Verify the hit point is within reach of the player character (not just the camera)
            float distToPlayer = Vector3.Distance(playerPos, hit.point);
            if (distToPlayer > range + 0.9f)
            {
                continue;
            }

            // Check for IDamageable (EnemyHealth, PlayerHealth)
            IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
            NetworkObject netObj = hit.collider.GetComponentInParent<NetworkObject>();

            Vector3 hitDirection = (hit.point - playerPos).normalized;
            if (hitDirection.sqrMagnitude < 0.01f)
            {
                hitDirection = transform.forward;
            }
            hitDirection.y = Mathf.Max(0.25f, hitDirection.y); // Subtle upward pop

            if (netObj != null)
            {
                PlayAttackFeedback(attackName, hit: true);
                AttackServerRpc(netObj.NetworkObjectId, damage, hitDirection * knockback);
                return;
            }

            // Punching physical non-networked props or barrels directly
            Rigidbody targetRb = hit.collider.GetComponentInParent<Rigidbody>();
            if (targetRb != null)
            {
                PlayAttackFeedback(attackName, hit: true);
                targetRb.AddForce(hitDirection * knockback * 18f, ForceMode.Impulse);
                return;
            }

            // Hit environment wall
            if (!hit.collider.isTrigger)
            {
                break;
            }
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void AttackServerRpc(ulong targetNetId, int damage, Vector3 knockbackImpulse)
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetId, out NetworkObject targetNetObj))
        {
            // Range validation against server distance
            float dist = Vector3.Distance(transform.position, targetNetObj.transform.position);
            if (dist > 7f)
            {
                return;
            }

            IDamageable damageable = targetNetObj.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(damage, knockbackImpulse);
            }

            Rigidbody rb = targetNetObj.GetComponentInParent<Rigidbody>();
            if (rb != null)
            {
                rb.AddForce(knockbackImpulse * 14f, ForceMode.Impulse);
            }
        }
    }

    private void PlayAttackFeedback(string attackName, bool hit)
    {
        // Debug confirmation & punch visual cue
        if (hit)
        {
            Debug.Log($"[PlayerAttack] {attackName} HIT!");
        }
    }
}
