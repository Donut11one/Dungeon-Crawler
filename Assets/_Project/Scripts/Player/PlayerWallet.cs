using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages player wallet and interfaces with the shared team economy (GameManager).
/// In Lethal Company mode, all crew members share a single company fund and a combined profit quota.
/// Any item purchase deducts from the shared pool; all salvaged scrap deposits into the shared pool.
/// </summary>
public class PlayerWallet : NetworkBehaviour
{
    [Header("Run Earnings (In-dungeon loose cash)")]
    public NetworkVariable<int> RunMoney = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Header("Persistent Savings (Mirrors shared crew funds)")]
    public NetworkVariable<int> BankedMoney = new NetworkVariable<int>(
        150,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Header("Combined Quota (Mirrors shared crew quota)")]
    [SerializeField] private int baseQuota = 250;
    [SerializeField] private int quotaIncrement = 125;

    public NetworkVariable<int> QuotaTarget = new NetworkVariable<int>(
        250,
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
            if (GameManager.Instance != null && GameManager.Instance.IsSpawned)
            {
                BankedMoney.Value = GameManager.Instance.GroupCredits.Value;
                QuotaProgress.Value = GameManager.Instance.QuotaProgress.Value;
                QuotaTarget.Value = GameManager.Instance.QuotaTarget.Value;
                QuotaLevel.Value = GameManager.Instance.QuotaLevel.Value;
                QuotaPoints.Value = GameManager.Instance.QuotaPoints.Value;
            }
            else
            {
                BankedMoney.Value = 150;
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
        Debug.Log($"[PlayerWallet] Player {OwnerClientId} picked up ${amount}. Current Run: ${RunMoney.Value}");
    }

    /// <summary>
    /// Server: Bank the current run earnings plus extraction bonus into persistent savings.
    /// In combined crew economy, deposits into GameManager.GroupCredits and advances Quota.
    /// </summary>
    public int BankRunMoney(int bonus = 0)
    {
        if (!IsServer)
        {
            return 0;
        }

        int totalExtracted = RunMoney.Value + bonus;
        RunMoney.Value = 0;

        if (GameManager.Instance != null && GameManager.Instance.IsSpawned)
        {
            GameManager.Instance.AddCrewCredits(totalExtracted, countsTowardsQuota: true);
            return totalExtracted;
        }

        BankedMoney.Value += totalExtracted;
        AddQuotaContribution(totalExtracted);
        return totalExtracted;
    }

    public int CalculateQuotaTarget(int level)
    {
        int target = baseQuota + Mathf.Max(0, level - 1) * quotaIncrement;
        return Mathf.Max(10, target);
    }

    /// <summary>
    /// Server: Adds money towards the crew's combined quota.
    /// </summary>
    public void AddQuotaContribution(int amount)
    {
        if (!IsServer || amount <= 0)
        {
            return;
        }

        if (GameManager.Instance != null && GameManager.Instance.IsSpawned)
        {
            GameManager.Instance.AddQuotaContribution(amount);
            return;
        }

        QuotaProgress.Value += amount;
        while (QuotaTarget.Value > 0 && QuotaProgress.Value >= QuotaTarget.Value)
        {
            QuotaProgress.Value -= QuotaTarget.Value;
            QuotaPoints.Value++;
            QuotaLevel.Value++;
            QuotaTarget.Value = CalculateQuotaTarget(QuotaLevel.Value);
        }
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
    /// In combined crew economy, deducts from shared GameManager.GroupCredits pool.
    /// </summary>
    public bool TrySpendBankedMoney(int amount)
    {
        if (!IsServer || amount <= 0)
        {
            return false;
        }

        if (GameManager.Instance != null && GameManager.Instance.IsSpawned)
        {
            return GameManager.Instance.TrySpendCrewCredits(amount);
        }

        if (BankedMoney.Value >= amount)
        {
            BankedMoney.Value -= amount;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Server: Full reset of both run, bank, and quota to initial state.
    /// </summary>
    public void ResetAllMoney()
    {
        if (!IsServer)
        {
            return;
        }

        RunMoney.Value = 0;
        if (GameManager.Instance != null && GameManager.Instance.IsSpawned)
        {
            GameManager.Instance.ResetCrewEconomy();
        }
        else
        {
            BankedMoney.Value = 0;
            QuotaProgress.Value = 0;
            QuotaPoints.Value = 0;
            QuotaLevel.Value = 1;
            QuotaTarget.Value = baseQuota;
        }
    }
}
