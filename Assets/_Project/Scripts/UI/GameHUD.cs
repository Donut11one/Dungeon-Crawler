using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// All-in-one game HUD using OnGUI (no Canvas setup needed).
/// Displays: lobby staging status & mutual ready system, countdown timer, health,
/// run earnings vs persistent bank savings, crosshair, extraction status, and all-player scoreboards.
/// </summary>
public class GameHUD : MonoBehaviour
{
    private struct ExtractedRecord
    {
        public int bankedAmount;
        public int totalBank;
    }

    private GUIStyle timerStyle;
    private GUIStyle healthStyle;
    private GUIStyle moneyStyle;
    private GUIStyle stateStyle;
    private GUIStyle subStateStyle;
    private GUIStyle infoStyle;
    private GUIStyle crosshairStyle;
    private GUIStyle scoreBoardStyle;
    private GUIStyle extractedBannerStyle;
    private GUIStyle healthBarTextStyle;
    private GUIStyle enemyBarTextStyle;
    private bool stylesInitialized;
    private Texture2D solidTexture;
    private bool isRoundOverSummaryActive = false;

    // Quota celebration banner state
    private static float quotaCelebrationTimer = 0f;
    private static ulong quotaCelebrationClientId;
    private static int quotaCelebrationPointsAdded;
    private static int quotaCelebrationTotalPoints;
    private static int quotaCelebrationNextTarget;
    private static int quotaCelebrationDays = 3;
    private static bool isQuotaAlert = false;
    private static string quotaBannerTitle = "";
    private static string quotaBannerSubtitle = "";

    public static void TriggerCrewQuotaCelebration(int pointsAdded, int totalPoints, int nextTarget, int days)
    {
        isQuotaAlert = false;
        quotaCelebrationPointsAdded = pointsAdded;
        quotaCelebrationTotalPoints = totalPoints;
        quotaCelebrationNextTarget = nextTarget;
        quotaCelebrationDays = days;
        quotaBannerTitle = "★ CREW PROFIT QUOTA MET! ★";
        quotaBannerSubtitle = $"+{pointsAdded} PRESTIGE POINT! (Total: {totalPoints} PTS)  |  Next Target: ${nextTarget} ({days} Dives Left)";
        quotaCelebrationTimer = 5.0f;
    }

    public static void TriggerQuotaDeadlineFailed(int penaltyDeducted, int remainingCredits, int nextTarget)
    {
        isQuotaAlert = true;
        quotaCelebrationPointsAdded = 0;
        quotaCelebrationTotalPoints = 0;
        quotaCelebrationNextTarget = nextTarget;
        quotaCelebrationDays = 3;
        quotaBannerTitle = "⚠ QUOTA DEADLINE EXPIRED! ⚠";
        quotaBannerSubtitle = $"Disciplinary Fine: -${penaltyDeducted} Crew Funds (Balance: ${remainingCredits}) | Reset to Tier 1: ${nextTarget} (3 Days)";
        quotaCelebrationTimer = 5.0f;
    }

    public static void TriggerQuotaCelebration(ulong clientId, int pointsAdded, int totalPoints, int nextTarget)
    {
        TriggerCrewQuotaCelebration(pointsAdded, totalPoints, nextTarget, 3);
    }

    // Tracks which players escaped and their banked money (populated via RPC from GameManager)
    private readonly Dictionary<ulong, ExtractedRecord> extractedThisRound = new();
    private float fpsTimer = 0f;
    private int fpsFrameCount = 0;
    private int currentFps = 60;

    private void InitStyles()
    {
        if (stylesInitialized)
        {
            return;
        }

        timerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 36,
            alignment = TextAnchor.UpperCenter,
            fontStyle = FontStyle.Bold
        };
        timerStyle.normal.textColor = Color.white;

        healthStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 24,
            alignment = TextAnchor.LowerLeft,
            fontStyle = FontStyle.Bold
        };
        healthStyle.normal.textColor = Color.green;

        moneyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold
        };
        moneyStyle.normal.textColor = new Color(1f, 0.85f, 0f); // Gold

        stateStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 48,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };

        subStateStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 26,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        subStateStyle.normal.textColor = new Color(1f, 0.85f, 0f);

        infoStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            alignment = TextAnchor.LowerRight
        };
        infoStyle.normal.textColor = Color.gray;

        crosshairStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 24,
            alignment = TextAnchor.MiddleCenter
        };
        crosshairStyle.normal.textColor = Color.white;

        scoreBoardStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 17,
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold
        };
        scoreBoardStyle.normal.textColor = Color.white;

        extractedBannerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 32,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        extractedBannerStyle.normal.textColor = new Color(0.2f, 1f, 0.2f);

        healthBarTextStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        healthBarTextStyle.normal.textColor = Color.white;

        enemyBarTextStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        enemyBarTextStyle.normal.textColor = Color.white;

        stylesInitialized = true;
    }

    /// <summary>
    /// Shows the round over summary screen and frees cursor.
    /// </summary>
    public void ShowRoundOver()
    {
        isRoundOverSummaryActive = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>
    /// Called across network via GameManager ClientRpc when round starts, restarts, or returns to lobby.
    /// Wipes all stale round-over states, extraction statuses, and locks cursor for gameplay.
    /// </summary>
    public void ClearRoundState()
    {
        isRoundOverSummaryActive = false;
        extractedThisRound.Clear();
        if (InGameMenuUI.Instance == null || !InGameMenuUI.Instance.IsOpen)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    /// <summary>
    /// Called by GameManager RPC when a player successfully extracts.
    /// </summary>
    public void RegisterExtraction(ulong clientId, int bankedAmount, int totalBank)
    {
        extractedThisRound[clientId] = new ExtractedRecord
        {
            bankedAmount = bankedAmount,
            totalBank = totalBank
        };
    }

    private void OnGUI()
    {
        InitStyles();

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            return;
        }
        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        // When the InGameMenuUI (Pause / Leave menu) or Armory Shop is open, suppress HUD to prevent any visual overlap
        if (InGameMenuUI.Instance != null && InGameMenuUI.Instance.IsOpen)
        {
            return;
        }

        if (ArmoryShopUI.Instance != null && ArmoryShopUI.Instance.IsOpen)
        {
            return;
        }

        DrawCrosshair();
        DrawEnemyHealthBars();
        DrawLobbyHUD();
        DrawTimer();
        DrawHealth();
        DrawMoney();
        DrawQuotaHUD();
        DrawHotbar();
        DrawControlsHint();
        DrawConnectionInfo();
        DrawRoundState();
        DrawExtractionBanner();
        DrawScoreboard();
        DrawQuotaCelebration();
    }

    private void DrawLobbyHUD()
    {
        if (GameManager.Instance == null)
        {
            return;
        }

        if (GameManager.Instance.CurrentState.Value != GameManager.GameState.Lobby)
        {
            return;
        }

        GUIStyle lobbyTitleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 26,
            alignment = TextAnchor.UpperCenter,
            fontStyle = FontStyle.Bold
        };
        lobbyTitleStyle.normal.textColor = new Color(0.15f, 0.85f, 1f);

        Rect titleRect = new Rect(0f, 20f, Screen.width, 32f);
        GUI.Label(titleRect, "STAGING BUNKER // CONTRACTOR BASECAMP", lobbyTitleStyle);

        int ready = 0;
        int total = 1;
        float countdown = -1f;

        if (LobbyArea.Instance != null)
        {
            ready = LobbyArea.Instance.ReadyCount.Value;
            total = LobbyArea.Instance.TotalPlayers.Value;
            countdown = LobbyArea.Instance.DiveCountdown.Value;
        }
        else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.ConnectedClientsList != null)
        {
            total = NetworkManager.Singleton.ConnectedClientsList.Count;
        }

        PlayerController localPc = FindLocalPlayerController();
        bool isLocalReady = localPc != null && localPc.IsReadyToDive.Value;

        if (countdown > 0f)
        {
            GUIStyle countdownStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                alignment = TextAnchor.UpperCenter,
                fontStyle = FontStyle.Bold
            };
            countdownStyle.normal.textColor = new Color(0.2f, 1f, 0.3f);

            int sec = Mathf.CeilToInt(countdown);
            Rect countRect = new Rect(0f, 55f, Screen.width, 35f);
            GUI.Label(countRect, $"ALL SQUAD MEMBERS READY! DIVING IN {sec}s...", countdownStyle);
        }
        else
        {
            GUIStyle subStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.UpperCenter,
                fontStyle = FontStyle.Bold
            };
            subStyle.normal.textColor = (ready >= total && total > 0) ? Color.green : new Color(1f, 0.85f, 0.2f);

            Rect statusRect = new Rect(0f, 55f, Screen.width, 28f);
            GUI.Label(statusRect, $"EXPEDITION READY: {ready} / {total} CONTRACTORS AGREED", subStyle);

            GUIStyle hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                alignment = TextAnchor.UpperCenter
            };
            hintStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f, 0.8f);

            Rect hintRect = new Rect(0f, 85f, Screen.width, 22f);
            GUI.Label(hintRect, "Both contractors must step on the ELEVATOR PLATFORM or press [F] to agree", hintStyle);
        }

        // Ready toggle button
        if (localPc != null)
        {
            string btnText = isLocalReady ? "YOU ARE READY ✓  (Press F to Cancel)" : "AGREE TO DIVE  (Press F or Step on Pad)";
            Color originalBg = GUI.backgroundColor;
            GUI.backgroundColor = isLocalReady ? new Color(0.2f, 0.8f, 0.3f) : new Color(0.9f, 0.7f, 0.1f);

            if (GUI.Button(new Rect(Screen.width / 2f - 180f, 115f, 360f, 38f), btnText))
            {
                localPc.SetReadyServerRpc(!isLocalReady);
            }

            // If Host, also offer a direct one-click "START EXPEDITION NOW" button
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                GUI.backgroundColor = new Color(0.15f, 0.85f, 1f);
                if (GUI.Button(new Rect(Screen.width / 2f - 180f, 160f, 360f, 32f), "START EXPEDITION NOW (HOST)"))
                {
                    if (GameManager.Instance != null)
                    {
                        GameManager.Instance.StartRound();
                    }
                }
            }

            GUI.backgroundColor = originalBg;
        }

        // Display shareable Relay Join Code in top-right corner while in the staging bunker
        if (RelayManager.IsRelayActive && !string.IsNullOrEmpty(RelayManager.CurrentJoinCode))
        {
            GUIStyle codeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Bold
            };
            codeStyle.normal.textColor = new Color(0.2f, 0.85f, 1f);
            GUI.Label(new Rect(Screen.width - 260f, 15f, 240f, 22f), $"RELAY CODE: {RelayManager.CurrentJoinCode}", codeStyle);

            if (GUI.Button(new Rect(Screen.width - 150f, 38f, 130f, 22f), "COPY JOIN CODE"))
            {
                GUIUtility.systemCopyBuffer = RelayManager.CurrentJoinCode;
            }
        }
    }


    private void DrawHotbar()
    {
        PlayerInventory inventory = FindLocalPlayerInventory();
        if (inventory == null)
        {
            return;
        }

        int slotCount = PlayerInventory.HotbarSlotCount;
        float slotSize = 46f;
        float slotSpacing = 6f;
        float totalWidth = slotCount * slotSize + (slotCount - 1) * slotSpacing;

        float startX = (Screen.width - totalWidth) * 0.5f;
        float startY = Screen.height - 90f;

        int selectedSlot = inventory.SelectedSlotIndex.Value;

        // Draw active item name & attack recharge bar centered above the hotbar
        ItemData activeItem = inventory.GetSelectedItem();
        string activeName = activeItem != null ? activeItem.DisplayName.ToUpper() : "FISTS";
        Color activeColor = activeItem != null ? activeItem.SlotColor : new Color(0.9f, 0.9f, 0.9f);

        PlayerRangedCombat rangedCombat = FindLocalPlayerRangedCombat();
        if (activeItem != null && activeItem.Type == ItemType.RangedWeapon && rangedCombat != null)
        {
            if (rangedCombat.IsReloading)
            {
                float barW = 160f;
                float barH = 5f;
                float barX = (Screen.width - barW) * 0.5f;
                float barY = startY - 26f;
                float fillPct = rangedCombat.ReloadProgress;

                DrawSolidRect(new Rect(barX - 1f, barY - 1f, barW + 2f, barH + 2f), new Color(0.05f, 0.05f, 0.05f, 0.85f));
                DrawSolidRect(new Rect(barX, barY, barW, barH), new Color(0.2f, 0.2f, 0.2f, 0.65f));
                DrawSolidRect(new Rect(barX, barY, barW * fillPct, barH), new Color(1f, 0.85f, 0.2f));

                GUIStyle reloadStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11,
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold
                };
                reloadStyle.normal.textColor = new Color(1f, 0.85f, 0.2f);
                GUI.Label(new Rect(barX - 50f, barY - 16f, barW + 100f, 16f), $"[ RELOADING... {Mathf.RoundToInt(fillPct * 100f)}% ]", reloadStyle);
            }
            else if (rangedCombat.CurrentAmmo == 0)
            {
                GUIStyle emptyStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold
                };
                emptyStyle.normal.textColor = new Color(1f, 0.25f, 0.25f);
                GUI.Label(new Rect(startX - 75f, startY - 23f, totalWidth + 150f, 20f), $"[ {activeName}  -  EMPTY! PRESS 'R' TO RELOAD ]", emptyStyle);
            }
            else
            {
                GUIStyle activeItemStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold
                };
                activeItemStyle.normal.textColor = activeColor;
                GUI.Label(new Rect(startX - 75f, startY - 23f, totalWidth + 150f, 20f), $"[ {activeName}  |  AMMO: {rangedCombat.CurrentAmmo}/{rangedCombat.MaxAmmo}  (R: Reload) ]", activeItemStyle);
            }
        }
        else if (activeItem != null && activeItem.Type == ItemType.Consumable)
        {
            GUIStyle activeItemStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            activeItemStyle.normal.textColor = activeColor;
            GUI.Label(new Rect(startX - 75f, startY - 23f, totalWidth + 150f, 20f), $"[ {activeName}  |  [L-Click] Heal +50 HP ]", activeItemStyle);
        }
        else if (activeItem != null && activeItem.ItemId == "flashlight")
        {
            GUIStyle activeItemStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            activeItemStyle.normal.textColor = activeColor;
            string state = inventory.IsFlashlightShining ? "ON" : "OFF";
            GUI.Label(new Rect(startX - 75f, startY - 23f, totalWidth + 150f, 20f), $"[ {activeName} ({state})  |  [L-Click] / [F] Toggle Beam ]", activeItemStyle);
        }
        else if (activeItem != null && activeItem.Type == ItemType.Scrap)
        {
            GUIStyle activeItemStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            activeItemStyle.normal.textColor = activeColor;
            int totalVal = activeItem.BuyPrice;
            if (inventory.Slots != null && selectedSlot >= 0 && selectedSlot < inventory.Slots.Count)
            {
                totalVal *= inventory.Slots[selectedSlot].count;
            }
            GUI.Label(new Rect(startX - 75f, startY - 23f, totalWidth + 150f, 20f), $"[ {activeName}  |  SALVAGE VALUE: ${totalVal}  (Sell at Shop) ]", activeItemStyle);
        }
        else
        {
            PlayerAttack localAttack = FindLocalPlayerAttack();
            if (localAttack != null && localAttack.IsOnCooldown)
            {
                float barW = 160f;
                float barH = 4f;
                float barX = (Screen.width - barW) * 0.5f;
                float barY = startY - 26f;
                float fillPct = localAttack.CooldownProgress;

                DrawSolidRect(new Rect(barX - 1f, barY - 1f, barW + 2f, barH + 2f), new Color(0.05f, 0.05f, 0.05f, 0.85f));
                DrawSolidRect(new Rect(barX, barY, barW, barH), new Color(0.2f, 0.2f, 0.2f, 0.65f));
                Color fillColor = localAttack.IsAttacking ? new Color(1f, 0.25f, 0.1f) : new Color(0.2f, 0.7f, 1f);
                DrawSolidRect(new Rect(barX, barY, barW * fillPct, barH), fillColor);

                string stateTag = localAttack.IsAttacking ? $"[ SWINGING // {activeName} ]" : $"[ RECOVERING ({localAttack.AttackTimer:0.0}s) ]";
                Color tagColor = localAttack.IsAttacking ? new Color(1f, 0.35f, 0.15f) : new Color(0.3f, 0.8f, 1f);
                GUIStyle tagStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11,
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold
                };
                tagStyle.normal.textColor = tagColor;
                GUI.Label(new Rect(barX - 50f, barY - 15f, barW + 100f, 15f), stateTag, tagStyle);
            }
            else
            {
                GUIStyle activeItemStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold
                };
                activeItemStyle.normal.textColor = activeColor;
                GUI.Label(new Rect(startX - 50f, startY - 22f, totalWidth + 100f, 18f), $"[ {activeName} ]", activeItemStyle);
            }
        }

        GUIStyle slotNumStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            alignment = TextAnchor.UpperLeft,
            fontStyle = FontStyle.Bold
        };
        slotNumStyle.normal.textColor = new Color(0.75f, 0.75f, 0.75f, 0.8f);

        GUIStyle itemNameStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };

        GUIStyle countStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            alignment = TextAnchor.LowerRight,
            fontStyle = FontStyle.Bold
        };
        countStyle.normal.textColor = Color.yellow;

        for (int i = 0; i < slotCount; i++)
        {
            float x = startX + i * (slotSize + slotSpacing);
            float y = startY;
            Rect slotRect = new Rect(x, y, slotSize, slotSize);

            bool isSelected = (i == selectedSlot);

            // Slot outer border (Thick gold border for selected slot!)
            if (isSelected)
            {
                DrawSolidRect(new Rect(x - 3f, y - 3f, slotSize + 6f, slotSize + 6f), new Color(1f, 0.85f, 0.2f, 0.95f));
                DrawSolidRect(slotRect, new Color(0.22f, 0.22f, 0.22f, 0.9f));
            }
            else
            {
                DrawSolidRect(new Rect(x - 1f, y - 1f, slotSize + 2f, slotSize + 2f), new Color(0.08f, 0.08f, 0.08f, 0.85f));
                DrawSolidRect(slotRect, new Color(0.14f, 0.14f, 0.14f, 0.75f));
            }

            // Slot number indicator "1", "2"...
            GUI.Label(new Rect(x + 3f, y + 2f, 20f, 14f), $"{i + 1}", slotNumStyle);

            InventorySlot slot = inventory.GetSlot(i);
            if (!slot.IsEmpty)
            {
                ItemData item = ItemRegistry.GetItem(slot.itemId.ToString());
                if (item != null)
                {
                    // Rudimentary colored inner box representing the item
                    float iconSize = 20f;
                    Rect iconRect = new Rect(x + (slotSize - iconSize) * 0.5f, y + 9f, iconSize, iconSize);
                    DrawSolidRect(iconRect, item.SlotColor);

                    // Short display label
                    string shortName = item.DisplayName.Length > 6 ? item.DisplayName.Substring(0, 5) + "." : item.DisplayName;
                    itemNameStyle.normal.textColor = Color.white;
                    GUI.Label(new Rect(x, y + 28f, slotSize, 16f), shortName, itemNameStyle);

                    if (slot.count > 1)
                    {
                        GUI.Label(new Rect(x, y, slotSize - 3f, slotSize - 2f), $"{slot.count}", countStyle);
                    }
                }
            }
            else if (isSelected)
            {
                // Selected empty slot shows "Fist"
                itemNameStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f, 0.7f);
                GUI.Label(new Rect(x, y + 15f, slotSize, 20f), "Fist", itemNameStyle);
            }
        }
    }

    private void DrawControlsHint()
    {
        GUIStyle hintStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter
        };
        hintStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f, 0.75f);

        float hintW = Screen.width - 500f;
        Rect rect = new Rect(250f, Screen.height - 28f, hintW, 25f);

        if (GameManager.Instance != null && GameManager.Instance.CurrentState.Value == GameManager.GameState.Lobby)
        {
            GUI.Label(rect, "[WASD] Move   |   [Space] Jump   |   [1-5] Hotbar   |   [Left Click] Attack / Shoot   |   [R] Reload   |   [E] Trade / Interact   |   [F] Agree to Dive", hintStyle);
        }
        else
        {
            GUI.Label(rect, "[WASD] Move   |   [Space] Jump   |   [1-5] Hotbar   |   [Left Click] Attack / Shoot   |   [R] Reload   |   [E] Grab/Drop   |   [Right Click] Throw", hintStyle);
        }
    }

    private void DrawCrosshair()
    {
        PlayerAttack attack = FindLocalPlayerAttack();
        PlayerRangedCombat ranged = FindLocalPlayerRangedCombat();
        Color crossColor = Color.white;
        string crossChar = "+";

        if (ranged != null && ranged.IsHoldingRangedWeapon)
        {
            if (ranged.IsReloading)
            {
                crossColor = new Color(1f, 0.85f, 0.2f, 0.9f);
                crossChar = "⟳";
            }
            else if (ranged.CurrentAmmo == 0)
            {
                crossColor = new Color(1f, 0.25f, 0.2f, 0.9f);
                crossChar = "!";
            }
            else
            {
                crossColor = new Color(0.2f, 1f, 0.45f, 0.95f);
                crossChar = "+";
            }
        }
        else if (attack != null)
        {
            if (attack.CurrentCombatState.Value == PlayerCombatState.Attacking)
            {
                crossColor = new Color(1f, 0.25f, 0.1f);
                crossChar = "x";
            }
            else if (attack.CurrentCombatState.Value == PlayerCombatState.Cooldown)
            {
                crossColor = new Color(0.2f, 0.65f, 0.95f, 0.85f);
                crossChar = "·";
            }
        }

        crosshairStyle.normal.textColor = crossColor;
        Rect rect = new Rect(Screen.width / 2f - 15f, Screen.height / 2f - 15f, 30f, 30f);
        GUI.Label(rect, crossChar, crosshairStyle);

        // Under crosshair: Draw slim reload or attack recharge indicator
        if (ranged != null && ranged.IsHoldingRangedWeapon && ranged.IsReloading)
        {
            float fillPct = ranged.ReloadProgress;
            float barW = 28f;
            float barH = 3f;
            float barX = Screen.width / 2f - barW * 0.5f;
            float barY = Screen.height / 2f + 16f;

            DrawSolidRect(new Rect(barX - 1f, barY - 1f, barW + 2f, barH + 2f), new Color(0.05f, 0.05f, 0.05f, 0.8f));
            DrawSolidRect(new Rect(barX, barY, barW, barH), new Color(0.2f, 0.2f, 0.2f, 0.6f));
            DrawSolidRect(new Rect(barX, barY, barW * fillPct, barH), new Color(1f, 0.85f, 0.2f));
        }
        else if (attack != null && attack.IsOnCooldown)
        {
            float fillPct = attack.CooldownProgress;
            float barW = 28f;
            float barH = 3f;
            float barX = Screen.width / 2f - barW * 0.5f;
            float barY = Screen.height / 2f + 16f;

            DrawSolidRect(new Rect(barX - 1f, barY - 1f, barW + 2f, barH + 2f), new Color(0.05f, 0.05f, 0.05f, 0.8f));
            DrawSolidRect(new Rect(barX, barY, barW, barH), new Color(0.2f, 0.2f, 0.2f, 0.6f));
            Color fillColor = attack.IsAttacking ? new Color(1f, 0.25f, 0.1f) : new Color(0.2f, 0.7f, 1f);
            DrawSolidRect(new Rect(barX, barY, barW * fillPct, barH), fillColor);
        }
    }

    private void DrawTimer()
    {
        if (GameManager.Instance == null)
        {
            return;
        }

        float timer = GameManager.Instance.RoundTimer.Value;
        GameManager.GameState state = GameManager.Instance.CurrentState.Value;

        if (state != GameManager.GameState.Playing)
        {
            return;
        }

        int minutes = Mathf.FloorToInt(timer / 60f);
        int seconds = Mathf.FloorToInt(timer % 60f);

        timerStyle.normal.textColor = timer <= 30f ? Color.red : Color.white;

        Rect rect = new Rect(Screen.width / 2f - 100f, 15f, 200f, 50f);
        GUI.Label(rect, $"{minutes:00}:{seconds:00}", timerStyle);
    }

    private void DrawHealth()
    {
        PlayerHealth localHealth = FindLocalPlayerHealth();
        if (localHealth == null)
        {
            return;
        }

        int hp = Mathf.Max(0, localHealth.CurrentHealth.Value);
        int maxHp = localHealth.MaxHealth > 0 ? localHealth.MaxHealth : 100;
        float pct = Mathf.Clamp01((float)hp / maxHp);

        float barX = 20f;
        float barY = Screen.height - 54f;
        float barW = 220f;
        float barH = 20f;

        // Outer dark border
        DrawSolidRect(new Rect(barX - 2f, barY - 2f, barW + 4f, barH + 4f), new Color(0.08f, 0.08f, 0.08f, 0.9f));

        // Background track
        DrawSolidRect(new Rect(barX, barY, barW, barH), new Color(0.2f, 0.2f, 0.2f, 0.75f));

        // Health fill lerp (Green when high, Yellow when mid, Red when low)
        Color fillColor;
        if (pct > 0.5f)
        {
            fillColor = Color.Lerp(new Color(1f, 0.85f, 0.1f), new Color(0.2f, 0.85f, 0.25f), (pct - 0.5f) * 2f);
        }
        else
        {
            fillColor = Color.Lerp(new Color(0.9f, 0.15f, 0.15f), new Color(1f, 0.85f, 0.1f), pct * 2f);
        }

        if (pct > 0f)
        {
            DrawSolidRect(new Rect(barX, barY, barW * pct, barH), fillColor);
        }

        // Text overlay
        if (localHealth.IsDead.Value)
        {
            healthBarTextStyle.normal.textColor = new Color(1f, 0.4f, 0.4f);
            GUI.Label(new Rect(barX, barY - 1f, barW, barH), "DEAD - RESPAWNING...", healthBarTextStyle);
        }
        else
        {
            healthBarTextStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(barX, barY - 1f, barW, barH), $"HP  {hp} / {maxHp}", healthBarTextStyle);
        }
    }

    private void DrawEnemyHealthBars()
    {
        if (GameManager.Instance == null || GameManager.Instance.CurrentState.Value != GameManager.GameState.Playing)
        {
            return;
        }

        Camera cam = GetActivePlayerCamera();
        if (cam == null)
        {
            return;
        }

        float maxDistance = 14f;
        float fadeStartDist = 10f;

        for (int i = EnemyHealth.ActiveEnemies.Count - 1; i >= 0; i--)
        {
            EnemyHealth enemy = EnemyHealth.ActiveEnemies[i];
            if (enemy == null)
            {
                EnemyHealth.ActiveEnemies.RemoveAt(i);
                continue;
            }

            if (enemy.IsDead.Value)
            {
                continue;
            }

            // Determine world position above enemy head
            Vector3 headWorldPos = enemy.transform.position + Vector3.up * 2.35f;
            Collider enemyCol = enemy.GetComponent<Collider>();
            if (enemyCol != null)
            {
                headWorldPos = enemyCol.bounds.center + Vector3.up * (enemyCol.bounds.extents.y + 0.35f);
            }

            Vector3 camPos = cam.transform.position;
            Vector3 toEnemy = headWorldPos - camPos;
            float dist = toEnemy.magnitude;

            if (dist > maxDistance || dist < 0.1f)
            {
                continue;
            }

            // Camera frustum check (in front of camera)
            Vector3 screenPos = cam.WorldToScreenPoint(headWorldPos);
            if (screenPos.z <= 0.2f)
            {
                continue;
            }

            // Line-of-sight raycast to ensure bars aren't visible through dungeon walls
            float rayTestDist = dist - 0.4f;
            if (rayTestDist > 0.2f)
            {
                Vector3 rayOrigin = camPos + toEnemy.normalized * 0.3f;
                RaycastHit[] hits = Physics.RaycastAll(rayOrigin, toEnemy.normalized, rayTestDist, ~0, QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

                bool blocked = false;
                PlayerController localPc = FindLocalPlayerController();
                for (int h = 0; h < hits.Length; h++)
                {
                    RaycastHit hit = hits[h];
                    if (hit.transform == enemy.transform || hit.transform.IsChildOf(enemy.transform))
                    {
                        break;
                    }

                    if (localPc != null && (hit.transform == localPc.transform || hit.transform.IsChildOf(localPc.transform)))
                    {
                        continue;
                    }

                    blocked = true;
                    break;
                }

                if (blocked)
                {
                    continue;
                }
            }

            // Fade alpha based on distance (solid up to fadeStartDist, fading out to maxDistance)
            float alpha = 1f;
            if (dist > fadeStartDist)
            {
                alpha = Mathf.Clamp01((maxDistance - dist) / (maxDistance - fadeStartDist));
            }

            if (alpha <= 0.02f)
            {
                continue;
            }

            // Convert screen position to GUI coordinates
            float guiX = screenPos.x;
            float guiY = Screen.height - screenPos.y;

            if (guiX < -80f || guiX > Screen.width + 80f || guiY < -50f || guiY > Screen.height + 50f)
            {
                continue;
            }

            int enemyHp = Mathf.Max(0, enemy.CurrentHealth.Value);
            int enemyMaxHp = enemy.MaxHealth > 0 ? enemy.MaxHealth : 50;
            float pct = Mathf.Clamp01((float)enemyHp / enemyMaxHp);

            float barW = 68f;
            float barH = 7f;
            float barX = guiX - barW * 0.5f;
            float barY = guiY - barH * 0.5f;

            // Border & background track
            DrawSolidRect(new Rect(barX - 1f, barY - 1f, barW + 2f, barH + 2f), new Color(0.05f, 0.05f, 0.05f, 0.85f * alpha));
            DrawSolidRect(new Rect(barX, barY, barW, barH), new Color(0.2f, 0.2f, 0.2f, 0.7f * alpha));

            // Fill color (red to yellow to green)
            Color fillColor;
            if (pct > 0.5f)
            {
                fillColor = Color.Lerp(Color.yellow, new Color(0.25f, 0.85f, 0.25f), (pct - 0.5f) * 2f);
            }
            else
            {
                fillColor = Color.Lerp(new Color(0.9f, 0.15f, 0.15f), Color.yellow, pct * 2f);
            }
            fillColor.a *= alpha;

            if (pct > 0f)
            {
                DrawSolidRect(new Rect(barX, barY, barW * pct, barH), fillColor);
            }

            // Enemy name above the floating bar
            string enemyName = enemy.DisplayName;
            GUIStyle enemyNameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.LowerCenter,
                fontStyle = FontStyle.Bold
            };
            enemyNameStyle.normal.textColor = new Color(1f, 1f, 1f, 0.95f * alpha);
            GUI.Label(new Rect(guiX - 100f, barY - 17f, 200f, 16f), enemyName, enemyNameStyle);

            // HP numbers below the floating bar
            enemyBarTextStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f, 0.85f * alpha);
            GUI.Label(new Rect(barX - 20f, barY + barH + 1f, barW + 40f, 13f), $"{enemyHp} / {enemyMaxHp}", enemyBarTextStyle);
        }
    }

    private Camera GetActivePlayerCamera()
    {
        PlayerController pc = FindLocalPlayerController();
        if (pc != null)
        {
            Camera playerCam = pc.GetComponentInChildren<Camera>();
            if (playerCam != null && playerCam.isActiveAndEnabled)
            {
                return playerCam;
            }
        }

        Camera main = Camera.main;
        if (main != null && main.isActiveAndEnabled)
        {
            return main;
        }

        return Camera.allCamerasCount > 0 ? Camera.allCameras[0] : null;
    }

    private void DrawSolidRect(Rect rect, Color color)
    {
        if (solidTexture == null)
        {
            solidTexture = new Texture2D(1, 1);
            solidTexture.SetPixel(0, 0, Color.white);
            solidTexture.Apply();
        }

        Color prevColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, solidTexture);
        GUI.color = prevColor;
    }

    private void OnDestroy()
    {
        if (solidTexture != null)
        {
            Destroy(solidTexture);
        }
    }

    private void DrawMoney()
    {
        int crewCredits = (GameManager.Instance != null && GameManager.Instance.IsSpawned)
            ? GameManager.Instance.GroupCredits.Value
            : 150;

        PlayerInventory localInventory = FindLocalPlayerInventory();
        int carriedScrapValue = localInventory != null ? localInventory.GetCarriedScrapValue() : 0;

        bool inLobby = GameManager.Instance != null &&
                       GameManager.Instance.CurrentState.Value == GameManager.GameState.Lobby;

        string moneyLabel = inLobby
            ? $"COMPANY FUNDS: ${crewCredits}"
            : (carriedScrapValue > 0 ? $"FUNDS: ${crewCredits}  |  CARRIED: +${carriedScrapValue}" : $"FUNDS: ${crewCredits}");

        Rect rect = new Rect(20f, Screen.height - 84f, 320f, 26f);
        GUI.Label(rect, moneyLabel, moneyStyle);
    }

    private void DrawQuotaHUD()
    {
        float boxX = 20f;
        float boxY = Screen.height - 128f;
        float boxW = 270f;
        float boxH = 38f;

        int progress = (GameManager.Instance != null && GameManager.Instance.IsSpawned)
            ? GameManager.Instance.QuotaProgress.Value
            : 0;
        int target = (GameManager.Instance != null && GameManager.Instance.IsSpawned && GameManager.Instance.QuotaTarget.Value > 0)
            ? GameManager.Instance.QuotaTarget.Value
            : 250;
        int points = (GameManager.Instance != null && GameManager.Instance.IsSpawned)
            ? GameManager.Instance.QuotaPoints.Value
            : 0;
        int days = (GameManager.Instance != null && GameManager.Instance.IsSpawned)
            ? GameManager.Instance.DaysRemaining.Value
            : 3;

        PlayerInventory localInventory = FindLocalPlayerInventory();
        int carriedScrap = localInventory != null ? localInventory.GetCarriedScrapValue() : 0;

        bool inLobby = GameManager.Instance != null &&
                       GameManager.Instance.CurrentState.Value == GameManager.GameState.Lobby;

        // Dark card background & subtle border
        DrawSolidRect(new Rect(boxX - 2f, boxY - 2f, boxW + 4f, boxH + 4f), new Color(0.04f, 0.06f, 0.10f, 0.88f));
        DrawSolidRect(new Rect(boxX, boxY, boxW, boxH), new Color(0.10f, 0.14f, 0.20f, 0.88f));

        GUIStyle quotaLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperLeft
        };

        GUIStyle pointsBadgeStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperRight
        };
        pointsBadgeStyle.normal.textColor = new Color(1f, 0.85f, 0.2f); // Golden Star

        int projectedTotal = inLobby ? progress : progress + carriedScrap;
        bool willMeetQuota = projectedTotal >= target;
        string daysTag = inLobby ? $"{days}D REMAIN" : $"{days}D";

        if (willMeetQuota && !inLobby)
        {
            quotaLabelStyle.normal.textColor = new Color(0.25f, 1f, 0.35f);
            GUI.Label(new Rect(boxX + 6f, boxY + 2f, 175f, 18f), $"QUOTA: ${progress} (+${carriedScrap}) / ${target}", quotaLabelStyle);
        }
        else
        {
            quotaLabelStyle.normal.textColor = new Color(0.75f, 0.88f, 1f);
            string extraText = (!inLobby && carriedScrap > 0) ? $" (+${carriedScrap})" : "";
            GUI.Label(new Rect(boxX + 6f, boxY + 2f, 175f, 18f), $"QUOTA: ${progress}{extraText} / ${target}", quotaLabelStyle);
        }

        GUI.Label(new Rect(boxX + boxW - 90f, boxY + 2f, 84f, 18f), $"★{points} | {daysTag}", pointsBadgeStyle);

        // Progress bar track & fill
        float barX = boxX + 6f;
        float barY = boxY + 22f;
        float barW = boxW - 12f;
        float barH = 8f;

        DrawSolidRect(new Rect(barX, barY, barW, barH), new Color(0.18f, 0.22f, 0.30f, 0.9f));

        float basePct = Mathf.Clamp01((float)progress / target);
        float projectedPct = Mathf.Clamp01((float)projectedTotal / target);

        // Projected progress fill (cyan/green)
        if (projectedPct > basePct)
        {
            Color projColor = willMeetQuota ? new Color(0.2f, 0.85f, 0.3f, 0.55f) : new Color(0.2f, 0.6f, 0.9f, 0.45f);
            DrawSolidRect(new Rect(barX, barY, barW * projectedPct, barH), projColor);
        }

        // Secured progress fill (gold)
        if (basePct > 0f)
        {
            Color fillColor = new Color(1f, 0.82f, 0.15f);
            DrawSolidRect(new Rect(barX, barY, barW * basePct, barH), fillColor);
        }
    }

    private void DrawQuotaCelebration()
    {
        if (quotaCelebrationTimer <= 0f)
        {
            return;
        }

        quotaCelebrationTimer -= Time.deltaTime;

        Color headerColor = isQuotaAlert ? new Color(1f, 0.25f, 0.25f) : new Color(1f, 0.88f, 0.15f);
        Color borderColor = isQuotaAlert ? new Color(1f, 0.3f, 0.3f, 0.95f) : new Color(1f, 0.85f, 0.1f, 0.95f);

        float bannerW = 640f;
        float bannerH = 75f;
        float bannerX = (Screen.width - bannerW) * 0.5f;
        float bannerY = 95f;

        // Dark banner backdrop with accent
        DrawSolidRect(new Rect(bannerX, bannerY, bannerW, bannerH), new Color(0.04f, 0.07f, 0.12f, 0.94f));
        DrawSolidRect(new Rect(bannerX, bannerY, bannerW, 3f), borderColor);
        DrawSolidRect(new Rect(bannerX, bannerY + bannerH - 2f, bannerW, 2f), borderColor);

        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        titleStyle.normal.textColor = headerColor;
        GUI.Label(new Rect(bannerX, bannerY + 8f, bannerW, 30f), quotaBannerTitle, titleStyle);

        GUIStyle subStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        subStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(bannerX, bannerY + 40f, bannerW, 24f), quotaBannerSubtitle, subStyle);
    }

    private void DrawConnectionInfo()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            return;
        }

        string role = NetworkManager.Singleton.IsHost ? "Host" : "Client";
        int playerCount = (NetworkManager.Singleton.ConnectedClientsList != null) ? NetworkManager.Singleton.ConnectedClientsList.Count : 1;
        string relayTag = RelayManager.IsRelayActive ? $" | Code: {RelayManager.CurrentJoinCode}" : "";

        Rect rect = new Rect(Screen.width - 340f, Screen.height - 35f, 330f, 25f);
        GUI.Label(rect, $"{currentFps} FPS | {role} | Players: {playerCount}{relayTag}", infoStyle);
    }

    private void Update()
    {
        fpsFrameCount++;
        fpsTimer += Time.unscaledDeltaTime;
        if (fpsTimer >= 0.5f)
        {
            currentFps = Mathf.RoundToInt(fpsFrameCount / fpsTimer);
            fpsFrameCount = 0;
            fpsTimer = 0f;
        }

        if (GameManager.Instance == null)
        {
            return;
        }

        // In Lobby: Any player can press F to toggle their ready agreement
        if (GameManager.Instance.CurrentState.Value == GameManager.GameState.Lobby)
        {
            if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
            {
                PlayerController pc = FindLocalPlayerController();
                if (pc != null)
                {
                    pc.SetReadyServerRpc(!pc.IsReadyToDive.Value);
                }
            }
        }
    }

    private void DrawRoundState()
    {
        if (GameManager.Instance == null)
        {
            return;
        }

        GameManager.GameState state = GameManager.Instance.CurrentState.Value;

        if (state == GameManager.GameState.RoundOver)
        {
            if (!isRoundOverSummaryActive)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            bool anyoneEscaped = extractedThisRound.Count > 0;

            // Count connected active players
            int activePlayerCount = 0;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.ConnectedClientsList != null)
            {
                foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
                {
                    if (client != null && client.PlayerObject != null)
                    {
                        activePlayerCount++;
                    }
                }
            }

            // Dimensions for the scoreboard card
            float cardWidth = 720f;
            float rowHeight = 36f;
            float headerSpace = 36f;
            float cardHeight = Mathf.Max(90f, headerSpace + activePlayerCount * rowHeight + 14f);
            float cardX = (Screen.width - cardWidth) * 0.5f;
            float cardY = (Screen.height - cardHeight) * 0.5f - 10f;

            // Header
            stateStyle.normal.textColor = anyoneEscaped ? new Color(0.2f, 1f, 0.3f) : new Color(1f, 0.25f, 0.25f);
            Rect headerRect = new Rect(0f, cardY - 70f, Screen.width, 60f);
            GUI.Label(headerRect, anyoneEscaped ? "EXPEDITION COMPLETE!" : "TIME'S UP!", stateStyle);

            // Card background & borders
            DrawSolidRect(new Rect(cardX, cardY, cardWidth, cardHeight), new Color(0.04f, 0.06f, 0.10f, 0.92f));

            Color accentColor = anyoneEscaped ? new Color(0.2f, 1f, 0.3f, 0.9f) : new Color(1f, 0.25f, 0.25f, 0.9f);
            DrawSolidRect(new Rect(cardX, cardY, cardWidth, 3f), accentColor);
            DrawSolidRect(new Rect(cardX, cardY + cardHeight - 1f, cardWidth, 1f), new Color(0.25f, 0.35f, 0.5f, 0.5f));
            DrawSolidRect(new Rect(cardX, cardY, 1f, cardHeight), new Color(0.25f, 0.35f, 0.5f, 0.5f));
            DrawSolidRect(new Rect(cardX + cardWidth - 1f, cardY, 1f, cardHeight), new Color(0.25f, 0.35f, 0.5f, 0.5f));

            // Column Header labels
            GUIStyle colHeadStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold
            };
            colHeadStyle.normal.textColor = new Color(0.6f, 0.7f, 0.85f, 0.8f);

            colHeadStyle.alignment = TextAnchor.MiddleLeft;
            GUI.Label(new Rect(cardX + 24f, cardY + 8f, 220f, 22f), "CONTRACTOR", colHeadStyle);

            colHeadStyle.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(cardX + 244f, cardY + 8f, 240f, 22f), "EXPEDITION RESULT", colHeadStyle);

            colHeadStyle.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(cardX + 484f, cardY + 8f, 212f, 22f), "BANK SAVINGS", colHeadStyle);

            DrawSolidRect(new Rect(cardX + 16f, cardY + 32f, cardWidth - 32f, 1f), new Color(0.2f, 0.28f, 0.4f, 0.5f));

            // Row styles
            GUIStyle leftStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            GUIStyle centerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            GUIStyle rightStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight
            };

            // Per-player results
            float startY = cardY + headerSpace + 6f;
            int i = 0;
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.ConnectedClientsList != null)
            {
                foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
                {
                    if (client == null || client.PlayerObject == null)
                    {
                        continue;
                    }

                    ulong cid = client.ClientId;
                    bool escaped = extractedThisRound.ContainsKey(cid);
                    bool isLocal = cid == NetworkManager.Singleton.LocalClientId;

                    PlayerWallet wallet = client.PlayerObject.GetComponent<PlayerWallet>();
                    int bank = (GameManager.Instance != null && GameManager.Instance.IsSpawned) ? GameManager.Instance.GroupCredits.Value : (wallet != null ? wallet.BankedMoney.Value : 150);
                    int qProg = (GameManager.Instance != null && GameManager.Instance.IsSpawned) ? GameManager.Instance.QuotaProgress.Value : 0;
                    int qTarg = (GameManager.Instance != null && GameManager.Instance.IsSpawned && GameManager.Instance.QuotaTarget.Value > 0) ? GameManager.Instance.QuotaTarget.Value : 250;
                    int points = (GameManager.Instance != null && GameManager.Instance.IsSpawned) ? GameManager.Instance.QuotaPoints.Value : 0;

                    float rowY = startY + i * rowHeight;

                    // Highlight row for local player
                    if (isLocal)
                    {
                        DrawSolidRect(new Rect(cardX + 8f, rowY, cardWidth - 16f, rowHeight - 4f), new Color(0.2f, 0.5f, 1f, 0.12f));
                    }
                    else if (i % 2 == 1)
                    {
                        DrawSolidRect(new Rect(cardX + 8f, rowY, cardWidth - 16f, rowHeight - 4f), new Color(1f, 1f, 1f, 0.03f));
                    }

                    string playerName = isLocal ? $"Player {cid + 1} (You) [★{points}]" : $"Player {cid + 1} [★{points}]";
                    leftStyle.normal.textColor = isLocal ? new Color(0.4f, 0.85f, 1f) : Color.white;
                    GUI.Label(new Rect(cardX + 24f, rowY, 220f, rowHeight - 4f), playerName, leftStyle);

                    if (escaped)
                    {
                        int bankedThisRun = extractedThisRound[cid].bankedAmount;
                        centerStyle.normal.textColor = new Color(0.2f, 1f, 0.3f);
                        GUI.Label(new Rect(cardX + 244f, rowY, 240f, rowHeight - 4f), $"ESCAPED (+${bankedThisRun})", centerStyle);
                    }
                    else
                    {
                        centerStyle.normal.textColor = new Color(1f, 0.3f, 0.3f);
                        GUI.Label(new Rect(cardX + 244f, rowY, 240f, rowHeight - 4f), "CAPTURED ($0)", centerStyle);
                    }

                    rightStyle.normal.textColor = new Color(1f, 0.85f, 0.2f);
                    GUI.Label(new Rect(cardX + 484f, rowY, 212f, rowHeight - 4f), $"CREW: ${bank} (Q: {qProg}/{qTarg})", rightStyle);

                    i++;
                }
            }

            // Automatic countdown to respawn squad in bunker
            float returnSecs = Mathf.Max(0f, GameManager.Instance.AutoReturnTimer.Value);
            string autoReturnMsg = $"RETURNING TO BUNKER IN {returnSecs:F1}s...";
            GUIStyle returnStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            returnStyle.normal.textColor = new Color(1f, 0.85f, 0.2f);
            GUI.Label(new Rect(0f, cardY + cardHeight + 18f, Screen.width, 35f), autoReturnMsg, returnStyle);
        }
        else
        {
            isRoundOverSummaryActive = false;
        }
    }

    /// <summary>
    /// Shows a small banner in the center when the local player has extracted.
    /// </summary>
    private void DrawExtractionBanner()
    {
        if (!IsLocalPlayerExtracted())
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

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
        {
            return;
        }

        ulong localId = NetworkManager.Singleton.LocalClientId;
        int bankedAmount = extractedThisRound.ContainsKey(localId) ? extractedThisRound[localId].bankedAmount : 0;

        PlayerWallet wallet = FindLocalPlayerWallet();
        int totalBank = wallet != null ? wallet.BankedMoney.Value : 0;

        Rect rect = new Rect(0f, Screen.height / 2f - 50f, Screen.width, 60f);
        GUI.Label(rect, $"ESCAPED! SAVINGS SECURED  \ud83d\udcb0  +${bankedAmount}", extractedBannerStyle);

        GUIStyle subStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            alignment = TextAnchor.MiddleCenter
        };
        subStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(0f, Screen.height / 2f + 15f, Screen.width, 30f),
            $"Total Savings: ${totalBank}  |  STATUS: SAFE (Awaiting squad...)", subStyle);
    }

    /// <summary>
    /// Shows a compact scoreboard in the top-right during a live round.
    /// </summary>
    private void DrawScoreboard()
    {
        if (GameManager.Instance == null)
        {
            return;
        }

        if (GameManager.Instance.CurrentState.Value != GameManager.GameState.Playing)
        {
            return;
        }

        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening || NetworkManager.Singleton.ConnectedClientsList == null)
        {
            return;
        }

        float x = Screen.width - 200f;
        float y = 15f;
        int i = 0;

        GUIStyle sbStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            alignment = TextAnchor.UpperRight,
            fontStyle = FontStyle.Bold
        };

        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client == null || client.PlayerObject == null)
            {
                continue;
            }

            PlayerInventory inv = client.PlayerObject.GetComponent<PlayerInventory>();
            int carriedScrap = inv != null ? inv.GetCarriedScrapValue() : 0;
            string valueDisplay = carriedScrap > 0 ? $"+${carriedScrap} scrap" : "$0";

            bool escaped = extractedThisRound.ContainsKey(client.ClientId);
            string escapedTag = escaped ? " ✓" : "";
            bool isLocal = client.ClientId == NetworkManager.Singleton.LocalClientId;

            sbStyle.normal.textColor = escaped
                ? new Color(0.2f, 1f, 0.2f)
                : (isLocal ? new Color(1f, 0.85f, 0f) : Color.white);

            GUI.Label(new Rect(x - 60f, y + i * 24f, 250f, 22f),
                $"P{client.ClientId + 1}: {valueDisplay}{escapedTag}", sbStyle);
            i++;
        }
    }

    private bool IsLocalPlayerExtracted()
    {
        if (NetworkManager.Singleton == null)
        {
            return false;
        }

        return extractedThisRound.ContainsKey(NetworkManager.Singleton.LocalClientId);
    }

    private PlayerController FindLocalPlayerController()
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

        return playerObj.GetComponent<PlayerController>();
    }

    private PlayerHealth FindLocalPlayerHealth()
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

        return playerObj.GetComponent<PlayerHealth>();
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

    private PlayerAttack FindLocalPlayerAttack()
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

        return playerObj.GetComponent<PlayerAttack>();
    }

    private PlayerRangedCombat FindLocalPlayerRangedCombat()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
        {
            NetworkObject playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (playerObj != null)
            {
                return playerObj.GetComponent<PlayerRangedCombat>();
            }
        }

        PlayerController[] pcs = FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude);
        for (int i = 0; i < pcs.Length; i++)
        {
            if (pcs[i] != null && pcs[i].IsOwner)
            {
                return pcs[i].GetComponent<PlayerRangedCombat>();
            }
        }

        if (pcs.Length > 0 && pcs[0] != null)
        {
            return pcs[0].GetComponent<PlayerRangedCombat>();
        }

        return null;
    }
}
