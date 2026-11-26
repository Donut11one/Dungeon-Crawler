using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Modal UI for the Quartermaster Armory Shop in the staging lobby.
/// Allows contractors to purchase weapons and equipment using persistent banked savings.
/// Emphasizes risk-reward: items are kept across runs if players extract in time,
/// but are lost upon death or dungeon timeout.
/// </summary>
public class ArmoryShopUI : MonoBehaviour
{
    private static ArmoryShopUI instance;
    public static ArmoryShopUI Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<ArmoryShopUI>();
                if (instance == null)
                {
                    GameObject go = new GameObject("ArmoryShopUI");
                    instance = go.AddComponent<ArmoryShopUI>();
                }
            }
            return instance;
        }
    }

    public bool IsOpen { get; private set; }

    private float openTime = -1f;
    private int openFrame = -1;
    private string feedbackMessage = string.Empty;
    private bool feedbackSuccess = true;
    private float feedbackTimer = 0f;
    private Vector2 scrollPos;
    private Texture2D solidTexture;

    private int currentTab = 0; // 0 = Weapons Catalog, 1 = Sell Salvage
    private Vector2 sellScrollPos;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
    }

    private void OnDestroy()
    {
        if (solidTexture != null)
        {
            Destroy(solidTexture);
        }
    }

    public static void ShowFeedback(string message, bool success)
    {
        if (Instance != null)
        {
            Instance.SetFeedback(message, success);
        }
    }

    public void SetFeedback(string message, bool success)
    {
        feedbackMessage = message;
        feedbackSuccess = success;
        feedbackTimer = 3.5f;
    }

    public void OpenShop()
    {
        if (IsOpen)
        {
            return;
        }

        ItemRegistry.Initialize();
        IsOpen = true;
        openTime = Time.unscaledTime;
        openFrame = Time.frameCount;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        feedbackTimer = 0f;
    }

    public void CloseShop()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void ToggleShop()
    {
        if (IsOpen)
        {
            CloseShop();
        }
        else
        {
            OpenShop();
        }
    }

    private void Update()
    {
        if (feedbackTimer > 0f)
        {
            feedbackTimer -= Time.deltaTime;
        }

        if (!IsOpen)
        {
            return;
        }

        // Debounce: ignore close requests for 0.25 seconds after opening to avoid instant re-trigger
        if (Time.unscaledTime - openTime < 0.25f || Time.frameCount == openFrame)
        {
            return;
        }

        // Close if game state leaves the lobby
        if (GameManager.Instance != null && GameManager.Instance.IsSpawned && GameManager.Instance.CurrentState.Value != GameManager.GameState.Lobby)
        {
            CloseShop();
            return;
        }

        // Handle Escape / E to close
        if (Keyboard.current != null)
        {
            if (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.eKey.wasPressedThisFrame)
            {
                CloseShop();
            }
        }
    }

    private void OnGUI()
    {
        if (!IsOpen)
        {
            return;
        }

        InitTexture();

        // 1. Dark background overlay
        DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));

        // 2. Center Dialog Box
        float dialogW = 720f;
        float dialogH = 530f;
        float x = (Screen.width - dialogW) * 0.5f;
        float y = (Screen.height - dialogH) * 0.5f;

        // Outer cyan border
        DrawRect(new Rect(x - 2f, y - 2f, dialogW + 4f, dialogH + 4f), new Color(0.15f, 0.75f, 0.95f, 0.9f));
        // Dialog Background
        DrawRect(new Rect(x, y, dialogW, dialogH), new Color(0.08f, 0.10f, 0.12f, 0.97f));

        // Title Header
        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        titleStyle.normal.textColor = new Color(0.2f, 0.85f, 1f);
        GUI.Label(new Rect(x, y + 10f, dialogW, 26f), "FACILITY ARMORY & SALVAGE // QUARTERMASTER SHOP", titleStyle);

        // Subheader note
        GUIStyle subStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter
        };
        subStyle.normal.textColor = new Color(1f, 0.82f, 0.25f);
        GUI.Label(new Rect(x, y + 36f, dialogW, 18f), "DEFEAT MONSTERS TO HARVEST SALVAGE SCRAP  |  SELL TO QUARTERMASTER TO BUY WEAPONS", subStyle);

        // Player Bank & Carried Salvage info
        PlayerWallet localWallet = FindLocalPlayerWallet();
        int bankMoney = (GameManager.Instance != null && GameManager.Instance.IsSpawned)
            ? GameManager.Instance.GroupCredits.Value
            : ((localWallet != null && localWallet.IsSpawned) ? localWallet.BankedMoney.Value : (localWallet != null ? 150 : 0));
        int quotaProgress = (GameManager.Instance != null && GameManager.Instance.IsSpawned)
            ? GameManager.Instance.QuotaProgress.Value
            : (localWallet != null ? localWallet.QuotaProgress.Value : 0);
        int quotaTarget = (GameManager.Instance != null && GameManager.Instance.IsSpawned && GameManager.Instance.QuotaTarget.Value > 0)
            ? GameManager.Instance.QuotaTarget.Value
            : (localWallet != null ? localWallet.QuotaTarget.Value : 250);
        int daysLeft = (GameManager.Instance != null && GameManager.Instance.IsSpawned)
            ? GameManager.Instance.DaysRemaining.Value
            : 3;

        PlayerInventory localInventory = FindLocalPlayerInventory();
        int carriedScrapValue = localInventory != null ? localInventory.GetCarriedScrapValue() : 0;
        int carriedScrapCount = localInventory != null ? localInventory.GetCarriedScrapCount() : 0;

        GUIStyle salvageStatStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold
        };
        salvageStatStyle.normal.textColor = new Color(0.25f, 0.95f, 0.55f);
        GUI.Label(new Rect(x + 20f, y + 56f, 260f, 22f), $"CARRIED SALVAGE: ${carriedScrapValue} ({carriedScrapCount} items)", salvageStatStyle);

        GUIStyle bankStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleRight,
            fontStyle = FontStyle.Bold
        };
        bankStyle.normal.textColor = new Color(1f, 0.85f, 0f);
        GUI.Label(new Rect(x + 285f, y + 56f, dialogW - 305f, 22f), $"COMPANY FUNDS: ${bankMoney}  |  QUOTA: ${quotaProgress}/${quotaTarget} ({daysLeft}D)", bankStyle);

        // Divider
        DrawRect(new Rect(x + 15f, y + 80f, dialogW - 30f, 1.5f), new Color(0.25f, 0.35f, 0.45f, 0.8f));

        // 3. Navigation Tabs (Buy Weapons vs Sell Salvage)
        float tabW = (dialogW - 40f) * 0.5f;
        float tabY = y + 86f;

        // Tab 0: Weapons Catalog
        Color tab0Color = currentTab == 0 ? new Color(0.18f, 0.45f, 0.65f, 0.95f) : new Color(0.12f, 0.15f, 0.18f, 0.85f);
        DrawRect(new Rect(x + 15f, tabY, tabW, 28f), tab0Color);
        if (currentTab == 0)
        {
            DrawRect(new Rect(x + 15f, tabY + 26f, tabW, 2.5f), new Color(0.3f, 0.85f, 1f));
        }
        if (GUI.Button(new Rect(x + 15f, tabY, tabW, 28f), GUIContent.none, GUIStyle.none))
        {
            currentTab = 0;
        }
        GUIStyle tabText0 = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        tabText0.normal.textColor = currentTab == 0 ? Color.white : new Color(0.7f, 0.75f, 0.8f);
        GUI.Label(new Rect(x + 15f, tabY, tabW, 28f), "1. BUY WEAPONS & GEAR", tabText0);

        // Tab 1: Sell Salvage & Scrap
        float tab1X = x + 15f + tabW + 10f;
        Color tab1Color = currentTab == 1 ? new Color(0.15f, 0.50f, 0.30f, 0.95f) : new Color(0.12f, 0.15f, 0.18f, 0.85f);
        DrawRect(new Rect(tab1X, tabY, tabW, 28f), tab1Color);
        if (currentTab == 1)
        {
            DrawRect(new Rect(tab1X, tabY + 26f, tabW, 2.5f), new Color(0.3f, 1f, 0.55f));
        }
        string sellTabLabel = carriedScrapValue > 0
            ? $"2. SELL SALVAGE & SCRAP (+${carriedScrapValue})"
            : "2. SELL SALVAGE & SCRAP";
        if (GUI.Button(new Rect(tab1X, tabY, tabW, 28f), GUIContent.none, GUIStyle.none))
        {
            currentTab = 1;
        }
        GUIStyle tabText1 = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        tabText1.normal.textColor = currentTab == 1 ? Color.white : (carriedScrapValue > 0 ? new Color(0.4f, 1f, 0.6f) : new Color(0.7f, 0.75f, 0.8f));
        GUI.Label(new Rect(tab1X, tabY, tabW, 28f), sellTabLabel, tabText1);

        // Divider below tabs
        DrawRect(new Rect(x + 15f, tabY + 31f, dialogW - 30f, 1.5f), new Color(0.25f, 0.35f, 0.45f, 0.8f));

        // 4. Tab Contents
        if (currentTab == 0)
        {
            DrawWeaponsTab(x, y, dialogW, dialogH, bankMoney, carriedScrapValue, localInventory);
        }
        else
        {
            DrawSalvageTab(x, y, dialogW, dialogH, bankMoney, carriedScrapValue, carriedScrapCount, localInventory);
        }

        // 5. Feedback Message Bar
        if (feedbackTimer > 0f && !string.IsNullOrEmpty(feedbackMessage))
        {
            GUIStyle fbStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            fbStyle.normal.textColor = feedbackSuccess ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.35f, 0.35f);
            DrawRect(new Rect(x + 20f, y + dialogH - 64f, dialogW - 40f, 22f), new Color(0f, 0f, 0f, 0.65f));
            GUI.Label(new Rect(x + 20f, y + dialogH - 64f, dialogW - 40f, 22f), feedbackMessage, fbStyle);
        }

        // 6. Close Button
        if (GUI.Button(new Rect(x + (dialogW - 180f) * 0.5f, y + dialogH - 38f, 180f, 28f), "CLOSE SHOP [ESC / E]"))
        {
            CloseShop();
        }
    }

    private void DrawWeaponsTab(float x, float y, float dialogW, float dialogH, int bankMoney, int carriedScrapValue, PlayerInventory localInventory)
    {
        float contentTopY = y + 122f;

        // Quick Sell Banner if player carries scrap
        if (carriedScrapValue > 0)
        {
            DrawRect(new Rect(x + 15f, contentTopY, dialogW - 30f, 28f), new Color(0.08f, 0.24f, 0.14f, 0.9f));
            DrawRect(new Rect(x + 15f, contentTopY, 3f, 28f), new Color(0.2f, 0.95f, 0.45f));

            GUIStyle qsStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold
            };
            qsStyle.normal.textColor = new Color(0.85f, 1f, 0.85f);
            GUI.Label(new Rect(x + 26f, contentTopY + 4f, 320f, 20f), $"Carrying ${carriedScrapValue} in monster scrap! Cash in to buy weapons.", qsStyle);

            if (GUI.Button(new Rect(x + dialogW - 250f, contentTopY + 3f, 230f, 22f), $"QUICK SELL ALL SCRAP (+${carriedScrapValue} TO FUNDS)"))
            {
                if (localInventory != null)
                {
                    localInventory.SellAllScrapServerRpc();
                }
            }

            contentTopY += 34f;
        }

        Rect scrollRect = new Rect(x + 15f, contentTopY, dialogW - 30f, y + dialogH - 68f - contentTopY);
        List<ItemData> allItems = ItemRegistry.GetAllItems();
        List<ItemData> shopItems = new List<ItemData>();

        for (int i = 0; i < allItems.Count; i++)
        {
            ItemData it = allItems[i];
            if (it != null && it.Type != ItemType.Scrap)
            {
                shopItems.Add(it);
            }
        }

        if (shopItems.Count == 0)
        {
            ItemRegistry.Reload();
            allItems = ItemRegistry.GetAllItems();
            for (int i = 0; i < allItems.Count; i++)
            {
                ItemData it = allItems[i];
                if (it != null && it.Type != ItemType.Scrap)
                {
                    shopItems.Add(it);
                }
            }
        }

        float contentH = Mathf.Max(scrollRect.height, shopItems.Count * 56f);
        Rect contentRect = new Rect(0f, 0f, scrollRect.width - 18f, contentH);

        scrollPos = GUI.BeginScrollView(scrollRect, scrollPos, contentRect);

        GUIStyle itemNameStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold
        };
        itemNameStyle.normal.textColor = Color.white;

        GUIStyle itemStatStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11
        };
        itemStatStyle.normal.textColor = new Color(0.75f, 0.78f, 0.82f);

        GUIStyle priceStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleRight,
            fontStyle = FontStyle.Bold
        };
        priceStyle.normal.textColor = new Color(1f, 0.85f, 0.1f);

        for (int i = 0; i < shopItems.Count; i++)
        {
            ItemData item = shopItems[i];
            if (item == null)
            {
                continue;
            }

            float itemY = i * 56f;
            float itemW = contentRect.width;

            // Row background
            Color rowColor = (i % 2 == 0)
                ? new Color(0.12f, 0.14f, 0.17f, 0.75f)
                : new Color(0.15f, 0.17f, 0.20f, 0.75f);
            DrawRect(new Rect(0f, itemY, itemW, 50f), rowColor);

            // Colored slot accent chip
            DrawRect(new Rect(6f, itemY + 7f, 36f, 36f), item.SlotColor);

            // Item Name & Type
            GUI.Label(new Rect(50f, itemY + 4f, 260f, 20f), $"{item.DisplayName}  [{item.Type}]", itemNameStyle);

            // Stats / Description
            string statText;
            if (item.Type == ItemType.Weapon || item.Type == ItemType.RangedWeapon)
            {
                string rangeNote = item.Type == ItemType.RangedWeapon ? $"  |  Rng: {item.AttackRange:0}m" : "";
                string pelletNote = item.ItemId == "shotgun" ? "x8" : "";
                statText = $"Dmg: {item.Damage}{pelletNote}  |  Speed: {item.AttackCooldown:0.00}s  |  Knockback: {item.KnockbackForce:0.0}{rangeNote}";
            }
            else
            {
                statText = item.Description;
            }
            GUI.Label(new Rect(50f, itemY + 24f, 320f, 22f), statText, itemStatStyle);

            // Price tag
            int cost = item.BuyPrice > 0 ? item.BuyPrice : 25;
            GUI.Label(new Rect(itemW - 200f, itemY + 12f, 75f, 26f), $"${cost}", priceStyle);

            // Buy Button
            bool canAfford = bankMoney >= cost;
            bool canAffordWithScrap = !canAfford && (bankMoney + carriedScrapValue >= cost);
            bool hasSpace = localInventory == null || !localInventory.IsSpawned || localInventory.HasFreeSlot();

            string buyLabel = "BUY";
            if (canAfford)
            {
                buyLabel = hasSpace ? "BUY" : "FULL";
            }
            else if (canAffordWithScrap)
            {
                buyLabel = "+SELL SCRAP";
            }
            else if (!hasSpace)
            {
                buyLabel = "FULL";
            }
            else
            {
                buyLabel = "NO CASH";
            }

            GUI.enabled = (canAfford || canAffordWithScrap) && hasSpace && localInventory != null;
            if (GUI.Button(new Rect(itemW - 115f, itemY + 10f, 105f, 30f), buyLabel))
            {
                if (localInventory != null)
                {
                    if (canAfford)
                    {
                        if (localInventory.IsSpawned)
                        {
                            localInventory.BuyItemServerRpc(item.ItemId);
                        }
                        else
                        {
                            localInventory.AddItem(item.ItemId, 1);
                            SetFeedback($"Purchased {item.DisplayName}!", true);
                        }
                    }
                    else if (canAffordWithScrap)
                    {
                        // Quick-sell all scrap first, then user can buy
                        localInventory.SellAllScrapServerRpc();
                        currentTab = 0;
                    }
                }
            }
            GUI.enabled = true;
        }

        GUI.EndScrollView();
    }

    private void DrawSalvageTab(float x, float y, float dialogW, float dialogH, int bankMoney, int carriedScrapValue, int carriedScrapCount, PlayerInventory localInventory)
    {
        float contentTopY = y + 122f;

        // Top Action Bar
        DrawRect(new Rect(x + 15f, contentTopY, dialogW - 30f, 34f), new Color(0.10f, 0.14f, 0.18f, 0.9f));
        DrawRect(new Rect(x + 15f, contentTopY, 3f, 34f), new Color(0.2f, 0.85f, 0.5f));

        GUIStyle summaryStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold
        };
        summaryStyle.normal.textColor = new Color(0.3f, 1f, 0.55f);
        GUI.Label(new Rect(x + 26f, contentTopY + 7f, 320f, 20f), $"TOTAL SALVAGE CARRIED: ${carriedScrapValue} ({carriedScrapCount} items)", summaryStyle);

        GUI.enabled = carriedScrapValue > 0 && localInventory != null;
        if (GUI.Button(new Rect(x + dialogW - 245f, contentTopY + 4f, 225f, 26f), $"SELL ALL SALVAGE (+${carriedScrapValue} TO FUNDS)"))
        {
            if (localInventory != null)
            {
                localInventory.SellAllScrapServerRpc();
            }
        }
        GUI.enabled = true;

        contentTopY += 40f;

        // Items list in inventory
        Rect scrollRect = new Rect(x + 15f, contentTopY, dialogW - 30f, y + dialogH - 68f - contentTopY);

        List<int> occupiedSlots = new List<int>();
        if (localInventory != null && localInventory.Slots != null)
        {
            for (int i = 0; i < localInventory.Slots.Count; i++)
            {
                if (!localInventory.Slots[i].IsEmpty)
                {
                    occupiedSlots.Add(i);
                }
            }
        }

        if (occupiedSlots.Count == 0)
        {
            // Empty inventory guide
            DrawRect(scrollRect, new Color(0.06f, 0.08f, 0.10f, 0.65f));

            GUIStyle emptyTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            emptyTitleStyle.normal.textColor = new Color(1f, 0.8f, 0.3f);
            GUI.Label(new Rect(scrollRect.x, scrollRect.y + 30f, scrollRect.width, 24f), "NO MONSTER SALVAGE IN INVENTORY", emptyTitleStyle);

            GUIStyle guideStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter
            };
            guideStyle.normal.textColor = new Color(0.8f, 0.85f, 0.9f);

            string guideText = "Dive into the lower containment basement to hunt monsters.\n" +
                               "Defeating abnormalities drops valuable scrap parts:\n\n" +
                               "• Monster Core ($100) — Flesh Brutes & Large Abnormalities\n" +
                               "• Relic Circuitry ($150) — Shadow Prowlers & Tech Constructs\n" +
                               "• Anomalous Sensor ($75) — Stalkers & Security Drones\n" +
                               "• Salvaged Plating ($40) — Hive Swarmers & Mutants\n\n" +
                               "Extract alive with scrap in your hotbar to sell here and buy firearms!";
            GUI.Label(new Rect(scrollRect.x + 20f, scrollRect.y + 60f, scrollRect.width - 40f, 160f), guideText, guideStyle);
            return;
        }

        float contentH = Mathf.Max(scrollRect.height, occupiedSlots.Count * 56f);
        Rect contentRect = new Rect(0f, 0f, scrollRect.width - 18f, contentH);

        sellScrollPos = GUI.BeginScrollView(scrollRect, sellScrollPos, contentRect);

        GUIStyle itemNameStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold
        };
        itemNameStyle.normal.textColor = Color.white;

        GUIStyle itemDescStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11
        };
        itemDescStyle.normal.textColor = new Color(0.75f, 0.85f, 0.82f);

        GUIStyle payoutStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            alignment = TextAnchor.MiddleRight,
            fontStyle = FontStyle.Bold
        };
        payoutStyle.normal.textColor = new Color(0.3f, 1f, 0.5f);

        for (int i = 0; i < occupiedSlots.Count; i++)
        {
            int slotIdx = occupiedSlots[i];
            InventorySlot slot = localInventory.Slots[slotIdx];
            ItemData item = ItemRegistry.GetItem(slot.itemId.ToString());
            if (item == null)
            {
                continue;
            }

            float itemY = i * 56f;
            float itemW = contentRect.width;

            // Row background
            Color rowColor = (i % 2 == 0)
                ? new Color(0.12f, 0.14f, 0.17f, 0.75f)
                : new Color(0.15f, 0.17f, 0.20f, 0.75f);
            DrawRect(new Rect(0f, itemY, itemW, 50f), rowColor);

            // Colored slot accent chip
            DrawRect(new Rect(6f, itemY + 7f, 36f, 36f), item.SlotColor);

            // Item Name & Count
            string stackTag = slot.count > 1 ? $"  x{slot.count}" : "";
            GUI.Label(new Rect(50f, itemY + 4f, 260f, 20f), $"{item.DisplayName}{stackTag}  [Slot {slotIdx + 1}]", itemNameStyle);

            // Value calculation
            int payout;
            string valueNote;
            if (item.Type == ItemType.Scrap)
            {
                payout = item.BuyPrice * slot.count;
                valueNote = $"Monster Salvage: ${item.BuyPrice} ea (100% Value)";
            }
            else
            {
                payout = Mathf.Max(5, Mathf.RoundToInt(item.BuyPrice * 0.5f * slot.count));
                valueNote = $"Equipment Resale: ${Mathf.RoundToInt(item.BuyPrice * 0.5f)} ea (50% Refund)";
            }
            GUI.Label(new Rect(50f, itemY + 24f, 320f, 22f), valueNote, itemDescStyle);

            // Payout readout
            GUI.Label(new Rect(itemW - 190f, itemY + 12f, 85f, 26f), $"+${payout}", payoutStyle);

            // Sell Button
            if (GUI.Button(new Rect(itemW - 95f, itemY + 10f, 85f, 30f), "SELL"))
            {
                if (localInventory != null)
                {
                    localInventory.SellItemServerRpc(slotIdx);
                }
            }
        }

        GUI.EndScrollView();
    }

    private void InitTexture()
    {
        if (solidTexture == null)
        {
            solidTexture = new Texture2D(1, 1);
            solidTexture.SetPixel(0, 0, Color.white);
            solidTexture.Apply();
        }
    }

    private void DrawRect(Rect rect, Color color)
    {
        InitTexture();
        Color prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, solidTexture);
        GUI.color = prev;
    }

    private PlayerWallet FindLocalPlayerWallet()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
        {
            NetworkObject playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (playerObj != null)
            {
                return playerObj.GetComponent<PlayerWallet>();
            }
        }

        PlayerController[] pcs = FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude);
        for (int i = 0; i < pcs.Length; i++)
        {
            if (pcs[i] != null && pcs[i].IsOwner)
            {
                return pcs[i].GetComponent<PlayerWallet>();
            }
        }

        if (pcs.Length > 0 && pcs[0] != null)
        {
            return pcs[0].GetComponent<PlayerWallet>();
        }

        return null;
    }

    private PlayerInventory FindLocalPlayerInventory()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
        {
            NetworkObject playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (playerObj != null)
            {
                return playerObj.GetComponent<PlayerInventory>();
            }
        }

        PlayerController[] pcs = FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude);
        for (int i = 0; i < pcs.Length; i++)
        {
            if (pcs[i] != null && pcs[i].IsOwner)
            {
                return pcs[i].GetComponent<PlayerInventory>();
            }
        }

        if (pcs.Length > 0 && pcs[0] != null)
        {
            return pcs[0].GetComponent<PlayerInventory>();
        }

        return null;
    }
}
