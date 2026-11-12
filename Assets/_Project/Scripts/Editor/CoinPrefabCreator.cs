using Unity.Netcode;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// Editor utility: ensures CoinPrefab exists, is registered in DefaultNetworkPrefabs,
/// and that PlayerPrefab has PlayerWallet attached.
/// Runs automatically on editor load or via Tools > Dungeon Crawler > Create Coin Prefab.
/// </summary>
public static class CoinPrefabCreator
{
    private const string CoinPrefabPath = "Assets/_Project/Prefabs/Items/CoinPrefab.prefab";
    private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Entities/PlayerPrefab.prefab";

    [InitializeOnLoadMethod]
    private static void AutoEnsureSetup()
    {
        EditorApplication.delayCall += () =>
        {
            EnsureCoinPrefab();
            EnsurePlayerWallet();
        };
    }

    [MenuItem("Tools/Dungeon Crawler/Create Coin Prefab")]
    public static void CreateCoinPrefab()
    {
        EnsureCoinPrefab(true);
        EnsurePlayerWallet();
    }

    public static void EnsureCoinPrefab(bool forceRecreate = false)
    {
        if (!forceRecreate && AssetDatabase.LoadAssetAtPath<GameObject>(CoinPrefabPath) != null)
        {
            return;
        }

        string dir = "Assets/_Project/Prefabs/Items";
        if (!AssetDatabase.IsValidFolder("Assets/_Project/Prefabs"))
        {
            AssetDatabase.CreateFolder("Assets/_Project", "Prefabs");
        }
        if (!AssetDatabase.IsValidFolder(dir))
        {
            AssetDatabase.CreateFolder("Assets/_Project/Prefabs", "Items");
        }

        GameObject coin = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        coin.name = "CoinPrefab";
        coin.transform.localScale = Vector3.one * 0.4f;

        Renderer rend = coin.GetComponent<Renderer>();
        if (rend != null)
        {
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = new Color(1f, 0.82f, 0.1f);
            rend.sharedMaterial = mat;
        }

        Object.DestroyImmediate(coin.GetComponent<SphereCollider>());
        coin.AddComponent<NetworkObject>();
        coin.AddComponent<Coin>();

        bool saved;
        PrefabUtility.SaveAsPrefabAsset(coin, CoinPrefabPath, out saved);
        Object.DestroyImmediate(coin);

        if (saved)
        {
            Debug.Log($"[CoinPrefabCreator] Created CoinPrefab at {CoinPrefabPath}");
            AssetDatabase.Refresh();
        }
    }

    public static void EnsurePlayerWallet()
    {
        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (playerPrefab == null)
        {
            return;
        }

        if (playerPrefab.GetComponent<PlayerWallet>() == null)
        {
            using (var editScope = new PrefabUtility.EditPrefabContentsScope(PlayerPrefabPath))
            {
                if (editScope.prefabContentsRoot.GetComponent<PlayerWallet>() == null)
                {
                    editScope.prefabContentsRoot.AddComponent<PlayerWallet>();
                    Debug.Log("[CoinPrefabCreator] Added PlayerWallet to PlayerPrefab");
                }
            }
        }
    }
}
#endif
