using System;
using UnityEditor;
using UnityEngine;

namespace UnityRuntimePlugins.Editor
{
    /// <summary>
    /// Connects Editor-only services (like AssetDatabase) to the Runtime PluginManager.
    /// </summary>
    [InitializeOnLoad]
    public static class PluginEditorIntegration
    {
        static PluginEditorIntegration()
        {
            PluginManager.EditorAssetLoader = LoadAssetViaAssetDatabase;
        }

        private static UnityEngine.Object LoadAssetViaAssetDatabase(string moduleName, string assetPath, Type type)
        {
            string[] results = AssetDatabase.FindAssets($"{assetPath} t:{type.Name}");
            foreach (var guid in results)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains(moduleName))
                {
                    var asset = AssetDatabase.LoadAssetAtPath(path, type);
                    if (asset != null)
                    {
                        Debug.LogWarning($"[PluginManager] Asset '{assetPath}' loaded via Editor fallback. " +
                                         "It is NOT available via Addressables! To fix this: \n" +
                                         "1. Mark the prefab as 'Addressable' in the Unity Inspector.\n" +
                                         $"2. Set its Addressable Key to exactly '{assetPath}'.\n" +
                                         "3. Ensure the module's Addressables catalog is built and included in the zip.");
                        return asset;
                    }
                }
            }
            return null;
        }
    }
}
