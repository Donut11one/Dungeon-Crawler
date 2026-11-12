using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked extraction zone. When a player enters this trigger during a round,
/// they escape — earning a bonus and locking them out of the dungeon.
/// Design pillars: Emergent (timed pressure + money interaction),
/// Clippable (players can be knocked into or away from the zone),
/// Ragdoll Chaos (ragdolled players can't extract).
/// </summary>
public class ExtractionPoint : MonoBehaviour
{
    [Header("Extraction")]
    [SerializeField] private float pulseSpeed = 2f;
    [SerializeField] private float pulseScale = 0.15f;

    private Vector3 baseScale;
    private Renderer zoneRenderer;

    private void Awake()
    {
        baseScale = transform.localScale;
        zoneRenderer = GetComponent<Renderer>();
    }

    private void Update()
    {
        // Pulsing visual (all clients, cosmetic only)
        if (GameManager.Instance == null)
        {
            return;
        }

        if (GameManager.Instance.CurrentState.Value != GameManager.GameState.Playing)
        {
            return;
        }

        float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseScale;
        transform.localScale = baseScale * pulse;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        if (GameManager.Instance == null)
        {
            return;
        }

        if (GameManager.Instance.CurrentState.Value != GameManager.GameState.Playing)
        {
            return;
        }

        // Must be a player
        PlayerHealth playerHealth = other.GetComponentInParent<PlayerHealth>();
        if (playerHealth == null)
        {
            return;
        }

        // Can't extract if dead
        if (playerHealth.IsDead.Value)
        {
            return;
        }

        // Can't extract if ragdolled (they slid in by accident)
        RagdollController ragdoll = other.GetComponentInParent<RagdollController>();
        if (ragdoll != null && ragdoll.IsRagdolled.Value)
        {
            return;
        }

        // Check already extracted
        NetworkObject playerNetObj = playerHealth.GetComponent<NetworkObject>();
        if (playerNetObj == null)
        {
            return;
        }

        GameManager.Instance.ExtractPlayer(playerNetObj.OwnerClientId);
    }
}
