using UnityEngine;

/// <summary>
/// Interface for any entity that can take damage from impacts, attacks, or hazards.
/// Implemented by PlayerHealth and EnemyHealth.
/// </summary>
public interface IDamageable
{
    /// <summary>
    /// Apply damage to this entity.
    /// </summary>
    /// <param name="damage">Amount of damage to deal.</param>
    /// <param name="hitDirection">World-space direction the hit came from (used for knockback).</param>
    void TakeDamage(int damage, Vector3 hitDirection);
}
