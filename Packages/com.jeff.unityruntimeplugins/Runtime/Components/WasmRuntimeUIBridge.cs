using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace UnityRuntimePlugins
{
    [System.Serializable]
    public class UICallbackMap
    {
        public string uiElementName;
        public string eventType;        // e.g. "clicked", "changed"
        public string wasmMethodName;    // The C# method in your source code
    }

    // Removed [RequireComponent(typeof(UIDocument))] to support uGUI layouts out-of-the-box!
    public class WasmRuntimeUIBridge : MonoBehaviour
    {
        [SerializeField] private WasmRuntimeProxy runtimeProxy;
        [SerializeField] private List<UICallbackMap> callbackMappings = new List<UICallbackMap>();

        private VisualElement _root;

        private void OnEnable()
        {
            // Discover UI Toolkit root if a UIDocument exists
            var uiDoc = GetComponent<UIDocument>();
            if (uiDoc != null) _root = uiDoc.rootVisualElement;

            // Discovery fallback
            if (runtimeProxy == null)
            {
                runtimeProxy = GetComponentInParent<WasmRuntimeProxy>();
                if (runtimeProxy == null) runtimeProxy = FindFirstObjectByType<WasmRuntimeProxy>();
            }

            if (runtimeProxy != null)
            {
                runtimeProxy.OnWasmCommandReceived += ProcessCommand;
                Debug.Log($"[WasmRuntimeUIBridge] Linked to proxy for module: {runtimeProxy.name}");

                // Dynamically bind interactive visual elements based on edit-time mappings
                BindMappedUIElements();
            }
            else
            {
                Debug.LogWarning("[WasmRuntimeUIBridge] No WasmRuntimeProxy found to listen to.");
            }
        }

        private void OnDisable()
        {
            if (runtimeProxy != null)
            {
                runtimeProxy.OnWasmCommandReceived -= ProcessCommand;
            }
        }

        /// <summary>
        /// Registers callbacks on both UI Toolkit and uGUI (Canvas-based) elements.
        /// </summary>
        private void BindMappedUIElements()
        {
            if (callbackMappings == null) return;

            foreach (var map in callbackMappings)
            {
                if (string.IsNullOrEmpty(map.uiElementName) || string.IsNullOrEmpty(map.wasmMethodName))
                    continue;

                // 1. Try Binding UI Toolkit Elements
                if (_root != null)
                {
                    var element = _root.Q<VisualElement>(map.uiElementName);
                    if (element != null)
                    {
                        if (element is Button button && map.eventType == "clicked")
                        {
                            button.clicked += () => runtimeProxy.InvokeFunction(map.wasmMethodName);
                            Debug.Log($"[WasmRuntimeUIBridge] Bound UI Toolkit Button '{map.uiElementName}' click event to WASM '{map.wasmMethodName}'");
                            continue;
                        }
                        else if (element is TextField textField && map.eventType == "changed")
                        {
                            textField.RegisterCallback<ChangeEvent<string>>(evt => 
                                runtimeProxy.InvokeFunction(map.wasmMethodName, evt.newValue)
                            );
                            Debug.Log($"[WasmRuntimeUIBridge] Bound UI Toolkit TextField '{map.uiElementName}' change event to WASM '{map.wasmMethodName}'");
                            continue;
                        }
                    }
                }

                // 2. Try Binding uGUI (Canvas GameObject Hierarchy) Elements
                Transform target = transform.FindRecursive(map.uiElementName);
                if (target == null)
                {
                    // Fallback to global search if name is unique
                    var go = GameObject.Find(map.uiElementName);
                    if (go != null) target = go.transform;
                }

                if (target != null)
                {
                    // 2a. uGUI Button Click Event
                    var uguiButton = target.GetComponent<UnityEngine.UI.Button>();
                    if (uguiButton != null && map.eventType == "clicked")
                    {
                        uguiButton.onClick.AddListener(() => runtimeProxy.InvokeFunction(map.wasmMethodName));
                        Debug.Log($"[WasmRuntimeUIBridge] Bound uGUI Button '{map.uiElementName}' click event to WASM '{map.wasmMethodName}'");
                        continue;
                    }

                    // 2b. uGUI InputField Changed Event
                    var uguiInput = target.GetComponent<UnityEngine.UI.InputField>();
                    if (uguiInput != null && map.eventType == "changed")
                    {
                        uguiInput.onValueChanged.AddListener(val => runtimeProxy.InvokeFunction(map.wasmMethodName, val));
                        Debug.Log($"[WasmRuntimeUIBridge] Bound uGUI InputField '{map.uiElementName}' change event to WASM '{map.wasmMethodName}'");
                        continue;
                    }

                    // 2c. TextMeshPro InputField (Using Reflection to prevent compile issues if TMPro package is missing)
                    var tmpInput = target.GetComponent("TMPro.TMP_InputField");
                    if (tmpInput != null && map.eventType == "changed")
                    {
                        var eventProp = tmpInput.GetType().GetProperty("onValueChanged");
                        if (eventProp != null)
                        {
                            var onValueChangedEvent = eventProp.GetValue(tmpInput);
                            var addListenerMethod = onValueChangedEvent.GetType().GetMethod("AddListener");
                            if (addListenerMethod != null)
                            {
                                System.Action<string> call = val => runtimeProxy.InvokeFunction(map.wasmMethodName, val);
                                addListenerMethod.Invoke(onValueChangedEvent, new object[] { call });
                                Debug.Log($"[WasmRuntimeUIBridge] Bound TMP InputField '{map.uiElementName}' change event to WASM '{map.wasmMethodName}'");
                                continue;
                            }
                        }
                    }
                }
            }
        }

        private void ProcessCommand(string command)
        {
            if (string.IsNullOrEmpty(command)) return;

            if (command.StartsWith("[SET_TEXT]:"))
            {
                HandleSetText(command.Substring(11));
            }
        }

        private void HandleSetText(string data)
        {
            var parts = data.Split('|');
            if (parts.Length != 2) return;

            string elementId = parts[0];
            string value = parts[1];

            // 1. Try UI Toolkit (Label)
            if (_root != null)
            {
                var label = _root.Q<Label>(elementId);
                if (label != null)
                {
                    label.text = value;
                    Debug.Log($"[WasmRuntimeUIBridge] Updated Toolkit Label {elementId} to '{value}'");
                    return;
                }
            }

            // 2. Try uGUI (Canvas-based)
            Transform target = transform.FindRecursive(elementId);
            if (target == null)
            {
                var go = GameObject.Find(elementId);
                if (go != null) target = go.transform;
            }

            if (target != null)
            {
                var textComponent = target.GetComponent<UnityEngine.UI.Text>();
                if (textComponent != null)
                {
                    textComponent.text = value;
                    Debug.Log($"[WasmRuntimeUIBridge] Updated uGUI Text {elementId} to '{value}'");
                    return;
                }

                var tmpComponent = target.GetComponent("TMPro.TMP_Text");
                if (tmpComponent != null)
                {
                    var property = tmpComponent.GetType().GetProperty("text");
                    if (property != null)
                    {
                        property.SetValue(tmpComponent, value);
                        Debug.Log($"[WasmRuntimeUIBridge] Updated TextMeshPro {elementId} to '{value}'");
                        return;
                    }
                }

                var inputField = target.GetComponent<UnityEngine.UI.InputField>();
                if (inputField != null)
                {
                    inputField.text = value;
                    Debug.Log($"[WasmRuntimeUIBridge] Updated uGUI InputField {elementId} to '{value}'");
                    return;
                }
            }

            Debug.LogWarning($"[WasmRuntimeUIBridge] Element '{elementId}' not found or has no compatible text component in hierarchy.");
        }
    }

    public static class TransformExtensions
    {
        public static Transform FindRecursive(this Transform parent, string name)
        {
            if (parent.name == name) return parent;
            foreach (Transform child in parent)
            {
                var found = child.FindRecursive(name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
