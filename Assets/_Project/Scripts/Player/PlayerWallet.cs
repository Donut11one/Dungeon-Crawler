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
        75,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Header("Per-Player Quota System")]
    [SerializeField] private int baseQuota = 100;
    [SerializeField] private int quotaIncrement = 50;

    public NetworkVariable<int> QuotaTarget = new NetworkVariable<int>(
        100,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> QuotaProgress = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> QuotaPoints = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> QuotaLevel = new NetworkVariable<int>(
        1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            if (BankedMoney.Value == 0)
            {
                // Initial contractor signing bonus for testing and first purchases
                BankedMoney.Value = 75;
            }

            if (QuotaTarget.Value <= 0)
            {
                QuotaTarget.Value = baseQuota;
            }
        }
    }

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

        // Fulfill per-player quota with the extracted earnings
        AddQuotaContribution(totalExtracted);

        Debug.Log($"[PlayerWallet] Player {OwnerClientId} extracted! Banked: +${bankedThisDive} | Total Bank: ${BankedMoney.Value} | Quota: {QuotaProgress.Value}/{QuotaTarget.Value} (Tier {QuotaLevel.Value}, {QuotaPoints.Value} pts)");
        return bankedThisDive;
    }

    /// <summary>
    /// Calculates the quota target for a specific quota tier/level.
    /// Increases after every quota completed.
    /// </summary>
    public int CalculateQuotaTarget(int level)
    {
        int target = baseQuota + Mathf.Max(0, level - 1) * quotaIncrement;
        return Mathf.Max(10, target);
    }

    /// <summary>
    /// Server: Adds money towards this player's individual quota.
    /// If reached or exceeded: awards +1 point per quota met, advances QuotaLevel,
    /// increases the next QuotaTarget, and carries over any excess progress.
    /// </summary>
    public void AddQuotaContribution(int amount)
    {
        if (!IsServer || amount <= 0)
        {
            return;
        }

        QuotaProgress.Value += amount;
        int pointsEarned = 0;

        while (QuotaTarget.Value > 0 && QuotaProgress.Value >= QuotaTarget.Value)
        {
            QuotaProgress.Value -= QuotaTarget.Value;
            QuotaPoints.Value++;
            QuotaLevel.Value++;
            pointsEarned++;

            QuotaTarget.Value = CalculateQuotaTarget(QuotaLevel.Value);
            Debug.Log($"[PlayerWallet] Player {OwnerClientId} MET QUOTA! Earned +1 Point (Total: {QuotaPoints.Value}). Next Quota Target: ${QuotaTarget.Value} (Tier {QuotaLevel.Value})");
        }

        if (pointsEarned > 0)
        {
            QuotaCompletedClientRpc(pointsEarned, QuotaPoints.Value, QuotaTarget.Value);
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void QuotaCompletedClientRpc(int pointsAdded, int totalPoints, int nextTarget)
    {
        GameHUD.TriggerQuotaCelebration(OwnerClientId, pointsAdded, totalPoints, nextTarget);
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
    /// Server: Drops all gold collected during this run onto the ground.
    /// Resets RunMoney back to 0 while preserving persistent BankedMoney.
    /// Returns the total amount of money dropped.
    /// </summary>
    public int DropAllRunMoney(Vector3 origin)
    {
        if (!IsServer)
        {
            return 0;
        }

        int amountToDrop = RunMoney.Value;
        if (amountToDrop <= 0)
        {
            return 0;
        }

        RunMoney.Value = 0;
        Debug.Log($"[PlayerWallet] Player {OwnerClientId} died and lost ${amountToDrop} run money.");
        return amountToDrop;
    }

    /// <summary>
    /// Server: Attempts to spend from persistent banked savings.
    /// Returns true if successful and funds were deducted.
    /// </summary>
    public bool TrySpendBankedMoney(int amount)
    {
        if (!IsServer || amount <= 0)
        {
            return false;
        }

        if (BankedMoney.Value >= amount)
        {
            BankedMoney.Value -= amount;
            Debug.Log($"[PlayerWallet] Player {OwnerClientId} spent ${amount}. Remaining Bank: ${BankedMoney.Value}");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Server: Full reset (both run, bank, and quota to initial state), if ever needed.
    /// </summary>
    public void ResetAllMoney()
    {
        if (!IsServer)
        {
            return;
        }

        RunMoney.Value = 0;
        BankedMoney.Value = 0;
        QuotaProgress.Value = 0;
        QuotaPoints.Value = 0;
        QuotaLevel.Value = 1;
        QuotaTarget.Value = baseQuota;
    }
}
