using System;
using System.IO;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace UnityRuntimePlugins.Editor
{
    public static class PluginSystemInitializer
    {
        [MenuItem("Plugins/Initialize System")]
        public static void Initialize()
        {
            UnityEngine.Debug.Log("[PluginSystemInitializer] Beginning system initialization checks...");
            
            bool dotnetInstalled = false;
            bool workloadInstalled = false;
            string dotnetVersion = "Unknown";
            string dotnetPath = "/usr/local/share/dotnet/dotnet";

            // 1. Verify .NET SDK installation
            if (!File.Exists(dotnetPath))
            {
                dotnetPath = "dotnet"; // Fallback to PATH environment
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = dotnetPath,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    process.WaitForExit();
                    if (process.ExitCode == 0)
                    {
                        dotnetVersion = process.StandardOutput.ReadToEnd().Trim();
                        dotnetInstalled = true;
                        UnityEngine.Debug.Log($"[PluginSystemInitializer] Found .NET SDK version {dotnetVersion}");
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[PluginSystemInitializer] Failed to invoke dotnet --version: {ex.Message}");
            }

            if (!dotnetInstalled)
            {
                EditorUtility.DisplayDialog("Initialize System",
                    "Error: .NET 8 SDK was not found on your system.\n\nPlease install the .NET 8 SDK from https://dotnet.microsoft.com/en-us/download before continuing.",
                    "OK");
                return;
            }

            // 2. Install wasi-wasm workload if missing
            try
            {
                UnityEngine.Debug.Log("[PluginSystemInitializer] Checking/installing wasi-wasm workload...");
                var startInfo = new ProcessStartInfo
                {
                    FileName = dotnetPath,
                    Arguments = "workload install wasi-wasm",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    process.WaitForExit();
                    if (process.ExitCode == 0)
                    {
                        workloadInstalled = true;
                        UnityEngine.Debug.Log("[PluginSystemInitializer] wasi-wasm workload verified/installed successfully.");
                    }
                    else
                    {
                        string err = process.StandardError.ReadToEnd().Trim();
                        UnityEngine.Debug.LogWarning($"[PluginSystemInitializer] Workload install returned non-zero code. Error: {err}");
                        
                        // Check if it's already list-verified
                        workloadInstalled = CheckWorkloadInstalled(dotnetPath);
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[PluginSystemInitializer] Workload installation threw exception: {ex.Message}");
                workloadInstalled = CheckWorkloadInstalled(dotnetPath);
            }

            // 3. Setup Project directories
            string projectsDir = Path.Combine(Application.dataPath, "PluginProjects");
            if (!Directory.Exists(projectsDir))
            {
                Directory.CreateDirectory(projectsDir);
                UnityEngine.Debug.Log($"[PluginSystemInitializer] Created folder: Assets/PluginProjects/");
            }

            string pluginsDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Plugins"));
            if (!Directory.Exists(pluginsDir))
            {
                Directory.CreateDirectory(pluginsDir);
                UnityEngine.Debug.Log($"[PluginSystemInitializer] Created folder: Plugins/");
            }

            AssetDatabase.Refresh();

            // 4. Summarize results
            string summary = "Plugin System Initialized Successfully!\n\n" +
                             $"• .NET SDK: Found ({dotnetVersion})\n" +
                             $"• WASI-WASM Workload: {(workloadInstalled ? "Installed & Active" : "Verification Pending / Missing permissions")}\n" +
                             "• Project Folders: Setup & Configured\n\n" +
                             "You are now ready to generate, compile, and run WebAssembly plugins!";

            EditorUtility.DisplayDialog("Initialize System", summary, "OK");
        }

        private static bool CheckWorkloadInstalled(string dotnetPath)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = dotnetPath,
                    Arguments = "workload list",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(startInfo))
                {
                    process.WaitForExit();
                    if (process.ExitCode == 0)
                    {
                        string output = process.StandardOutput.ReadToEnd().ToLower();
                        return output.Contains("wasi-wasm") || output.Contains("wasi");
                    }
                }
            }
            catch { }
            return false;
        }
    }
}
