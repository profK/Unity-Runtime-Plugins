using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;

namespace UnityRuntimePlugins
{
    public class PluginUISelector : MonoBehaviour
    {
        [SerializeField] private RectTransform container;
        [SerializeField] private GameObject buttonPrefab; // Optional: If null, will use IMGUI for demo

        private List<string> _availablePrefabs = new List<string>();
        private GameObject _currentInstance;

        private async void Start()
        {
            await RefreshList();
        }

        public async System.Threading.Tasks.Task RefreshList()
        {
            _availablePrefabs.Clear();
            var modules = PluginManager.Instance.GetLoadedModules();
            
            foreach (var module in modules)
            {
                Debug.Log($"[PluginUISelector] Processing module {module.name}");
                var assets = await PluginManager.Instance.GetAssetsByType<GameObject>(module.name);
                foreach (var asset in assets)
                {
                    _availablePrefabs.Add($"{module.name}:{asset}");
                }
            }
            
            Debug.Log($"[PluginUISelector] Found {_availablePrefabs.Count} UI prefabs across {modules.Count()} modules.");
        }

        private void OnGUI()
        {
            // Simple IMGUI overlay if no container is provided, otherwise we'd use uGUI buttons
            if (container == null)
            {
                GUILayout.BeginArea(new Rect(10, 10, 200, Screen.height - 20));
                GUILayout.Label("Plugin UI Prefabs", GUI.skin.box);
                
                if (GUILayout.Button("Refresh")) _ = RefreshList();

                foreach (var prefabKey in _availablePrefabs)
                {
                    if (GUILayout.Button(prefabKey))
                    {
                        _ = LoadAndDisplay(prefabKey);
                    }
                }
                GUILayout.EndArea();
            }
        }

        private async System.Threading.Tasks.Task LoadAndDisplay(string prefabKey)
        {
            var parts = prefabKey.Split(':');
            if (parts.Length != 2) return;

            string moduleName = parts[0];
            string assetPath = parts[1];

            // Destroy previous instance
            if (_currentInstance != null)
            {
                Destroy(_currentInstance);
            }

            GameObject prefab = await PluginManager.Instance.LoadAsset<GameObject>(moduleName, assetPath);
            Debug.Log($"[PluginUISelector] Loaded prefab {prefabKey}");
            if (prefab != null)
            {
                // Check if it has the required Unity UIBehaviour
                if (prefab.GetComponent<UnityEngine.UI.Graphic>() != null)
                {
                    _currentInstance = Instantiate(prefab, container != null ? container : transform);
                    Debug.Log($"[PluginUISelector] Displayed {prefabKey}");
                }
                else
                {
                    Debug.LogWarning($"[PluginUISelector] Prefab {prefabKey} does not inherit from UIBehaviour.");
                }
            }
        }
    }
}
