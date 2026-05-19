using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityRuntimePlugins
{
    public interface IPluginServices
    {
        /// <summary>
        /// Returns a list of all currently loaded plugin modules.
        /// </summary>
        IEnumerable<PluginModuleInfo> GetLoadedModules();

        /// <summary>
        /// Asynchronously loads an asset from a specific module.
        /// </summary>
        Task<T> LoadAsset<T>(string moduleName, string assetPath) where T : Object;

        /// <summary>
        /// Invokes a function in a WASM module by name.
        /// </summary>
        void InvokeWasmFunction(string moduleName, string functionName, params object[] args);

        /// <summary>
        /// Invokes a function on a module using a strongly-typed command payload serialized as JSON.
        /// </summary>
        void SendCommand<TCommand>(string moduleName, TCommand command) where TCommand : class;

        /// <summary>
        /// Returns a list of asset keys/paths for assets of a specific type in a module.
        /// </summary>
        Task<IEnumerable<string>> GetAssetsByType<T>(string moduleName) where T : Object;
    }

    [System.Serializable]
    public class PluginModuleInfo
    {
        public string name;
        public string version;
        public string rootPath;
        public bool isClientLoaded;
        public bool isServerLoaded;
        public PluginManifest manifest;
    }
}
