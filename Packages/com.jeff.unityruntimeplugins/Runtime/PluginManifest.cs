using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityRuntimePlugins
{
    [Serializable]
    public class PluginManifest
    {
        public string name;
        public string version;
        public string entryPoint;
        public List<string> dependencies = new List<string>();
        public List<string> capabilities = new List<string>();

        public static PluginManifest FromJson(string json)
        {
            return JsonUtility.FromJson<PluginManifest>(json);
        }

        public string ToJson()
        {
            return JsonUtility.ToJson(this, true);
        }
    }
}
