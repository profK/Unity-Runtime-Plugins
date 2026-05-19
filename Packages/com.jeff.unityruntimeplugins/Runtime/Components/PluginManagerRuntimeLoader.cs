using UnityEngine;

namespace UnityRuntimePlugins
{
    public class PluginManagerRuntimeLoader : MonoBehaviour
    {
        [SerializeField] private string pluginsPath;
        [SerializeField] private bool loadOnStart = true;

        private async void Start()
        {
            if (loadOnStart)
            {
                await PluginManager.Instance.Initialize(pluginsPath);
            }
        }
    }
}
