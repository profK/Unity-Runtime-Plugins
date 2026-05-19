using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

namespace UnityRuntimePlugins.Editor
{
    public class PluginInspector : EditorWindow
    {
        private Vector2 _scrollPos;
        private string _selectedZipPath;
        private List<string> _availableZips = new List<string>();
        private Dictionary<string, ModuleData> _parsedModules = new Dictionary<string, ModuleData>();

        private class ModuleData
        {
            public string Name;
            public Dictionary<string, SectionData> Sections = new Dictionary<string, SectionData>();
        }

        private class SectionData
        {
            public Dictionary<string, List<string>> Subsections = new Dictionary<string, List<string>>();
        }

        [MenuItem("Plugins/Inspector")]
        public static void ShowWindow()
        {
            GetWindow<PluginInspector>("Plugin Inspector");
        }

        private void OnEnable()
        {
            RefreshAvailablePlugins();
        }

        private void RefreshAvailablePlugins()
        {
            _availableZips.Clear();
            string pluginsPath = Path.Combine(Application.dataPath, "..", "Plugins");
            if (Directory.Exists(pluginsPath))
            {
                var files = Directory.GetFiles(pluginsPath, "*.zip");
                _availableZips.AddRange(files);
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal();
            
            // Left Column: Available Zips
            EditorGUILayout.BeginVertical(GUILayout.Width(250));
            EditorGUILayout.LabelField("Available Plugins (.zip)", EditorStyles.boldLabel);
            if (GUILayout.Button("Refresh List")) RefreshAvailablePlugins();
            
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            foreach (var zipPath in _availableZips)
            {
                string fileName = Path.GetFileName(zipPath);
                if (GUILayout.Button(fileName, _selectedZipPath == zipPath ? EditorStyles.whiteLabel : EditorStyles.label))
                {
                    InspectZip(zipPath);
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            // Right Column: Contents
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("Plugin Hierarchy", EditorStyles.boldLabel);
            
            if (string.IsNullOrEmpty(_selectedZipPath))
            {
                EditorGUILayout.HelpBox("Select a plugin from the left to inspect its contents.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.TextField("Selected Path", _selectedZipPath);
                EditorGUILayout.Space();
                
                _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
                foreach (var modulePair in _parsedModules)
                {
                    DrawModule(modulePair.Value);
                }
                EditorGUILayout.EndScrollView();
            }
            
            EditorGUILayout.EndVertical();
            
            EditorGUILayout.EndHorizontal();
        }

        private void DrawModule(ModuleData module)
        {
            EditorGUILayout.LabelField($"Module: {module.Name}", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            
            foreach (var sectionPair in module.Sections)
            {
                EditorGUILayout.LabelField($"Section: {sectionPair.Key}", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                
                foreach (var subPair in sectionPair.Value.Subsections)
                {
                    EditorGUILayout.LabelField($"{subPair.Key}:", EditorStyles.miniBoldLabel);
                    EditorGUI.indentLevel++;
                    foreach (var file in subPair.Value)
                    {
                        DrawFile(file);
                    }
                    EditorGUI.indentLevel--;
                }
                
                EditorGUI.indentLevel--;
            }
            
            EditorGUI.indentLevel--;
            EditorGUILayout.Space();
        }

        private void DrawFile(string filePath)
        {
            string fileName = Path.GetFileName(filePath);
            string ext = Path.GetExtension(filePath).ToLower();
            string type = GetAssetType(ext);
            
            EditorGUILayout.LabelField($"{fileName} ({type})", EditorStyles.label);
        }

        private string GetAssetType(string ext)
        {
            return ext switch
            {
                ".wasm" => "WebAssembly Module",
                ".json" => "Metadata/Config",
                ".prefab" => "Prefab Asset",
                ".png" or ".jpg" => "Texture/Image",
                ".uss" or ".uxml" => "UI Resource",
                ".txt" => "Plain Text",
                _ => string.IsNullOrEmpty(ext) ? "Folder/Unknown" : $"{ext.ToUpper().Substring(1)} File"
            };
        }

        private void InspectZip(string path)
        {
            _selectedZipPath = path;
            _parsedModules.Clear();
            
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(path))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        ParseEntry(entry.FullName);
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error reading ZIP: {e.Message}");
            }
        }

        private void ParseEntry(string fullName)
        {
            if (fullName.EndsWith("/")) return; // Skip directories

            string[] parts = fullName.Split(new[] { '/', '\\' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return; // Expecting <module>/<section>/<subsection>/...

            string moduleName = parts[0];
            string sectionName = parts[1];
            string subsectionName = parts[2];

            if (!_parsedModules.TryGetValue(moduleName, out var module))
            {
                module = new ModuleData { Name = moduleName };
                _parsedModules.Add(moduleName, module);
            }

            if (!module.Sections.TryGetValue(sectionName, out var section))
            {
                section = new SectionData();
                module.Sections.Add(sectionName, section);
            }

            if (!section.Subsections.TryGetValue(subsectionName, out var subsection))
            {
                subsection = new List<string>();
                section.Subsections.Add(subsectionName, subsection);
            }

            subsection.Add(fullName);
        }
    }
}
