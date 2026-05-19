using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityRuntimePlugins
{
    /// <summary>
    /// A utility component to test plugin UI prefabs at runtime.
    /// Attach to a Canvas or child of a Canvas.
    /// </summary>
    public class UITester : MonoBehaviour
    {
        [SerializeField] private RectTransform container;
        
        private List<PluginModuleInfo> _modules = new List<PluginModuleInfo>();
        private List<string> _prefabs = new List<string>();
        
        private PluginModuleInfo _selectedModule;
        private string _selectedPrefab;
        private GameObject _currentInstance;

        private void Start()
        {
            if (container == null) container = GetComponent<RectTransform>();
            RefreshModules();
        }

        public async void RefreshModules()
        {
            await PluginManager.Instance.Initialize();
            _modules = PluginManager.Instance.GetLoadedModules().ToList();
            _selectedModule = null;
            _prefabs.Clear();
            Debug.Log($"[UITester] Refreshed modules. Count: {_modules.Count}");
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 250, Screen.height - 20), GUI.skin.box);
            GUILayout.Label("UI Tester", GUI.skin.label);
            
            if (GUILayout.Button("Refresh Plugins")) RefreshModules();

            GUILayout.Space(10);
            GUILayout.Label("Step 1: Select Plugin", GUI.skin.label);
            
            foreach (var module in _modules)
            {
                bool isSelected = _selectedModule == module;
                if (GUILayout.Toggle(isSelected, module.name, "Button"))
                {
                    if (!isSelected)
                    {
                        _selectedModule = module;
                        _ = LoadPrefabsForModule(module.name);
                    }
                }
            }

            if (_selectedModule != null)
            {
                GUILayout.Space(10);
                GUILayout.Label($"Step 2: Select Prefab ({_selectedModule.name})", GUI.skin.label);

                if (_prefabs.Count == 0)
                {
                    GUILayout.Label("No prefabs found.");
                }

                foreach (var prefabName in _prefabs)
                {
                    bool isSelected = _selectedPrefab == prefabName;
                    if (GUILayout.Toggle(isSelected, prefabName, "Button"))
                    {
                        if (!isSelected)
                        {
                            _selectedPrefab = prefabName;
                            _ = SpawnPrefab(_selectedModule.name, prefabName);
                        }
                    }
                }
            }

            GUILayout.EndArea();
        }

        private async Task LoadPrefabsForModule(string moduleName)
        {
            _selectedPrefab = null;
            var assets = await PluginManager.Instance.GetAssetsByType<GameObject>(moduleName);
            _prefabs = assets.ToList();
            Debug.Log($"[UITester] Found {_prefabs.Count} prefabs in {moduleName}");
        }

        private async Task SpawnPrefab(string moduleName, string prefabName)
        {
            if (_currentInstance != null)
            {
                Destroy(_currentInstance);
            }

            GameObject prefab = await PluginManager.Instance.LoadAsset<GameObject>(moduleName, prefabName);
            if (prefab != null)
            {
                _currentInstance = Instantiate(prefab, container != null ? container : transform);
                Debug.Log($"[UITester] Spawned {prefabName} from {moduleName}");
            }
            else
            {
                Debug.LogError($"[UITester] Failed to load prefab {prefabName} from {moduleName}");
            }
        }
    }
}
