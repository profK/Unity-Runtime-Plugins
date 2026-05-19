using UnityEngine;

namespace UnityRuntimePlugins
{
    /// <summary>
    /// A helper component to test and verify the behavior of the Plugin IoC container.
    /// Attach this script to a GameObject in a Unity scene and right-click -> 'Run IoC Container Verification'.
    /// </summary>
    public class PluginIoCTest : MonoBehaviour
    {
        private class MockPlugin : ICallablePlugin
        {
            public string Name { get; }
            public string LastInvokedFunction { get; private set; }
            public object[] LastInvokedArgs { get; private set; }

            public MockPlugin(string name)
            {
                Name = name;
            }

            public void InvokeFunction(string functionName, params object[] args)
            {
                LastInvokedFunction = functionName;
                LastInvokedArgs = args;
                string argsStr = args != null ? string.Join(", ", args) : "none";
                Debug.Log($"[MockPlugin:{Name}] InvokeFunction '{functionName}' called with args: {argsStr}");
            }
        }

        [System.Serializable]
        public class CalculateSumCommand
        {
            public int valA;
            public int valB;
        }

        [ContextMenu("Run IoC Container Verification")]
        public void RunTest()
        {
            Debug.Log("[PluginIoCTest] Starting Inversion of Control container verification...");

            var container = PluginIoCContainer.Instance;

            // 1. Verify Registration and Resolution
            var pluginA = new MockPlugin("ModuleA");
            var pluginB = new MockPlugin("ModuleB");

            container.Register<ICallablePlugin>("ModuleA", pluginA);
            container.Register<ICallablePlugin>("ModuleB", pluginB);

            if (container.Contains("ModuleA") && container.Contains("ModuleB"))
            {
                Debug.Log("[PluginIoCTest] PASS: ModuleA and ModuleB registered successfully.");
            }
            else
            {
                Debug.LogError("[PluginIoCTest] FAIL: Registration failed.");
            }

            var resolvedA = container.Resolve<ICallablePlugin>("ModuleA") as MockPlugin;
            if (resolvedA == pluginA)
            {
                Debug.Log("[PluginIoCTest] PASS: Resolved ModuleA matches registered instance.");
            }
            else
            {
                Debug.LogError("[PluginIoCTest] FAIL: Resolve returned incorrect instance.");
            }

            // 2. Verify Inter-Plugin Invocation via PluginManager
            PluginManager.Instance.InvokeWasmFunction("ModuleA", "CalculateSum", "5", "10");

            if (pluginA.LastInvokedFunction == "CalculateSum" && 
                pluginA.LastInvokedArgs != null && 
                pluginA.LastInvokedArgs.Length == 2 && 
                pluginA.LastInvokedArgs[0].ToString() == "5")
            {
                Debug.Log("[PluginIoCTest] PASS: PluginManager successfully routed inter-plugin call to ModuleA.");
            }
            else
            {
                Debug.LogError("[PluginIoCTest] FAIL: InvokeWasmFunction did not route correctly.");
            }

            // 3. Verify Strongly-Typed Dispatch (Type-Safe Dispatcher)
            var cmdObj = new CalculateSumCommand { valA = 100, valB = 200 };
            PluginManager.Instance.SendCommand("ModuleB", cmdObj);

            if (pluginB.LastInvokedFunction == "CalculateSumCommand" &&
                pluginB.LastInvokedArgs != null &&
                pluginB.LastInvokedArgs.Length == 1 &&
                pluginB.LastInvokedArgs[0].ToString().Contains("100") &&
                pluginB.LastInvokedArgs[0].ToString().Contains("200"))
            {
                Debug.Log("[PluginIoCTest] PASS: SendCommand successfully dispatched strongly-typed JSON command to ModuleB.");
            }
            else
            {
                Debug.LogError("[PluginIoCTest] FAIL: SendCommand did not route or serialize correctly.");
            }

            // 4. Verify Unregistration
            container.Unregister("ModuleA");
            if (!container.Contains("ModuleA"))
            {
                Debug.Log("[PluginIoCTest] PASS: Unregistered ModuleA successfully.");
            }
            else
            {
                Debug.LogError("[PluginIoCTest] FAIL: Unregister failed.");
            }

            container.Unregister("ModuleB");

            Debug.Log("[PluginIoCTest] Inversion of Control container verification finished.");
        }
    }
}
