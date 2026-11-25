#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor utility: ensures PlayerPrefab has PlayerWallet attached,
/// and cleans up any legacy coin entities from active scenes.
/// </summary>
public static class PlayerWalletSetupHelper
{
    private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Entities/PlayerPrefab.prefab";

    [InitializeOnLoadMethod]
    private static void AutoEnsureSetup()
    {
        EditorApplication.delayCall += () =>
        {
            EnsurePlayerWallet();
            CleanupSceneCoins();
            EnsurePlayerCharacterModel();
        };
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
                    Debug.Log("[PlayerWalletSetupHelper] Added PlayerWallet to PlayerPrefab");
                }
            }
        }
    }

    [MenuItem("Tools/Dungeon Crawler/Clean Scene Coins")]
    public static void CleanupSceneCoins()
    {
        GameObject[] allObjects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include);
        int removed = 0;
        for (int i = 0; i < allObjects.Length; i++)
        {
            GameObject go = allObjects[i];
            if (go != null && (go.name.StartsWith("Coin") || go.name.Contains("CoinPrefab")))
            {
                Undo.DestroyObjectImmediate(go);
                removed++;
            }
        }

        if (removed > 0)
        {
            Debug.Log($"[PlayerWalletSetupHelper] Cleaned up {removed} legacy coin objects from scene.");
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
        }
    }

    public static void EnsurePlayerCharacterModel()
    {
        string fbxPath = "Assets/_Project/Art/Models/Characters/MainCharacter_rig.fbx";
        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (fbx == null)
        {
            return;
        }

        GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (playerPrefab == null)
        {
            return;
        }

        Transform body = playerPrefab.transform.Find("Body");
        if (body != null && (body.GetComponent<MeshFilter>() != null || body.childCount == 0))
        {
            ModelImporterWizard.AttachCharacterToPlayer(fbx);
        }
    }
}
#endif
