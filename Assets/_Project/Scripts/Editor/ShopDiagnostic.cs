#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ShopDiagnostic
{
    static ShopDiagnostic()
    {
        RunDiagnostics();
    }

    [MenuItem("Tools/Dungeon Crawler/Run Shop Diagnostics")]
    public static void RunDiagnostics()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== SHOP & ITEM REGISTRY DIAGNOSTICS ===");

        // 1. TextAssets in Resources
        TextAsset asset = Resources.Load<TextAsset>("Items");
        TextAsset scrapAsset = Resources.Load<TextAsset>("Scrap");
        sb.AppendLine($"\n--- RESOURCES LOAD ---");
        sb.AppendLine($"Resources.Load<TextAsset>(\"Items\"): {(asset != null ? $"Found ({asset.text.Length} chars)" : "NULL!")}");
        sb.AppendLine($"Resources.Load<TextAsset>(\"Scrap\"): {(scrapAsset != null ? $"Found ({scrapAsset.text.Length} chars)" : "NULL!")}");

        string directPath = Path.Combine(Application.dataPath, "_Project/Resources/Items.json");
        string scrapPath = Path.Combine(Application.dataPath, "_Project/Resources/Scrap.json");
        sb.AppendLine($"Direct file at {directPath}: {File.Exists(directPath)}");
        sb.AppendLine($"Direct file at {scrapPath}: {File.Exists(scrapPath)}");

        // 2. Initialize ItemRegistry
        sb.AppendLine($"\n--- ITEM REGISTRY ---");
        try
        {
            ItemRegistry.Reload();
            List<ItemData> allItems = ItemRegistry.GetAllItems();
            sb.AppendLine($"ItemRegistry.GetAllItems() returned {allItems.Count} items:");
            int shopCount = 0;
            for (int i = 0; i < allItems.Count; i++)
            {
                ItemData it = allItems[i];
                if (it != null)
                {
                    bool isShop = it.Type != ItemType.Scrap;
                    if (isShop)
                    {
                        shopCount++;
                    }
                    sb.AppendLine($"  [{i}] ID='{it.ItemId}', Name='{it.DisplayName}', Type={it.Type}, Price={it.BuyPrice}, Dmg={it.Damage}, IsShopItem={isShop}");
                }
                else
                {
                    sb.AppendLine($"  [{i}] NULL ItemData");
                }
            }
            sb.AppendLine($"Total Shop Items (non-scrap): {shopCount}");
        }
        catch (System.Exception ex)
        {
            sb.AppendLine($"EXCEPTION in ItemRegistry: {ex}");
        }

        // 3. Check ArmoryShopUI in open scene or prefabs
        sb.AppendLine($"\n--- SCENE OBJECTS ---");
        ArmoryShopUI[] shopUIs = Object.FindObjectsByType<ArmoryShopUI>(FindObjectsInactive.Include);
        sb.AppendLine($"Found {shopUIs.Length} ArmoryShopUI instances in open scene.");
        for (int i = 0; i < shopUIs.Length; i++)
        {
            sb.AppendLine($"  - GameObject: '{shopUIs[i].gameObject.name}', Active={shopUIs[i].gameObject.activeInHierarchy}, IsOpen={shopUIs[i].IsOpen}");
        }

        ShopNPC[] npcs = Object.FindObjectsByType<ShopNPC>(FindObjectsInactive.Include);
        sb.AppendLine($"Found {npcs.Length} ShopNPC instances in open scene.");
        for (int i = 0; i < npcs.Length; i++)
        {
            sb.AppendLine($"  - GameObject: '{npcs[i].gameObject.name}', Active={npcs[i].gameObject.activeInHierarchy}, Pos={npcs[i].transform.position}");
        }

        string outPath = Path.Combine(Application.dataPath, "../Library/ShopDiagnosticOutput.txt");
        File.WriteAllText(outPath, sb.ToString());
        Debug.Log(sb.ToString());
    }
}
#endif
