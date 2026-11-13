using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Attached to throwable physics objects (barrels, junk).
/// When this object collides at high velocity, it deals damage
/// to anything with IDamageable. Server-authoritative.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class ImpactDamage : NetworkBehaviour
{
    [Header("Impact Settings")]
    [SerializeField] private float minimumImpactSpeed = 2.5f;
    [SerializeField] private int baseDamage = 25;
    [SerializeField] private float damagePerSpeed = 5f;

    private Rigidbody rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Only process damage on the server
        if (!IsServer)
        {
            return;
        }

        float speed = rb != null ? rb.linearVelocity.magnitude : 0f;
        if (speed < minimumImpactSpeed)
        {
            return;
        }

        // Search collider and all parent objects for IDamageable
        IDamageable damageable = collision.collider != null
            ? collision.collider.GetComponentInParent<IDamageable>()
            : collision.gameObject.GetComponentInParent<IDamageable>();

        if (damageable != null)
        {
            int damage = baseDamage + Mathf.RoundToInt(speed * damagePerSpeed);
            Vector3 hitDir = rb != null ? rb.linearVelocity.normalized : transform.forward;
            damageable.TakeDamage(damage, hitDir);
        }
    }
}
