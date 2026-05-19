using System;
using System.Collections;
using System.IO;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;
using Wasmtime;

namespace UnityRuntimePlugins
{
    public class WasmRuntimeProxy : MonoBehaviour, ICallablePlugin
    {
        [SerializeField] private string moduleName;
        [SerializeField] private string wasmFileName = "logic.wasm";

        public string ModuleName => moduleName;

        public event Action<string> OnWasmCommandReceived;

        private Engine _engine;
        private Store _store;
        private Linker _linker;
        private Module _module;
        private Instance _instance;

        private string _stdoutPath;
        private string _hostToGuestPath;
        private string _guestToHostPath;
        private bool _isCommunicating = false;

        private System.Collections.Concurrent.ConcurrentQueue<string> _commandQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
        private System.Threading.AutoResetEvent _commandSignal = new System.Threading.AutoResetEvent(false);

        private void LogDebug(string msg)
        {
            Debug.Log(msg);
            try
            {
                string logPath = Path.Combine(Application.dataPath, "..", "unity_wasm_debug.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] [HOST] {msg}\n");
            }
            catch (Exception) {}
        }

        private void OnEnable()
        {
            StartCoroutine(WaitAndInitialize());
        }

        private System.Collections.IEnumerator WaitAndInitialize()
        {
            // Wait for PluginManager to be ready (it typically initializes in Start)
            yield return new WaitForSeconds(0.5f); 
            InitializeWasm();
        }

        private void OnDisable()
        {
            CleanupWasm();
        }

        private void InitializeWasm()
        {
            try
            {
                // Clear host debug log on new run
                try
                {
                    string logPath = Path.Combine(Application.dataPath, "..", "unity_wasm_debug.log");
                    if (File.Exists(logPath)) File.Delete(logPath);
                }
                catch (Exception) {}

                LogDebug($"Initializing WASM module: {moduleName}");

                string targetModule = string.IsNullOrEmpty(moduleName) ? "ClientTest" : moduleName;
                string wasmName = targetModule + ".wasm";
                string streamingAssetsPath = Path.Combine(Application.persistentDataPath, "ExtractedPlugins", "TestPlugins", targetModule, "client", "streaming_assets");
                
                if (!Directory.Exists(streamingAssetsPath)) 
                {
                    streamingAssetsPath = Path.Combine(Application.persistentDataPath, "ExtractedPlugins", "TestPlugins", targetModule, "client", "streaming_assets~");
                }
                
                _hostToGuestPath = Path.Combine(streamingAssetsPath, "host_to_guest.txt");
                _guestToHostPath = Path.Combine(streamingAssetsPath, "guest_to_host.txt");

                try
                {
                    if (File.Exists(_hostToGuestPath)) File.Delete(_hostToGuestPath);
                    if (File.Exists(_guestToHostPath)) File.Delete(_guestToHostPath);
                    string guestLogPath = Path.Combine(streamingAssetsPath, "guest_debug.log");
                    if (File.Exists(guestLogPath)) File.Delete(guestLogPath);
                }
                catch (Exception ex)
                {
                    LogDebug($"Error clearing stale files: {ex.Message}");
                }

                LogDebug($"Looking for WASM in: {streamingAssetsPath} (Exists: {Directory.Exists(streamingAssetsPath)})");
                if (Directory.Exists(streamingAssetsPath))
                {
                    LogDebug($"Files in folder: {string.Join(", ", Directory.GetFiles(streamingAssetsPath))}");
                }

                string persistentPath = Path.Combine(streamingAssetsPath, wasmName);
                
                if (!File.Exists(persistentPath))
                {
                    LogDebug($"WASM file not found at: {persistentPath}");
                    return;
                }

                _engine = new Engine();
                _linker = new Linker(_engine);
                _store = new Store(_engine);
 
                // CRITICAL FIX: Explicitly grant Read & Write permissions to directories and files
                var wasiConfig = new WasiConfiguration()
                    .WithInheritedStandardOutput()
                    .WithInheritedStandardError()
                    .WithEnvironmentVariable("WASMTIME_BACKTRACE_DETAILS", "1")
                    .WithArgs(wasmName, targetModule)
                    .WithPreopenedDirectory(streamingAssetsPath, ".", WasiDirectoryPermissions.Read | WasiDirectoryPermissions.Write, WasiFilePermissions.Read | WasiFilePermissions.Write)
                    .WithPreopenedDirectory(streamingAssetsPath, "/", WasiDirectoryPermissions.Read | WasiDirectoryPermissions.Write, WasiFilePermissions.Read | WasiFilePermissions.Write)
                    .WithPreopenedDirectory(Path.Combine(streamingAssetsPath, "managed"), "managed", WasiDirectoryPermissions.Read | WasiDirectoryPermissions.Write, WasiFilePermissions.Read | WasiFilePermissions.Write)
                    .WithPreopenedDirectory(Path.Combine(streamingAssetsPath, "managed"), "/managed", WasiDirectoryPermissions.Read | WasiDirectoryPermissions.Write, WasiFilePermissions.Read | WasiFilePermissions.Write);
                
                _store.SetWasiConfiguration(wasiConfig);
                _linker.DefineWasi();

                // Define host functions for compatibility/fallback
                _linker.DefineFunction("env", "UnityCommand", (Caller caller, int cmdPtr, int cmdLen, int dataPtr, int dataLen) => {
                    var memory = caller.GetMemory("memory");
                    string cmd = memory.ReadString(cmdPtr, cmdLen);
                    string data = memory.ReadString(dataPtr, dataLen);
                    LogDebug($"Received direct host command: [{cmd}]:{data}");
                    if (cmd == "CALL_PLUGIN")
                    {
                        HandleCallPluginCommand(data);
                    }
                    else
                    {
                        HandleWasmCommand($"[{cmd}]:{data}");
                    }
                });

                _linker.DefineFunction("env", "GetNextCommand", (Caller caller, int bufPtr, int maxLen) => {
                    string cmd;
                    while (!_commandQueue.TryDequeue(out cmd))
                    {
                        _commandSignal.WaitOne(100); 
                    }
                    
                    var memory = caller.GetMemory("memory");
                    if (memory == null) return 0;

                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(cmd);
                    int len = Math.Min(bytes.Length, maxLen);
                    for (int i = 0; i < len; i++)
                    {
                        memory.WriteByte(bufPtr + i, bytes[i]);
                    }
                    return len;
                });

                _module = Module.FromFile(_engine, persistentPath);
                _instance = _linker.Instantiate(_store, _module);

                LogDebug($"Successfully loaded {wasmFileName}. Starting background loop...");

                // Run the WASM _start in the background
                System.Threading.Tasks.Task.Run(() => {
                    try {
                        var start = _instance.GetFunction("_start");
                        if (start != null) start.Invoke();
                    } catch (Wasmtime.WasmtimeException ex) {
                        if (ex.ExitCode.HasValue) {
                            int code = ex.ExitCode.Value;
                            if (code != 0) {
                                LogDebug($"WASM exited with status {code}: {ex.Message}");
                            } else {
                                LogDebug($"WASM exited normally (status 0).");
                            }
                        } else {
                            LogDebug($"WASM trap error: {ex.Message}");
                        }
                    } catch (Exception e) {
                        LogDebug($"WASM Loop Error: {e.Message}");
                    }
                });

                // Register plugin in the IoC container
                PluginIoCContainer.Instance.Register<ICallablePlugin>(targetModule, this);

                StartCoroutine(CommunicationLoop());
            }
            catch (Exception e)
            {
                LogDebug($"Failed to initialize WASM: {e.Message}");
            }
        }

        public void InvokeFunction(string functionName, params object[] args)
        {
            string cmd = functionName;
            if (args != null && args.Length > 0)
            {
                cmd += "|" + string.Join("|", args);
            }
            LogDebug($"Enqueueing command: {cmd}");
            _commandQueue.Enqueue(cmd);
            _commandSignal.Set();
        }

        private void HandleCallPluginCommand(string data)
        {
            if (string.IsNullOrEmpty(data)) return;
            
            int firstPipe = data.IndexOf('|');
            if (firstPipe == -1)
            {
                LogDebug($"Invalid CALL_PLUGIN command data format (missing pipe): {data}");
                return;
            }

            string targetModule = data.Substring(0, firstPipe);
            string remainder = data.Substring(firstPipe + 1);
            
            int secondPipe = remainder.IndexOf('|');
            string functionName;
            object[] args;
            
            if (secondPipe == -1)
            {
                functionName = remainder;
                args = System.Array.Empty<object>();
            }
            else
            {
                functionName = remainder.Substring(0, secondPipe);
                string payload = remainder.Substring(secondPipe + 1);
                args = new object[] { payload };
            }

            LogDebug($"CALL_PLUGIN: Resolving target module '{targetModule}' to invoke '{functionName}'");
            var target = PluginIoCContainer.Instance.Resolve<ICallablePlugin>(targetModule);
            if (target != null)
            {
                target.InvokeFunction(functionName, args);
            }
            else
            {
                LogDebug($"CALL_PLUGIN Error: Target module '{targetModule}' is not registered in the IoC container.");
            }
        }

        private void HandleCallUnityCommand(string data)
        {
            if (string.IsNullOrEmpty(data)) return;

            string[] parts = data.Split('|');
            if (parts.Length < 3)
            {
                LogDebug($"CALL_UNITY Error: Invalid format. Must be STATIC|Type|Method|Args... or INSTANCE|GoName|CompName|Method|Args...");
                return;
            }

            string mode = parts[0]; // "STATIC" or "INSTANCE"

            MainThreadDispatcher.Enqueue(() => {
                try
                {
                    if (mode == "STATIC")
                    {
                        string typeName = parts[1];
                        string methodName = parts[2];
                        
                        object[] methodArgs = new object[parts.Length - 3];
                        for (int i = 0; i < methodArgs.Length; i++)
                        {
                            methodArgs[i] = parts[i + 3];
                        }

                        System.Type targetType = System.Type.GetType(typeName);
                        if (targetType == null)
                        {
                            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                            {
                                targetType = assembly.GetType(typeName);
                                if (targetType != null) break;
                            }
                        }

                        if (targetType == null)
                        {
                            LogDebug($"CALL_UNITY Error: Static target type '{typeName}' not found.");
                            return;
                        }

                        var methods = targetType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                        System.Reflection.MethodInfo bestMethod = null;
                        foreach (var m in methods)
                        {
                            if (m.Name == methodName && m.GetParameters().Length == methodArgs.Length)
                            {
                                bestMethod = m;
                                break;
                            }
                        }

                        if (bestMethod == null)
                        {
                            LogDebug($"CALL_UNITY Error: Static method '{methodName}' with {methodArgs.Length} arguments not found on type '{typeName}'.");
                            return;
                        }

                        var parameters = bestMethod.GetParameters();
                        object[] convertedArgs = new object[methodArgs.Length];
                        for (int i = 0; i < methodArgs.Length; i++)
                        {
                            convertedArgs[i] = System.Convert.ChangeType(methodArgs[i], parameters[i].ParameterType);
                        }

                        bestMethod.Invoke(null, convertedArgs);
                        LogDebug($"CALL_UNITY Success: Invoked static '{typeName}.{methodName}'");
                    }
                    else if (mode == "INSTANCE")
                    {
                        if (parts.Length < 4)
                        {
                            LogDebug("CALL_UNITY Error: Instance format must be INSTANCE|GameObjectName|ComponentName|MethodName|Args...");
                            return;
                        }

                        string goName = parts[1];
                        string compName = parts[2];
                        string methodName = parts[3];

                        object[] methodArgs = new object[parts.Length - 4];
                        for (int i = 0; i < methodArgs.Length; i++)
                        {
                            methodArgs[i] = parts[i + 4];
                        }

                        var go = GameObject.Find(goName);
                        if (go == null)
                        {
                            LogDebug($"CALL_UNITY Error: GameObject '{goName}' not found.");
                            return;
                        }

                        var comp = go.GetComponent(compName);
                        if (comp == null)
                        {
                            foreach (var component in go.GetComponents<Component>())
                            {
                                if (component != null && component.GetType().Name == compName)
                                {
                                    comp = component;
                                    break;
                                }
                            }
                        }

                        if (comp == null)
                        {
                            LogDebug($"CALL_UNITY Error: Component '{compName}' not found on GameObject '{goName}'.");
                            return;
                        }

                        var targetType = comp.GetType();
                        var methods = targetType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        System.Reflection.MethodInfo bestMethod = null;
                        foreach (var m in methods)
                        {
                            if (m.Name == methodName && m.GetParameters().Length == methodArgs.Length)
                            {
                                bestMethod = m;
                                break;
                            }
                        }

                        if (bestMethod == null)
                        {
                            LogDebug($"CALL_UNITY Error: Instance method '{methodName}' with {methodArgs.Length} arguments not found on component '{compName}'.");
                            return;
                        }

                        var parameters = bestMethod.GetParameters();
                        object[] convertedArgs = new object[methodArgs.Length];
                        for (int i = 0; i < methodArgs.Length; i++)
                        {
                            convertedArgs[i] = System.Convert.ChangeType(methodArgs[i], parameters[i].ParameterType);
                        }

                        bestMethod.Invoke(comp, convertedArgs);
                        LogDebug($"CALL_UNITY Success: Invoked instance '{goName}.{compName}.{methodName}'");
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"CALL_UNITY Exception: {ex.Message}");
                }
            });
        }

        protected void HandleWasmCommand(string command)
        {
            MainThreadDispatcher.Enqueue(() => {
                OnWasmCommandReceived?.Invoke(command);
            });
        }

        private IEnumerator CommunicationLoop()
        {
            _isCommunicating = true;
            string streamingAssetsPath = Path.GetDirectoryName(_hostToGuestPath);
            string guestLogPath = Path.Combine(streamingAssetsPath, "guest_debug.log");

            LogDebug($"CommunicationLoop started. Monitoring host_to_guest: '{_hostToGuestPath}' and guest_to_host: '{_guestToHostPath}'");
            
            while (_isCommunicating)
            {
                // 1. Send commands from Host to Guest
                if (_commandQueue.TryPeek(out string nextCmd))
                {
                    try
                    {
                        bool canSend = !File.Exists(_hostToGuestPath) || string.IsNullOrEmpty(File.ReadAllText(_hostToGuestPath));
                        if (canSend)
                        {
                            File.WriteAllText(_hostToGuestPath, nextCmd);
                            LogDebug($"Wrote command '{nextCmd}' to host_to_guest.txt");
                            _commandQueue.TryDequeue(out _); 
                        }
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"Exception sending command to guest: {ex.Message}");
                    }
                }

                // 2. Receive commands from Guest to Host
                try
                {
                    if (File.Exists(_guestToHostPath))
                    {
                        string guestCmd = File.ReadAllText(_guestToHostPath).Trim();
                        if (!string.IsNullOrEmpty(guestCmd))
                        {
                            File.WriteAllText(_guestToHostPath, string.Empty);
                            LogDebug($"Read command '{guestCmd}' from guest_to_host.txt");
                            
                            int colonIndex = guestCmd.IndexOf(':');
                            if (colonIndex != -1)
                            {
                                string cmd = guestCmd.Substring(0, colonIndex);
                                string data = guestCmd.Substring(colonIndex + 1);
                                
                                if (cmd == "CALL_PLUGIN")
                                {
                                    HandleCallPluginCommand(data);
                                }
                                else if (cmd == "CALL_UNITY")
                                {
                                    HandleCallUnityCommand(data);
                                }
                                else
                                {
                                    HandleWasmCommand($"[{cmd}]:{data}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"Exception receiving command from guest: {ex.Message}");
                }

                // 3. Aggregate Guest Debug Logs
                try
                {
                    if (File.Exists(guestLogPath))
                    {
                        string guestLogs = File.ReadAllText(guestLogPath);
                        if (!string.IsNullOrEmpty(guestLogs))
                        {
                            File.WriteAllText(guestLogPath, string.Empty);
                            foreach (var line in guestLogs.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                LogDebug($"[GUEST] {line}");
                            }
                        }
                    }
                }
                catch (Exception) {}

                yield return new WaitForSecondsRealtime(0.05f); // Poll every 50ms
            }
        }

        private void CleanupWasm()
        {
            LogDebug("CleanupWasm initiated. Shutting down communications...");
            
            string targetModule = string.IsNullOrEmpty(moduleName) ? "ClientTest" : moduleName;
            PluginIoCContainer.Instance.Unregister(targetModule);

            _isCommunicating = false;
            _commandSignal.Set(); 

            // 1. Signal Guest to exit gracefully
            try
            {
                if (!string.IsNullOrEmpty(_hostToGuestPath))
                {
                    File.WriteAllText(_hostToGuestPath, "EXIT");
                    LogDebug("Wrote EXIT signal to guest.");
                }
            }
            catch (Exception) {}

            // Wait 150ms for WASM thread to exit polling loop cleanly
            System.Threading.Thread.Sleep(150);

            // 2. Clear communication files
            try
            {
                if (File.Exists(_hostToGuestPath)) File.Delete(_hostToGuestPath);
                if (File.Exists(_guestToHostPath)) File.Delete(_guestToHostPath);
            }
            catch (Exception) {}

            // 3. Dispose resources safely (preventing native segfaults)
            LogDebug("Disposing Wasmtime store and engine resources...");
            _store?.Dispose();
            _engine?.Dispose();
            _instance = null;
            LogDebug("CleanupWasm finished successfully.");
        }
    }
}
