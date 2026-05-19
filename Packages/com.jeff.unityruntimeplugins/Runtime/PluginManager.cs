using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace UnityRuntimePlugins
{
    public class PluginManager : IPluginServices
    {
        private static PluginManager _instance;
        public static PluginManager Instance => _instance ??= new PluginManager();

        private Dictionary<string, PluginModuleInfo> _loadedModules = new Dictionary<string, PluginModuleInfo>();
        private string _extractionRoot;

        private PluginManager()
        {
            _extractionRoot = Path.Combine(Application.persistentDataPath, "ExtractedPlugins");
            if (!Directory.Exists(_extractionRoot))
            {
                Directory.CreateDirectory(_extractionRoot);
            }
        }

        public async Task Initialize(string pluginsSearchPath = null)
        {
            if (string.IsNullOrEmpty(pluginsSearchPath))
            {
                pluginsSearchPath = Path.Combine(Application.dataPath, "..", "Plugins");
            }

            if (!Directory.Exists(pluginsSearchPath))
            {
                Debug.LogWarning($"[PluginManager] Search path does not exist: {pluginsSearchPath}");
                return;
            }

            var zipFiles = Directory.GetFiles(pluginsSearchPath, "*.zip", SearchOption.AllDirectories);
            foreach (var zipPath in zipFiles)
            {
                await LoadPluginFromZip(zipPath);
            }
        }

        private async Task LoadPluginFromZip(string zipPath)
        {
            string pluginName = Path.GetFileNameWithoutExtension(zipPath);
            string extractionPath = Path.Combine(_extractionRoot, pluginName);

            try
            {
                // Clean up old extraction if exists
                if (Directory.Exists(extractionPath))
                {
                    Directory.Delete(extractionPath, true);
                }

                ZipFile.ExtractToDirectory(zipPath, extractionPath);
                Debug.Log($"[PluginManager] Extracted {pluginName} to {extractionPath}");

                // Restore hidden .dll files that were renamed to .bytes to bypass Unity's compiler
                string[] bytesFiles = Directory.GetFiles(extractionPath, "*.bytes", SearchOption.AllDirectories);
                foreach (string file in bytesFiles)
                {
                    string newFile = file.Substring(0, file.Length - 6) + ".dll";
                    File.Move(file, newFile);
                }

                // Scan for modules (sub-directories with manifest.json)
                var manifestFiles = Directory.GetFiles(extractionPath, "manifest.json", SearchOption.AllDirectories);
                foreach (var manifestPath in manifestFiles)
                {
                    await LoadModule(manifestPath);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[PluginManager] Failed to load plugin from {zipPath}: {e.Message}");
            }
        }

        private async Task LoadModule(string manifestPath)
        {
            string json = File.ReadAllText(manifestPath);
            PluginManifest manifest = PluginManifest.FromJson(json);
            string moduleRoot = Path.GetDirectoryName(manifestPath);

            if (_loadedModules.ContainsKey(manifest.name))
            {
                Debug.LogWarning($"[PluginManager] Module {manifest.name} is already loaded. Skipping.");
                return;
            }

            PluginModuleInfo info = new PluginModuleInfo
            {
                name = manifest.name,
                version = manifest.version,
                rootPath = moduleRoot,
                manifest = manifest
            };

            // Environment selection
            bool isServer = Application.isBatchMode;
            
            // 1. Common
            await RegisterFolder(Path.Combine(moduleRoot, "common"), info);
            
            // 2. Specific
            if (isServer)
            {
                await RegisterFolder(Path.Combine(moduleRoot, "server"), info);
                info.isServerLoaded = true;
            }
            else
            {
                await RegisterFolder(Path.Combine(moduleRoot, "client"), info);
                info.isClientLoaded = true;
            }

            _loadedModules.Add(manifest.name, info);
            Debug.Log($"[PluginManager] Module {manifest.name} v{manifest.version} loaded.");
        }

        private async Task RegisterFolder(string path, PluginModuleInfo info)
        {
            await Task.Yield();
            if (!Directory.Exists(path)) return;

            // Addressables integration placeholder
            string addressablesPath = Path.Combine(path, "addressables");
            if (Directory.Exists(addressablesPath))
            {
                // In a real implementation, we would load the content catalog here
                // Addressables.LoadContentCatalogAsync(...)
                Debug.Log($"[PluginManager] Found addressables for {info.name} at {addressablesPath}");
            }

            // WASM registration placeholder
            string streamingAssetsPath = Path.Combine(path, "streaming_assets");
            if (!Directory.Exists(streamingAssetsPath)) streamingAssetsPath = Path.Combine(path, "streaming_assets~");

            if (Directory.Exists(streamingAssetsPath))
            {
                 Debug.Log($"[PluginManager] Found streaming assets for {info.name} at {streamingAssetsPath}");
            }
        }

        #region IPluginServices Implementation

        public IEnumerable<PluginModuleInfo> GetLoadedModules() => _loadedModules.Values;

        /// <summary>
        /// A delegate used in the Editor to provide a fallback for loading assets that aren't yet in an Addressables catalog.
        /// </summary>
        public static Func<string, string, Type, UnityEngine.Object> EditorAssetLoader;

        public async Task<T> LoadAsset<T>(string moduleName, string assetPath) where T : UnityEngine.Object
        {
            if (!_loadedModules.TryGetValue(moduleName, out var info))
            {
                Debug.LogError($"[PluginManager] Cannot load asset '{assetPath}': Module '{moduleName}' is not loaded.");
                return null;
            }

            try
            {
                // Pre-flight check: see if the Addressables key exists to avoid noisy console exceptions in the Editor
                var locationsHandle = Addressables.LoadResourceLocationsAsync(assetPath, typeof(T));
                await locationsHandle.Task;
                bool keyExists = locationsHandle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded && locationsHandle.Result != null && locationsHandle.Result.Count > 0;
                Addressables.Release(locationsHandle);

                if (keyExists)
                {
                    // Primary: Use Addressables
                    var handle = Addressables.LoadAssetAsync<T>(assetPath);
                    await handle.Task;
                    if (handle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
                    {
                        return handle.Result;
                    }
                    else
                    {
                        // If Addressables failed, release the handle and fall through to Editor fallback
                        Addressables.Release(handle);
                    }
                }
            }
            catch (Exception)
            {
                // Silence Addressables noise in Editor as we have a fallback
            }

            // Editor Fallback: Use the hook if provided by the Editor assembly
            if (Application.isEditor && EditorAssetLoader != null)
            {
                var asset = EditorAssetLoader(moduleName, assetPath, typeof(T)) as T;
                if (asset != null) return asset;
            }

            return null;
        }

        public void InvokeWasmFunction(string moduleName, string functionName, params object[] args)
        {
            var target = PluginIoCContainer.Instance.Resolve<ICallablePlugin>(moduleName);
            if (target != null)
            {
                target.InvokeFunction(functionName, args);
            }
            else
            {
                Debug.LogError($"[PluginManager] Cannot invoke function: Module '{moduleName}' is not registered in the IoC container.");
            }
        }

        public void SendCommand<TCommand>(string moduleName, TCommand command) where TCommand : class
        {
            if (command == null) return;
            string payload = JsonUtility.ToJson(command);
            string commandName = typeof(TCommand).Name;

            var target = PluginIoCContainer.Instance.Resolve<ICallablePlugin>(moduleName);
            if (target != null)
            {
                target.InvokeFunction(commandName, payload);
            }
            else
            {
                Debug.LogError($"[PluginManager] Cannot send command: Module '{moduleName}' is not registered in the IoC container.");
            }
        }

        public async Task<IEnumerable<string>> GetAssetsByType<T>(string moduleName) where T : UnityEngine.Object
        {
            if (!_loadedModules.TryGetValue(moduleName, out var info))
            {
                return Enumerable.Empty<string>();
            }

            // Real discovery: Scan the extracted folders for files that might match the type
            // Note: This is a fallback for when Addressables aren't fully set up
            List<string> foundAssets = new List<string>();
            
            string[] searchFolders = { "common", "client", "server" };
            string extension = typeof(T) == typeof(GameObject) ? "*.prefab" : "*.*";

            foreach (var folder in searchFolders)
            {
                string path = Path.Combine(info.rootPath, folder, "addressables");
                if (Directory.Exists(path))
                {
                    var files = Directory.GetFiles(path, extension, SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        // Return just the filename or a relative path as the "key"
                        foundAssets.Add(Path.GetFileNameWithoutExtension(file));
                    }
                }
            }

            if (foundAssets.Count == 0)
            {
                Debug.LogWarning($"[PluginManager] No assets of type {typeof(T).Name} found in module {moduleName}");
            }

            return await Task.FromResult(foundAssets.Distinct());
        }

        #endregion
    }
}
