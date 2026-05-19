using System;
using System.IO;
using System.Threading;

namespace TestPlugin
{
    public class PluginLogic
    {
        private const string HostToGuestFile = "host_to_guest.txt";
        private const string GuestToHostFile = "guest_to_host.txt";

        private static void LogDebug(string msg)
        {
            Console.WriteLine(msg);
            try
            {
                File.AppendAllText("guest_debug.log", $"{msg}\n");
                File.AppendAllText("/guest_debug.log", $"{msg}\n");
            }
            catch (Exception) {}
        }

        public static void Main() 
        {
            LogDebug("Main started. Initializing files...");

            // Clean up any existing stale files on startup
            try
            {
                if (File.Exists(HostToGuestFile)) File.Delete(HostToGuestFile);
                if (File.Exists(GuestToHostFile)) File.Delete(GuestToHostFile);
                if (File.Exists("guest_debug.log")) File.Delete("guest_debug.log");
                if (File.Exists("/guest_debug.log")) File.Delete("/guest_debug.log");
                LogDebug("Stale files cleared successfully.");
            }
            catch (Exception ex) 
            {
                LogDebug($"Startup cleanup error: {ex.GetType().Name} - {ex.Message}");
            }

            LogDebug("Entering polling loop...");

            // Start the background polling loop
            bool keepRunning = true;
            while (keepRunning)
            {
                try
                {
                    // Check both relative and absolute paths
                    string targetFile = null;
                    if (File.Exists(HostToGuestFile))
                    {
                        targetFile = HostToGuestFile;
                    }
                    else if (File.Exists("/" + HostToGuestFile))
                    {
                        targetFile = "/" + HostToGuestFile;
                    }

                    if (targetFile != null)
                    {
                        string cmd = File.ReadAllText(targetFile).Trim();
                        if (!string.IsNullOrEmpty(cmd))
                        {
                            LogDebug($"Received command from host: {cmd}");
                            
                            // Clear the command file to acknowledge receipt
                            File.WriteAllText(targetFile, string.Empty);
                            
                            if (cmd == "OnButtonPressed")
                            {
                                OnButtonPressed();
                            }
                            else if (cmd == "EXIT")
                            {
                                LogDebug("Received EXIT command. Exiting WASM gracefully.");
                                keepRunning = false;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"Loop exception: {ex.GetType().Name} - {ex.Message}");
                }

                if (keepRunning)
                {
                    Thread.Sleep(50);
                }
            }
            LogDebug("Main execution ended successfully.");
        }

        public static void OnButtonPressed()
        {
            LogDebug("Processing OnButtonPressed event.");
            SendCommand("SET_TEXT", "StatusText|pressed!");
        }

        private static void SendCommand(string command, string data)
        {
            try
            {
                File.WriteAllText(GuestToHostFile, $"{command}:{data}");
                File.WriteAllText("/" + GuestToHostFile, $"{command}:{data}");
                LogDebug($"Sent command back to host: {command}:{data}");
            }
            catch (Exception ex)
            {
                LogDebug($"SendCommand error: {ex.GetType().Name} - {ex.Message}");
            }
        }
    }
}
