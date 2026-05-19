using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityRuntimePlugins;

namespace UnityRuntimePlugins.Editor
{
    [CustomEditor(typeof(WasmRuntimeUIBridge))]
    public class WasmRuntimeUIBridgeEditor : UnityEditor.Editor
    {
        private WasmRuntimeUIBridge _bridge;
        private List<string> _availableUIElements = new List<string>();
        private List<string> _availablePluginMethods = new List<string>();

        private void OnEnable()
        {
            _bridge = (WasmRuntimeUIBridge)target;
            RefreshEditorMetadata();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Draw default components
            EditorGUILayout.PropertyField(serializedObject.FindProperty("runtimeProxy"));
            
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("WASM UI Callback Mappings", EditorStyles.boldLabel);

            var mappingsProp = serializedObject.FindProperty("callbackMappings");

            if (GUILayout.Button("Scan UI & Plugin Source Code", GUILayout.Height(30)))
            {
                RefreshEditorMetadata();
            }

            if (_availableUIElements.Count == 0 || _availablePluginMethods.Count == 0)
            {
                EditorGUILayout.HelpBox("Could not discover UI Elements or Plugin Methods. Ensure your UI Document or Canvas hierarchy contains named interactive elements and your plugin source path is correct.", MessageType.Warning);
            }

            for (int i = 0; i < mappingsProp.arraySize; i++)
            {
                SerializedProperty mapProp = mappingsProp.GetArrayElementAtIndex(i);
                SerializedProperty uiNameProp = mapProp.FindPropertyRelative("uiElementName");
                SerializedProperty eventTypeProp = mapProp.FindPropertyRelative("eventType");
                SerializedProperty methodProp = mapProp.FindPropertyRelative("wasmMethodName");

                EditorGUILayout.BeginVertical(GUI.skin.box);
                
                // 1. UI Element Selection Dropdown
                int uiIndex = Mathf.Max(0, _availableUIElements.IndexOf(uiNameProp.stringValue));
                int newUiIndex = EditorGUILayout.Popup("UI Element", uiIndex, _availableUIElements.ToArray());
                if (_availableUIElements.Count > 0) uiNameProp.stringValue = _availableUIElements[newUiIndex];

                // 2. Event Type Selection Dropdown
                string[] eventTypes = { "clicked", "changed" };
                int eventIndex = Mathf.Max(0, System.Array.IndexOf(eventTypes, eventTypeProp.stringValue));
                int newEventIndex = EditorGUILayout.Popup("Event Type", eventIndex, eventTypes);
                eventTypeProp.stringValue = eventTypes[newEventIndex];

                // 3. Plugin C# Method Dropdown
                int methodIndex = Mathf.Max(0, _availablePluginMethods.IndexOf(methodProp.stringValue));
                int newMethodIndex = EditorGUILayout.Popup("Plugin C# Method", methodIndex, _availablePluginMethods.ToArray());
                if (_availablePluginMethods.Count > 0) methodProp.stringValue = _availablePluginMethods[newMethodIndex];

                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Remove Mapping", GUILayout.Width(120)))
                {
                    mappingsProp.DeleteArrayElementAtIndex(i);
                    break;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
            }

            if (GUILayout.Button("Add New Callback Mapping"))
            {
                mappingsProp.arraySize++;
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void RefreshEditorMetadata()
        {
            _availableUIElements.Clear();
            _availablePluginMethods.Clear();

            // 1a. Discover named interactive elements in UI Toolkit (UIDocument)
            var uiDoc = _bridge.GetComponent<UIDocument>();
            if (uiDoc != null && uiDoc.visualTreeAsset != null)
            {
                var tempRoot = uiDoc.visualTreeAsset.Instantiate();
                tempRoot.Query<VisualElement>().ForEach(el =>
                {
                    if (!string.IsNullOrEmpty(el.name) && 
                        (el is Button || el is TextField || el is Toggle || el is Slider))
                    {
                        _availableUIElements.Add(el.name);
                    }
                });
            }

            // 1b. Discover named interactive elements in uGUI (Canvas child GameObjects)
            var uguiButtons = _bridge.GetComponentsInChildren<UnityEngine.UI.Button>(true);
            foreach (var btn in uguiButtons)
            {
                if (!string.IsNullOrEmpty(btn.name) && !_availableUIElements.Contains(btn.name))
                {
                    _availableUIElements.Add(btn.name);
                }
            }

            var uguiInputs = _bridge.GetComponentsInChildren<UnityEngine.UI.InputField>(true);
            foreach (var input in uguiInputs)
            {
                if (!string.IsNullOrEmpty(input.name) && !_availableUIElements.Contains(input.name))
                {
                    _availableUIElements.Add(input.name);
                }
            }

            // Detect TextMeshPro InputFields using string-based type check (avoids compile errors if TMPro is absent)
            foreach (var component in _bridge.GetComponentsInChildren<Component>(true))
            {
                if (component != null && component.GetType().FullName == "TMPro.TMP_InputField")
                {
                    if (!string.IsNullOrEmpty(component.name) && !_availableUIElements.Contains(component.name))
                    {
                        _availableUIElements.Add(component.name);
                    }
                }
            }

            // 2. Discover C# methods in uncompiled plugin source folders
            string targetModule = "ClientTest"; // Default fallback
            var proxyProp = serializedObject.FindProperty("runtimeProxy");
            if (proxyProp != null && proxyProp.objectReferenceValue != null)
            {
                var proxy = (WasmRuntimeProxy)proxyProp.objectReferenceValue;
                if (!string.IsNullOrEmpty(proxy.ModuleName))
                {
                    targetModule = proxy.ModuleName;
                }
            }

            string rootPath = Path.Combine(Application.dataPath, "PluginProjects");
            if (Directory.Exists(rootPath))
            {
                var sourceDirs = Directory.GetDirectories(rootPath, "source_code~", SearchOption.AllDirectories);
                foreach (var dir in sourceDirs)
                {
                    if (dir.Contains(targetModule))
                    {
                        ParseMethodsFromSourceDirectory(dir);
                    }
                }
            }
        }

        private void ParseMethodsFromSourceDirectory(string dirPath)
        {
            if (!Directory.Exists(dirPath)) return;

            var files = Directory.GetFiles(dirPath, "*.cs", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                try
                {
                    string content = File.ReadAllText(file);
                    var matches = Regex.Matches(content, @"public\s+(?:static\s+)?void\s+(\w+)\s*\(");
                    foreach (Match match in matches)
                    {
                        string methodName = match.Groups[1].Value;
                        if (!_availablePluginMethods.Contains(methodName))
                        {
                            _availablePluginMethods.Add(methodName);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[WasmRuntimeUIBridgeEditor] Failed to parse file {file}: {ex.Message}");
                }
            }
        }
    }
}
