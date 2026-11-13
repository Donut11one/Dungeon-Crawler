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

    // Tracks which players escaped and their banked money (populated via RPC from GameManager)
    private readonly Dictionary<ulong, ExtractedRecord> extractedThisRound = new();

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
            fontSize = 22,
            alignment = TextAnchor.LowerLeft,
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
    /// Called across network via GameManager ClientRpc when round starts, restarts, or returns to lobby.
    /// Wipes all stale round-over states, extraction statuses, and locks cursor for gameplay.
    /// </summary>
    public void ClearRoundState()
    {
        extractedThisRound.Clear();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
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

        if (NetworkManager.Singleton == null)
        {
            return;
        }
        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            return;
        }

        DrawCrosshair();
        DrawEnemyHealthBars();
        DrawLobbyHUD();
        DrawTimer();
        DrawHealth();
        DrawMoney();
        DrawHotbar();
        DrawControlsHint();
        DrawConnectionInfo();
        DrawRoundState();
        DrawExtractionBanner();
        DrawScoreboard();
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
        else if (NetworkManager.Singleton != null)
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

            GUI.backgroundColor = originalBg;
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

        // Draw active item name centered above the hotbar (Minecraft style!)
        ItemData activeItem = inventory.GetSelectedItem();
        string activeName = activeItem != null ? activeItem.DisplayName.ToUpper() : "FISTS";
        Color activeColor = activeItem != null ? activeItem.SlotColor : new Color(0.9f, 0.9f, 0.9f);

        GUIStyle activeItemStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        activeItemStyle.normal.textColor = activeColor;
        GUI.Label(new Rect(startX - 50f, startY - 20f, totalWidth + 100f, 18f), $"[ {activeName} ]", activeItemStyle);

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
            fontSize = 14,
            alignment = TextAnchor.LowerCenter
        };
        hintStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f, 0.7f);

        Rect rect = new Rect(0f, Screen.height - 35f, Screen.width, 30f);

        if (GameManager.Instance != null && GameManager.Instance.CurrentState.Value == GameManager.GameState.Lobby)
        {
            GUI.Label(rect, "[WASD] Move   |   [Space] Jump   |   [1-5] Hotbar   |   [Left Click] Attack / Fist   |   [E] Pick Up/Drop   |   [F] Agree to Dive", hintStyle);
        }
        else
        {
            GUI.Label(rect, "[WASD] Move   |   [Space] Jump   |   [1-5] Hotbar   |   [Left Click] Attack / Fist   |   [E] Pick Up/Drop   |   [Right Click] Throw", hintStyle);
        }
    }

    private void DrawCrosshair()
    {
        Rect rect = new Rect(Screen.width / 2f - 15f, Screen.height / 2f - 15f, 30f, 30f);
        GUI.Label(rect, "+", crosshairStyle);
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
        float barY = Screen.height - 65f;
        float barW = 230f;
        float barH = 22f;

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
        PlayerWallet localWallet = FindLocalPlayerWallet();
        if (localWallet == null)
        {
            return;
        }

        bool inLobby = GameManager.Instance != null &&
                       GameManager.Instance.CurrentState.Value == GameManager.GameState.Lobby;

        // In lobby: show total accumulated money; in run: show money collected this run (starts at 0)
        int displayAmount = inLobby ? localWallet.BankedMoney.Value : localWallet.RunMoney.Value;

        Rect rect = new Rect(20f, Screen.height - 95f, 300f, 40f);
        GUI.Label(rect, $"MONEY: ${displayAmount}", moneyStyle);
    }

    private void DrawConnectionInfo()
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        string role = NetworkManager.Singleton.IsHost ? "Host" : "Client";
        int playerCount = NetworkManager.Singleton.ConnectedClientsList.Count;

        Rect rect = new Rect(Screen.width - 250f, Screen.height - 35f, 240f, 25f);
        GUI.Label(rect, $"{role} | Players: {playerCount}", infoStyle);
    }

    private void Update()
    {
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

        // When round is over: Press L or Space to return squad to lobby
        if (GameManager.Instance.CurrentState.Value == GameManager.GameState.RoundOver)
        {
            if (Keyboard.current != null)
            {
                if (Keyboard.current.lKey.wasPressedThisFrame ||
                    Keyboard.current.rKey.wasPressedThisFrame ||
                    Keyboard.current.spaceKey.wasPressedThisFrame)
                {
                    GameManager.Instance.ReturnToLobbyServerRpc();
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
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            bool anyoneEscaped = extractedThisRound.Count > 0;

            // Header
            stateStyle.normal.textColor = anyoneEscaped ? new Color(0.2f, 1f, 0.2f) : Color.red;
            Rect headerRect = new Rect(0f, Screen.height / 2f - 115f, Screen.width, 70f);
            GUI.Label(headerRect, anyoneEscaped ? "EXPEDITION COMPLETE!" : "TIME'S UP!", stateStyle);

            // Per-player results
            float startY = Screen.height / 2f - 35f;
            int i = 0;
            if (NetworkManager.Singleton != null)
            {
                foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
                {
                    if (client.PlayerObject == null)
                    {
                        continue;
                    }

                    ulong cid = client.ClientId;
                    bool escaped = extractedThisRound.ContainsKey(cid);

                    PlayerWallet wallet = client.PlayerObject.GetComponent<PlayerWallet>();
                    int bank = wallet != null ? wallet.BankedMoney.Value : 0;

                    string label = cid == NetworkManager.Singleton.LocalClientId ? " (You)" : "";
                    string status = escaped ? "ESCAPED" : "CAPTURED";
                    Color statusColor = escaped ? new Color(0.2f, 1f, 0.2f) : Color.red;

                    scoreBoardStyle.normal.textColor = statusColor;
                    Rect row = new Rect(Screen.width / 2f - 210f, startY + i * 32f, 420f, 28f);

                    if (escaped)
                    {
                        int bankedThisRun = extractedThisRound[cid].bankedAmount;
                        GUI.Label(row, $"Player {cid + 1}{label}: {status} (+${bankedThisRun})  |  Total: ${bank}", scoreBoardStyle);
                    }
                    else
                    {
                        GUI.Label(row, $"Player {cid + 1}{label}: {status} ($0)  |  Total: ${bank}", scoreBoardStyle);
                    }
                    i++;
                }
            }

            // Return to Bunker button (returns all contractors to lobby to ready up together)
            if (GUI.Button(new Rect(Screen.width / 2f - 160f, Screen.height / 2f + 95f, 320f, 45f), "RETURN TO BUNKER (SPACE / L)"))
            {
                GameManager.Instance.ReturnToLobbyServerRpc();
            }
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

        ulong localId = NetworkManager.Singleton.LocalClientId;
        int bankedAmount = extractedThisRound.ContainsKey(localId) ? extractedThisRound[localId].bankedAmount : 0;

        PlayerWallet wallet = FindLocalPlayerWallet();
        int totalBank = wallet != null ? wallet.BankedMoney.Value : 0;

        Rect rect = new Rect(0f, Screen.height / 2f - 50f, Screen.width, 60f);
        GUI.Label(rect, $"ESCAPED TO LOBBY!  \ud83d\udcb0  +${bankedAmount}", extractedBannerStyle);

        GUIStyle subStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            alignment = TextAnchor.MiddleCenter
        };
        subStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(0f, Screen.height / 2f + 15f, Screen.width, 30f),
            $"Total Money: ${totalBank}  |  Waiting in Bunker for squad...", subStyle);
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

        if (NetworkManager.Singleton == null)
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
            if (client.PlayerObject == null)
            {
                continue;
            }

            PlayerWallet wallet = client.PlayerObject.GetComponent<PlayerWallet>();
            int runMoney = wallet != null ? wallet.RunMoney.Value : 0;

            bool escaped = extractedThisRound.ContainsKey(client.ClientId);
            string escapedTag = escaped ? " ✓" : "";
            bool isLocal = client.ClientId == NetworkManager.Singleton.LocalClientId;

            sbStyle.normal.textColor = escaped
                ? new Color(0.2f, 1f, 0.2f)
                : (isLocal ? new Color(1f, 0.85f, 0f) : Color.white);

            GUI.Label(new Rect(x, y + i * 24f, 180f, 22f),
                $"P{client.ClientId + 1}: ${runMoney}{escapedTag}", sbStyle);
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
}
