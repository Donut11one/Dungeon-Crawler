using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Tracks a player's money. Server-authoritative via NetworkVariables.
/// Separates in-dungeon run earnings (RunMoney, starts at $0 per run)
/// from extracted savings (BankedMoney, stays with player across runs).
/// </summary>
public class PlayerWallet : NetworkBehaviour
{
    [Header("Run Earnings (Starts at $0 each dive)")]
    public NetworkVariable<int> RunMoney = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Header("Banked Savings (Extracted money that stays with you)")]
    public NetworkVariable<int> BankedMoney = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Backwards compatibility accessor for run money.
    /// </summary>
    public NetworkVariable<int> Money => RunMoney;

    /// <summary>
    /// Server: Add coins collected during the current dungeon run.
    /// </summary>
    public void AddMoney(int amount)
    {
        if (!IsServer)
        {
            return;
        }

        RunMoney.Value += amount;
        Debug.Log($"[PlayerWallet] Player {OwnerClientId} picked up ${amount}. Current Run: ${RunMoney.Value} | Bank: ${BankedMoney.Value}");
    }

    /// <summary>
    /// Server: Bank the current run earnings plus extraction bonus into persistent savings.
    /// Resets RunMoney back to 0. Returns the total banked amount.
    /// </summary>
    public int BankRunMoney(int bonus = 0)
    {
        if (!IsServer)
        {
            return 0;
        }

        int totalExtracted = RunMoney.Value + bonus;
        BankedMoney.Value += totalExtracted;
        int bankedThisDive = totalExtracted;
        RunMoney.Value = 0;

        Debug.Log($"[PlayerWallet] Player {OwnerClientId} extracted! Banked: +${bankedThisDive} | Total Bank: ${BankedMoney.Value}");
        return bankedThisDive;
    }

    /// <summary>
    /// Server: Start a new dungeon run. Resets run money to $0 while keeping all banked savings.
    /// </summary>
    public void StartNewRun()
    {
        if (!IsServer)
        {
            return;
        }

        RunMoney.Value = 0;
    }

    /// <summary>
    /// Server: Resets current run money back to zero (does NOT touch banked savings).
    /// </summary>
    public void ResetMoney()
    {
        if (!IsServer)
        {
            return;
        }

        RunMoney.Value = 0;
    }

    /// <summary>
    /// Server: Full reset (both run and bank to 0), if ever needed.
    /// </summary>
    public void ResetAllMoney()
    {
        if (!IsServer)
        {
            return;
        }

        RunMoney.Value = 0;
        BankedMoney.Value = 0;
    }
}
