using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityRuntimePlugins.Editor
{
    public class PluginProjectGenerator : EditorWindow
    {
        private string _pluginName = "NewPlugin";
        private string _moduleName = "MainModule";

        [MenuItem("Plugins/Project Generator")]
        public static void ShowWindow()
        {
            GetWindow<PluginProjectGenerator>("Plugin Generator");
        }

        private void OnGUI()
        {
            _pluginName = EditorGUILayout.TextField("Plugin Name", _pluginName);
            _moduleName = EditorGUILayout.TextField("Module Name", _moduleName);

            if (GUILayout.Button("Generate Plugin Scaffold"))
            {
                Generate();
            }
        }

        private void Generate()
        {
            string rootPath = Path.Combine(Application.dataPath, "PluginProjects", _pluginName);
            string modulePath = Path.Combine(rootPath, _moduleName);

            if (Directory.Exists(rootPath))
            {
                Debug.LogError($"Plugin project already exists at {rootPath}");
                return;
            }

            // Create folders (using source_code~ to hide C# source files from standard Unity compiler)
            string[] subfolders = { "client", "server", "common" };
            string[] leafFolders = { "addressables", "streaming_assets", "source_code~" };

            foreach (var sub in subfolders)
            {
                foreach (var leaf in leafFolders)
                {
                    Directory.CreateDirectory(Path.Combine(modulePath, sub, leaf));
                }
            }

            // Create manifest.json
            PluginManifest manifest = new PluginManifest
            {
                name = _moduleName,
                version = "1.0.0",
                entryPoint = $"{_moduleName}.wasm"
            };
            File.WriteAllText(Path.Combine(modulePath, "manifest.json"), manifest.ToJson());

            // Create C# .csproj for WASM compilation inside client/source_code~/
            string clientSourcePath = Path.Combine(modulePath, "client", "source_code~");
            string csprojContent = 
@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RuntimeIdentifier>wasi-wasm</RuntimeIdentifier>
    <OutputType>Exe</OutputType>
    <WasmSingleFileBundle>true</WasmSingleFileBundle>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <PublishTrimmed>false</PublishTrimmed>
    <WasmBuildNative>false</WasmBuildNative>
    <InvariantGlobalization>true</InvariantGlobalization>
    <BlazorEnableTimeZoneSupport>false</BlazorEnableTimeZoneSupport>
  </PropertyGroup>
</Project>";
            File.WriteAllText(Path.Combine(clientSourcePath, $"{_moduleName}.csproj"), csprojContent);

            // Create WasmPluginBase.cs inside client/source_code~/
            string baseClassContent = 
@"using System;
using System.IO;
using System.Threading;

public abstract class WasmPluginBase
{
    protected string HostToGuestFile { get; } = ""host_to_guest.txt"";
    protected string GuestToHostFile { get; } = ""guest_to_host.txt"";
    protected string LogFile { get; } = ""guest_debug.log"";
    
    private bool _isRunning = true;
    private readonly int _pollIntervalMs;

    protected WasmPluginBase(int pollIntervalMs = 30)
    {
        _pollIntervalMs = pollIntervalMs;
    }

    /// <summary>
    /// Starts the plugin execution loop. Call this from your Main method.
    /// </summary>
    public void Run()
    {
        Log(""Plugin initialized."");
        SafeDelete(HostToGuestFile);
        SafeDelete(GuestToHostFile);

        OnStart();

        while (_isRunning)
        {
            if (File.Exists(HostToGuestFile))
            {
                try
                {
                    string rawCommand = File.ReadAllText(HostToGuestFile).Trim();
                    if (!string.IsNullOrEmpty(rawCommand))
                    {
                        SafeDelete(HostToGuestFile); // Acknowledge command

                        if (rawCommand == ""EXIT"")
                        {
                            Log(""EXIT command received. Shutting down."");
                            _isRunning = false;
                            break;
                        }

                        // Parse command
                        string[] parts = rawCommand.Split('|');
                        string commandName = parts[0];
                        string[] args = new string[parts.Length - 1];
                        Array.Copy(parts, 1, args, 0, args.Length);

                        OnCommandReceived(commandName, args);
                    }
                }
                catch (IOException)
                {
                    // Handle lock conflicts gracefully during polling
                }
            }

            Thread.Sleep(_pollIntervalMs);
        }

        OnShutdown();
        Log(""Plugin shutdown complete."");
    }

    protected virtual void OnStart() { }
    protected virtual void OnShutdown() { }
    protected abstract void OnCommandReceived(string commandName, string[] args);

    /// <summary>
    /// Updates the text of a Host UI element.
    /// </summary>
    protected void SetText(string elementId, string value)
    {
        SendCommand($""[SET_TEXT]:{elementId}|{value}"");
    }

    /// <summary>
    /// Dynamic dynamic linking/calls to other plugins via the IoC container.
    /// </summary>
    protected void CallPlugin(string targetModule, string functionName, params string[] args)
    {
        string arguments = args != null && args.Length > 0 ? ""|"" + string.Join(""|"", args) : """";
        SendCommand($""CALL_PLUGIN:{targetModule}|{functionName}{arguments}"");
    }

    /// <summary>
    /// Invokes a static C# method on a Unity host class.
    /// </summary>
    protected void CallUnityStatic(string typeFullName, string methodName, params string[] args)
    {
        string arguments = args != null && args.Length > 0 ? ""|"" + string.Join(""|"", args) : """";
        SendCommand($""CALL_UNITY:STATIC|{typeFullName}|{methodName}{arguments}"");
    }

    /// <summary>
    /// Invokes an instance C# method on a GameObject's component in the active scene.
    /// </summary>
    protected void CallUnityInstance(string gameObjectName, string componentName, string methodName, params string[] args)
    {
        string arguments = args != null && args.Length > 0 ? ""|"" + string.Join(""|"", args) : """";
        SendCommand($""CALL_UNITY:INSTANCE|{gameObjectName}|{componentName}|{methodName}{arguments}"");
    }

    /// <summary>
    /// Sends a raw command back to the Host.
    /// </summary>
    protected void SendCommand(string command)
    {
        try
        {
            File.WriteAllText(GuestToHostFile, command);
        }
        catch (IOException ex)
        {
            Log($""Failed to write command: {ex.Message}"");
        }
    }

    /// <summary>
    /// Logs a debug line, aggregated automatically by the Host.
    /// </summary>
    protected void Log(string message)
    {
        try
        {
            File.AppendAllText(LogFile, $""[{GetType().Name}] {message}\n"");
        }
        catch { }
    }

    private void SafeDelete(string filePath)
    {
        try
        {
            if (File.Exists(filePath)) File.Delete(filePath);
        }
        catch { }
    }
}
";
            File.WriteAllText(Path.Combine(clientSourcePath, "WasmPluginBase.cs"), baseClassContent);

            // Create clean inherited PluginLogic.cs stub C# Logic file inside client/source_code~/
            string stubCode = 
@"using System;

public class PluginLogic : WasmPluginBase
{
    public static void Main()
    {
        new PluginLogic().Run();
    }

    protected override void OnStart()
    {
        Log(""Plugin active and initialized via WasmPluginBase."");
    }

    protected override void OnCommandReceived(string commandName, string[] args)
    {
        if (commandName == ""OnButtonPressed"")
        {
            Log(""Button press received by Guest."");
            SetText(""StatusText"", ""Hello from WasmPluginBase!"");
        }
    }
}
";
            File.WriteAllText(Path.Combine(clientSourcePath, "PluginLogic.cs"), stubCode);

            Debug.Log($"[PluginProjectGenerator] Generated complete WASM plugin project for {_pluginName} at {rootPath}");
            AssetDatabase.Refresh();
        }
    }
}
