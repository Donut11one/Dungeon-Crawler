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
    [SerializeField] private float minimumImpactSpeed = 5f;
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

        float speed = rb.linearVelocity.magnitude;
        if (speed < minimumImpactSpeed)
        {
            return;
        }

        IDamageable damageable = collision.gameObject.GetComponent<IDamageable>();
        if (damageable != null)
        {
            int damage = baseDamage + Mathf.RoundToInt(speed * damagePerSpeed);
            Vector3 hitDir = rb.linearVelocity.normalized;
            damageable.TakeDamage(damage, hitDir);
        }
    }
}
