using UnityEngine;
using UnityEngine.UI;

namespace UnityRuntimePlugins
{
    /// <summary>
    /// Attach this to a Button in a plugin prefab to link it to the WASM runtime.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class WasmUIButtonLink : MonoBehaviour
    {
        [SerializeField] private string wasmFunctionName = "OnButtonPressed";
        
        private WasmRuntimeProxy _proxy;

        private void Start()
        {
            // Find proxy in the same object or parent
            _proxy = GetComponentInParent<WasmRuntimeProxy>();
            
            // Fallback: search globally if not found (optional, depending on hierarchy)
            if (_proxy == null) _proxy = FindFirstObjectByType<WasmRuntimeProxy>();

            if (_proxy != null)
            {
                GetComponent<Button>().onClick.AddListener(TriggerWasm);
                Debug.Log($"[WasmUIButtonLink] Linked {name} to WASM function '{wasmFunctionName}'");
            }
            else
            {
                Debug.LogWarning($"[WasmUIButtonLink] No WasmRuntimeProxy found for button {name}.");
            }
        }

        private void TriggerWasm()
        {
            if (_proxy != null)
            {
                _proxy.InvokeFunction(wasmFunctionName);
            }
        }
    }
}
