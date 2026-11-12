using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Attached to the elevator pad in the LobbyArea.
/// Stepping onto the elevator pad toggles the player's readiness to dive into the dungeon.
/// When all connected contractors step on the pad (or press F to agree),
/// the dive sequence countdown initiates!
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
            float speed = pulseSpeed;
            if (LobbyArea.Instance != null && LobbyArea.Instance.DiveCountdown.Value > 0f)
            {
                speed = pulseSpeed * 2.5f; // Pulse faster during launch countdown
            }

            float pulse = 1f + Mathf.Sin(Time.time * speed) * 0.08f;
            transform.localScale = new Vector3(baseScale.x * pulse, baseScale.y, baseScale.z * pulse);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (GameManager.Instance == null || GameManager.Instance.CurrentState.Value != GameManager.GameState.Lobby)
        {
            return;
        }

        PlayerController pc = other.GetComponentInParent<PlayerController>();
        if (pc == null)
        {
            return;
        }

        // Client owner sets ready via RPC
        if (pc.IsOwner)
        {
            if (!pc.IsReadyToDive.Value)
            {
                pc.SetReadyServerRpc(true);
            }
        }
        else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            pc.IsReadyToDive.Value = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (GameManager.Instance == null || GameManager.Instance.CurrentState.Value != GameManager.GameState.Lobby)
        {
            return;
        }

        PlayerController pc = other.GetComponentInParent<PlayerController>();
        if (pc == null)
        {
            return;
        }

        // Stepping off the elevator cancels ready
        if (pc.IsOwner)
        {
            if (pc.IsReadyToDive.Value)
            {
                pc.SetReadyServerRpc(false);
            }
        }
        else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            pc.IsReadyToDive.Value = false;
        }
    }
}
