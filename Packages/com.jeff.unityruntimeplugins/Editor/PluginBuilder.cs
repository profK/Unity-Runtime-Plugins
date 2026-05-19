using System;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;
using System.Linq;

namespace UnityRuntimePlugins.Editor
{
    public static class PluginBuilder
    {
        [MenuItem("Plugins/Build All Plugins")]
        public static void BuildAllPlugins()
        {
            string pluginsRoot = Path.Combine(Application.dataPath, "PluginProjects");
            if (!Directory.Exists(pluginsRoot))
            {
                Debug.LogError($"[PluginBuilder] Plugins root not found at {pluginsRoot}");
                return;
            }

            string[] pluginDirs = Directory.GetDirectories(pluginsRoot);
            foreach (var dir in pluginDirs)
            {
                if (Path.GetFileName(dir).StartsWith(".")) continue;
                BuildPlugin(dir);
            }
        }

        [MenuItem("Plugins/Build Active Plugin")]
        public static void BuildActivePlugin()
        {
            // Just build all for now
            BuildAllPlugins();
        }

        private static void BuildPlugin(string pluginRoot)
        {
            string pluginName = Path.GetFileName(pluginRoot);
            string outputPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Plugins", $"{pluginName}.zip"));
            
            try
            {
                Debug.Log($"[PluginBuilder] Building plugin '{pluginName}'...");

                // 1. Compile WASM modules
                if (!CompileWasmModules(pluginRoot))
                {
                    Debug.LogError($"[PluginBuilder] Compilation failed for {pluginName}");
                    return;
                }

                // 2. Zip the plugin
                if (File.Exists(outputPath)) File.Delete(outputPath);
                string pluginsDir = Path.GetDirectoryName(outputPath);
                if (!Directory.Exists(pluginsDir)) Directory.CreateDirectory(pluginsDir);

                ZipFile.CreateFromDirectory(pluginRoot, outputPath);
                
                if (File.Exists(outputPath))
                {
                    Debug.Log($"[PluginBuilder] SUCCESS: ZIP created at {outputPath}");
                    
                    // Cleanup extraction
                    string persistentExtractionPath = Path.Combine(Application.persistentDataPath, "ExtractedPlugins", pluginName);
                    if (Directory.Exists(persistentExtractionPath))
                    {
                        Directory.Delete(persistentExtractionPath, true);
                        Debug.Log($"[PluginBuilder] Cleaned up extraction folder");
                    }
                }

                UnityEditor.AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                Debug.LogError($"[PluginBuilder] Build Exception for {pluginName}: {e.Message}");
            }
        }

        private static bool CompileWasmModules(string pluginRoot)
        {
            string[] modules = Directory.GetDirectories(pluginRoot);
            bool allSuccess = true;

            foreach (var modulePath in modules)
            {
                string moduleName = Path.GetFileName(modulePath);
                string sourcePath = Path.Combine(modulePath, "client", "source_code~");

                if (!Directory.Exists(sourcePath)) continue;

                Debug.Log($"[PluginBuilder] Compiling WASM for module: {moduleName}...");
                string publishDir = null;
                string tempBuildRoot = null;
                
                if (!RunDotnetBuild(sourcePath, moduleName, out publishDir, out tempBuildRoot))
                {
                    allSuccess = false;
                    continue;
                }

                // Deploy directly from temp publish directory to streaming_assets
                if (!string.IsNullOrEmpty(publishDir) && Directory.Exists(publishDir))
                {
                    string targetDir = Path.Combine(modulePath, "client", "streaming_assets");
                    if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
                    Directory.CreateDirectory(targetDir);
                    
                    string managedDir = Path.Combine(targetDir, "managed");
                    Directory.CreateDirectory(managedDir);
                    
                    string[] files = Directory.GetFiles(publishDir);
                    string wasmFileName = moduleName + ".wasm";

                    foreach (var file in files)
                    {
                        string fileName = Path.GetFileName(file);
                        if (fileName == "dotnet.wasm")
                        {
                            File.Copy(file, Path.Combine(targetDir, wasmFileName), true);
                        }
                        else
                        {
                            // Rename .dll to .bytes to prevent Unity from compiling it
                            string safeFileName = fileName.EndsWith(".dll") ? fileName.Replace(".dll", ".bytes") : fileName;
                            
                            File.Copy(file, Path.Combine(targetDir, safeFileName), true);
                            File.Copy(file, Path.Combine(managedDir, safeFileName), true);
                        }
                    }
                }
                
                // Clean up the temp build root now that we are done copying
                if (!string.IsNullOrEmpty(tempBuildRoot) && Directory.Exists(tempBuildRoot))
                {
                    Directory.Delete(tempBuildRoot, true);
                }
            }
            return allSuccess;
        }

        private static bool RunDotnetBuild(string workingDir, string moduleName, out string publishDir, out string tempBuildRoot)
        {
            tempBuildRoot = Path.Combine(Path.GetTempPath(), "UnityWasmBuild_" + Guid.NewGuid().ToString("N"));
            publishDir = Path.Combine(tempBuildRoot, "bin", "Release", "net8.0", "wasi-wasm", "publish");
            
            try
            {
                Directory.CreateDirectory(tempBuildRoot);
                CopyDirectory(workingDir, tempBuildRoot);

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/local/share/dotnet/dotnet",
                    Arguments = "publish -c Release -r wasi-wasm --force",
                    WorkingDirectory = tempBuildRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = System.Diagnostics.Process.Start(startInfo))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0) return false;
                }

                return true;
            }
            catch 
            { 
                return false; 
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir)) File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                string name = Path.GetFileName(dir);
                if (name == "bin" || name == "obj") continue;
                CopyDirectory(dir, Path.Combine(destDir, name));
            }
        }
    }
}
