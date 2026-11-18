using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

/// <summary>
/// Manages Unity Relay cloud allocations, authentication, and join codes.
/// Allows players to host and join multiplayer sessions across different Wi-Fi networks (Internet)
/// without port forwarding or VPN software.
/// </summary>
public static class RelayManager
{
    public static string CurrentJoinCode { get; private set; } = string.Empty;
    public static bool IsRelayActive { get; private set; } = false;
    public static bool IsConnecting { get; private set; } = false;
    public static string StatusMessage { get; private set; } = string.Empty;
    public static bool HasError { get; private set; } = false;
    public static bool UseDtlsEncryption { get; set; } = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        IsRelayActive = false;
        IsConnecting = false;
        StatusMessage = string.Empty;
        HasError = false;
        CurrentJoinCode = string.Empty;
    }

    /// <summary>
    /// Selects the appropriate Relay endpoint from the allocation.
    /// Prefers standard UDP ("udp") for maximum consumer router/firewall compatibility,
    /// avoiding DTLS packet drops on residential Wi-Fi or mobile hotspots.
    /// </summary>
    private static RelayServerEndpoint SelectEndpoint(System.Collections.Generic.List<RelayServerEndpoint> endpoints, bool useDtls)
    {
        if (endpoints == null || endpoints.Count == 0)
        {
            return null;
        }

        string targetType = useDtls ? RelayServerEndpoint.ConnectionTypeDtls : RelayServerEndpoint.ConnectionTypeUdp;
        RelayServerEndpoint found = endpoints.Find(e => e != null && string.Equals(e.ConnectionType, targetType, StringComparison.OrdinalIgnoreCase));
        if (found != null)
        {
            return found;
        }

        if (useDtls)
        {
            found = endpoints.Find(e => e != null && string.Equals(e.ConnectionType, RelayServerEndpoint.ConnectionTypeUdp, StringComparison.OrdinalIgnoreCase));
            if (found != null)
            {
                return found;
            }
        }

        return endpoints[0];
    }

    /// <summary>
    /// Initializes Unity Gaming Services and signs the user in anonymously.
    /// </summary>
    public static async Task<bool> InitializeServicesAsync()
    {
        try
        {
            StatusMessage = "Connecting to Unity Gaming Services...";
            HasError = false;

            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            if (AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn)
            {
                StatusMessage = "Ready";
                return true;
            }

            if (AuthenticationService.Instance != null && !AuthenticationService.Instance.IsSignedIn)
            {
                StatusMessage = "Authenticating with Unity Services...";
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            StatusMessage = "Ready";
            return true;
        }
        catch (ServicesInitializationException sie)
        {
            StatusMessage = "Unity Services not linked. Switch to LOCAL (LAN) mode.";
            HasError = true;
            Debug.LogWarning($"[RelayManager] Unity Gaming Services not initialized: {sie.Message}. Use LOCAL (LAN / VPN) mode to play!");
            return false;
        }
        catch (AuthenticationException ae)
        {
            StatusMessage = "Auth failed. Switch to LOCAL (LAN) mode.";
            HasError = true;
            Debug.LogWarning($"[RelayManager] Unity Authentication failed: {ae.Message}. Use LOCAL (LAN / VPN) mode!");
            return false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Auth Error: {ex.Message}";
            HasError = true;
            Debug.LogWarning($"[RelayManager] Services initialization failed: {ex.Message}. Use LOCAL (LAN / VPN) mode to play without UGS!");
            return false;
        }
    }

    /// <summary>
    /// Hosts a game using Unity Relay and returns the 6-character Join Code.
    /// </summary>
    public static async Task<string> StartHostWithRelayAsync(int maxConnections = 4)
    {
        IsConnecting = true;
        HasError = false;
        CurrentJoinCode = string.Empty;

        try
        {
            bool ready = await InitializeServicesAsync();
            if (!ready)
            {
                IsConnecting = false;
                return null;
            }

            StatusMessage = "Allocating Relay Cloud Server...";

            // 1. Request Relay server allocation
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);

            // 2. Retrieve the shareable Join Code from allocation
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            // 3. Configure UnityTransport with Relay Host Data
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                StatusMessage = "Error: UnityTransport component missing!";
                HasError = true;
                IsConnecting = false;
                return null;
            }

            RelayServerEndpoint endpoint = SelectEndpoint(allocation.ServerEndpoints, UseDtlsEncryption);
            string host = endpoint != null ? endpoint.Host : allocation.RelayServer.IpV4;
            ushort port = (ushort)(endpoint != null ? endpoint.Port : allocation.RelayServer.Port);
            bool isSecure = endpoint != null ? endpoint.Secure : false;

            Debug.Log($"[RelayManager] Hosting on Relay: Host={host}, Port={port}, Protocol={(endpoint?.ConnectionType ?? "udp")}, Secure={isSecure}");

            transport.SetHostRelayData(
                host,
                port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData,
                isSecure
            );

            // 4. Start Host session via NGO
            StatusMessage = "Starting Host Session...";
            bool success = NetworkManager.Singleton.StartHost();

            if (success)
            {
                CurrentJoinCode = joinCode;
                IsRelayActive = true;
                StatusMessage = $"Relay Host Active! Code: {joinCode}";
                IsConnecting = false;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                Debug.Log($"[RelayManager] Relay Host started! Join Code: {joinCode}");
                return joinCode;
            }
            else
            {
                StatusMessage = "Failed to start Host session.";
                HasError = true;
                IsConnecting = false;
                return null;
            }
        }
        catch (RelayServiceException ex)
        {
            StatusMessage = $"Relay Error: {ex.Message}";
            HasError = true;
            IsConnecting = false;
            Debug.LogError($"[RelayManager] Relay allocation failed: {ex}");
            return null;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Host Error: {ex.Message}";
            HasError = true;
            IsConnecting = false;
            Debug.LogError($"[RelayManager] Unexpected error: {ex}");
            return null;
        }
    }

    /// <summary>
    /// Joins an existing game using a 6-character Unity Relay Join Code.
    /// </summary>
    public static async Task<bool> StartClientWithRelayAsync(string joinCode)
    {
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            StatusMessage = "Please enter a valid Join Code!";
            HasError = true;
            return false;
        }

        joinCode = joinCode.Trim().ToUpperInvariant();
        IsConnecting = true;
        HasError = false;

        try
        {
            bool ready = await InitializeServicesAsync();
            if (!ready)
            {
                IsConnecting = false;
                return false;
            }

            StatusMessage = $"Joining Relay with Code [{joinCode}]...";

            // 1. Join Relay allocation via the Join Code
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            // 2. Configure UnityTransport with Relay Client Data
            UnityTransport transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            if (transport == null)
            {
                StatusMessage = "Error: UnityTransport component missing!";
                HasError = true;
                IsConnecting = false;
                return false;
            }

            RelayServerEndpoint endpoint = SelectEndpoint(joinAllocation.ServerEndpoints, UseDtlsEncryption);
            string host = endpoint != null ? endpoint.Host : joinAllocation.RelayServer.IpV4;
            ushort port = (ushort)(endpoint != null ? endpoint.Port : joinAllocation.RelayServer.Port);
            bool isSecure = endpoint != null ? endpoint.Secure : false;

            Debug.Log($"[RelayManager] Joining Relay: Host={host}, Port={port}, Protocol={(endpoint?.ConnectionType ?? "udp")}, Secure={isSecure}");

            transport.SetClientRelayData(
                host,
                port,
                joinAllocation.AllocationIdBytes,
                joinAllocation.Key,
                joinAllocation.ConnectionData,
                joinAllocation.HostConnectionData,
                isSecure
            );

            // 3. Start Client session via NGO
            StatusMessage = "Connecting to Host...";
            bool success = NetworkManager.Singleton.StartClient();

            if (success)
            {
                CurrentJoinCode = joinCode;
                IsRelayActive = true;
                StatusMessage = "Connected to Relay Session!";
                IsConnecting = false;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                Debug.Log($"[RelayManager] Connected to Relay session with Code: {joinCode}");
                return true;
            }
            else
            {
                StatusMessage = "Failed to start Client session.";
                HasError = true;
                IsConnecting = false;
                return false;
            }
        }
        catch (RelayServiceException ex)
        {
            if (ex.Reason == RelayExceptionReason.JoinCodeNotFound ||
                ex.Reason == RelayExceptionReason.AllocationNotFound ||
                ex.Reason == RelayExceptionReason.EntityNotFound)
            {
                StatusMessage = "Invalid Join Code! Room not found.";
            }
            else
            {
                StatusMessage = $"Relay Error: {ex.Message}";
            }
            HasError = true;
            IsConnecting = false;
            Debug.LogError($"[RelayManager] Failed to join Relay: {ex}");
            return false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Join Error: {ex.Message}";
            HasError = true;
            IsConnecting = false;
            Debug.LogError($"[RelayManager] Unexpected error: {ex}");
            return false;
        }
    }

    /// <summary>
    /// Resets Relay state upon disconnection or returning to main menu.
    /// </summary>
    public static void ResetState()
    {
        CurrentJoinCode = string.Empty;
        IsRelayActive = false;
        IsConnecting = false;
        StatusMessage = string.Empty;
        HasError = false;
    }
}
