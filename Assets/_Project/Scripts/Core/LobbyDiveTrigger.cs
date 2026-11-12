using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Attached to the elevator pad in the LobbyArea.
/// When a player enters this trigger while in GameState.Lobby,
/// it launches the expedition and teleports all players into the dungeon.
/// </summary>
public class LobbyDiveTrigger : MonoBehaviour
{
    private float pulseSpeed = 2.5f;
    private Vector3 baseScale;

    private void Awake()
    {
        baseScale = transform.localScale;
    }

    private void Update()
    {
        if (GameManager.Instance == null)
        {
            return;
        }

        // Only pulse when waiting in lobby
        if (GameManager.Instance.CurrentState.Value == GameManager.GameState.Lobby)
        {
            float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * 0.08f;
            transform.localScale = new Vector3(baseScale.x * pulse, baseScale.y, baseScale.z * pulse);
        }
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

        if (GameManager.Instance.CurrentState.Value != GameManager.GameState.Lobby)
        {
            return;
        }

        // Verify it was a player
        PlayerHealth player = other.GetComponentInParent<PlayerHealth>();
        if (player == null)
        {
            return;
        }

        Debug.Log($"[LobbyDiveTrigger] Player {player.OwnerClientId} entered elevator pad. Launching dive!");
        GameManager.Instance.StartRound();
    }
}
