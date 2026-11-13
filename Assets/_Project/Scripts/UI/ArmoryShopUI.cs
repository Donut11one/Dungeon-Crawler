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

    private int openFrame = -1;
    private string feedbackMessage = string.Empty;
    private bool feedbackSuccess = true;
    private float feedbackTimer = 0f;
    private Vector2 scrollPos;
    private Texture2D solidTexture;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
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

        IsOpen = true;
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

        // Do not process close on the exact frame the shop was opened
        if (Time.frameCount == openFrame)
        {
            return;
        }

        // Close if game state leaves the lobby
        if (GameManager.Instance != null && GameManager.Instance.CurrentState.Value != GameManager.GameState.Lobby)
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
        float dialogW = 640f;
        float dialogH = 500f;
        float x = (Screen.width - dialogW) * 0.5f;
        float y = (Screen.height - dialogH) * 0.5f;

        // Border
        DrawRect(new Rect(x - 2f, y - 2f, dialogW + 4f, dialogH + 4f), new Color(0.15f, 0.75f, 0.95f, 0.9f));
        // Background
        DrawRect(new Rect(x, y, dialogW, dialogH), new Color(0.08f, 0.10f, 0.12f, 0.97f));

        // Title Header
        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        titleStyle.normal.textColor = new Color(0.2f, 0.85f, 1f);
        GUI.Label(new Rect(x, y + 12f, dialogW, 28f), "FACILITY ARMORY // QUARTERMASTER SHOP", titleStyle);

        // Subheader extraction risk note
        GUIStyle subStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter
        };
        subStyle.normal.textColor = new Color(1f, 0.82f, 0.25f);
        GUI.Label(new Rect(x, y + 42f, dialogW, 20f), "GEAR IS KEPT ACROSS RUNS IF YOU EXTRACT! DYING OR TIMING OUT WILL LOSE YOUR WEAPONS.", subStyle);

        // Player Bank savings
        PlayerWallet localWallet = FindLocalPlayerWallet();
        int bankMoney = localWallet != null ? localWallet.BankedMoney.Value : 0;
        PlayerInventory localInventory = FindLocalPlayerInventory();

        GUIStyle bankStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15,
            alignment = TextAnchor.MiddleRight,
            fontStyle = FontStyle.Bold
        };
        bankStyle.normal.textColor = new Color(1f, 0.85f, 0f);
        GUI.Label(new Rect(x + 20f, y + 66f, dialogW - 40f, 24f), $"CONTRACTOR BANK: ${bankMoney}", bankStyle);

        // Divider
        DrawRect(new Rect(x + 15f, y + 94f, dialogW - 30f, 1.5f), new Color(0.25f, 0.35f, 0.45f, 0.8f));

        // 3. Item List (Scroll Area)
        Rect scrollRect = new Rect(x + 15f, y + 102f, dialogW - 30f, dialogH - 170f);
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
            if (item.Type == ItemType.Weapon)
            {
                statText = $"Dmg: {item.Damage}  |  Speed: {item.AttackCooldown:0.00}s  |  Knockback: {item.KnockbackForce:0.0}";
            }
            else
            {
                statText = item.Description;
            }
            GUI.Label(new Rect(50f, itemY + 24f, 320f, 22f), statText, itemStatStyle);

            // Price tag
            int cost = item.BuyPrice > 0 ? item.BuyPrice : 25;
            GUI.Label(new Rect(itemW - 190f, itemY + 12f, 75f, 26f), $"${cost}", priceStyle);

            // Buy Button
            bool canAfford = bankMoney >= cost;
            bool hasSpace = localInventory != null && localInventory.HasFreeSlot();

            string buyLabel = "BUY";
            if (!canAfford)
            {
                buyLabel = "NO CASH";
            }
            else if (!hasSpace)
            {
                buyLabel = "FULL";
            }

            GUI.enabled = canAfford && hasSpace && localInventory != null;
            if (GUI.Button(new Rect(itemW - 105f, itemY + 10f, 95f, 30f), buyLabel))
            {
                if (localInventory != null)
                {
                    localInventory.BuyItemServerRpc(item.ItemId);
                }
            }
            GUI.enabled = true;
        }

        GUI.EndScrollView();

        // 4. Feedback Message Bar
        if (feedbackTimer > 0f && !string.IsNullOrEmpty(feedbackMessage))
        {
            GUIStyle fbStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            fbStyle.normal.textColor = feedbackSuccess ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.35f, 0.35f);
            DrawRect(new Rect(x + 20f, y + dialogH - 64f, dialogW - 40f, 22f), new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(new Rect(x + 20f, y + dialogH - 64f, dialogW - 40f, 22f), feedbackMessage, fbStyle);
        }

        // 5. Close Button
        if (GUI.Button(new Rect(x + (dialogW - 180f) * 0.5f, y + dialogH - 38f, 180f, 28f), "CLOSE SHOP [ESC / E]"))
        {
            CloseShop();
        }
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
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null)
        {
            return null;
        }

        NetworkObject playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (playerObj == null)
        {
            return null;
        }

        return playerObj.GetComponent<PlayerWallet>();
    }

    private PlayerInventory FindLocalPlayerInventory()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null)
        {
            return null;
        }

        NetworkObject playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        if (playerObj == null)
        {
            return null;
        }

        return playerObj.GetComponent<PlayerInventory>();
    }
}