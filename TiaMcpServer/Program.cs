using System;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Diagnostics;
using TiaMcpServer.Mcp;

namespace TiaMcpServer
{
    class Program
    {
        public static string TiaPath { get; private set; } = @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\V20";
        public static bool AssembliesLoaded { get; private set; } = false;
        public static string CurrentVersion { get; private set; } = "V20";

        public static void UpdateTiaPath(string version)
        {
            if (string.IsNullOrEmpty(version)) return;

            // Normalize version string (e.g., "17", "V17", "v17" -> "V17")
            string v = version.ToUpper();
            if (!v.StartsWith("V")) v = "V" + v;

            if (AssembliesLoaded && CurrentVersion != v)
            {
                throw new InvalidOperationException($"Cannot switch to {v}. Siemens assemblies for {CurrentVersion} are already loaded in this process. Please restart the MCP server to switch TIA Portal versions.");
            }

            string path = $@"C:\Program Files\Siemens\Automation\Portal {v}\PublicAPI\{v}";
            if (!Directory.Exists(path))
            {
                // Try alternate location
                path = $@"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI\{v}";
            }

            TiaPath = path;
            CurrentVersion = v;
            Console.Error.WriteLine($"Updated TIA Path to: {TiaPath}");
        }

        private static void AutoDetectTiaPath()
        {
            // 1. Check for running processes
            var processes = Process.GetProcessesByName("Siemens.Automation.Portal");
            if (processes.Length > 0)
            {
                foreach (var process in processes)
                {
                    try
                    {
                        string exePath = process.MainModule.FileName;
                        // Path is like: C:\Program Files\Siemens\Automation\Portal V17\Bin\Siemens.Automation.Portal.exe
                        string portalDir = Path.GetDirectoryName(Path.GetDirectoryName(exePath));
                        string version = Path.GetFileName(portalDir).Replace("Portal ", "");
                        
                        // Check if it's a version we know about or can find API for
                        string apiPath = Path.Combine(portalDir, "PublicAPI", version, "Siemens.Engineering.dll");
                        if (File.Exists(apiPath))
                        {
                            Console.Error.WriteLine($"Detected running TIA Portal {version}. Using API from: {apiPath}");
                            TiaPath = Path.GetDirectoryName(apiPath);
                            CurrentVersion = version;
                            return;
                        }

                        // Try V20 PublicAPI structure (PublicAPI\V17, PublicAPI\V18, etc.)
                        string v20Root = @"C:\Program Files\Siemens\Automation\Portal V20\PublicAPI";
                        if (Directory.Exists(v20Root))
                        {
                            apiPath = Path.Combine(v20Root, version, "Siemens.Engineering.dll");
                            if (File.Exists(apiPath))
                            {
                                Console.Error.WriteLine($"Detected running TIA Portal {version}. Using API from V20 PublicAPI: {apiPath}");
                                TiaPath = Path.GetDirectoryName(apiPath);
                                CurrentVersion = version;
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Error checking running process: {ex.Message}");
                    }
                }
            }

            // 2. Fallback to scanning Program Files for latest available API
            string automationRoot = @"C:\Program Files\Siemens\Automation";
            if (Directory.Exists(automationRoot))
            {
                var dirs = Directory.GetDirectories(automationRoot, "Portal V*")
                                    .OrderByDescending(d => d)
                                    .ToList();

                foreach (var dir in dirs)
                {
                    string version = Path.GetFileName(dir).Replace("Portal ", "");
                    string apiPath = Path.Combine(dir, "PublicAPI", version, "Siemens.Engineering.dll");
                    if (File.Exists(apiPath))
                    {
                        Console.Error.WriteLine($"Using latest installed TIA Portal {version} API: {apiPath}");
                        TiaPath = Path.GetDirectoryName(apiPath);
                        CurrentVersion = version;
                        return;
                    }
                }
                
                // Check V20 PublicAPI subdirectories
                string v20PublicApi = Path.Combine(automationRoot, "Portal V20", "PublicAPI");
                if (Directory.Exists(v20PublicApi))
                {
                    var v20Dirs = Directory.GetDirectories(v20PublicApi, "V*")
                                           .OrderByDescending(d => d)
                                           .ToList();
                    foreach (var vDir in v20Dirs)
                    {
                        string apiPath = Path.Combine(vDir, "Siemens.Engineering.dll");
                        if (File.Exists(apiPath))
                        {
                            string version = Path.GetFileName(vDir);
                            Console.Error.WriteLine($"Using latest API found in V20 PublicAPI ({version}): {apiPath}");
                            TiaPath = vDir;
                            CurrentVersion = version;
                            return;
                        }
                    }
                }
            }
            
            Console.Error.WriteLine($"Warning: No Siemens.Engineering.dll found. Defaulting to: {TiaPath}");
        }

        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            
            // Try to find a valid TIA path before anything triggers assembly loading
            AutoDetectTiaPath();

            AppDomain.CurrentDomain.AssemblyResolve += (sender, eventArgs) =>
            {
                var assemblyName = new AssemblyName(eventArgs.Name);
                string assemblyPath = Path.Combine(TiaPath, assemblyName.Name + ".dll");

                if (File.Exists(assemblyPath))
                {
                    AssembliesLoaded = true;
                    // Load the assembly and return it. 
                    // Note: If version differs from requested, .NET might still reject it 
                    // unless we return it here.
                    return Assembly.LoadFrom(assemblyPath);
                }
                
                // Attempt to find it in other PublicAPI versions if possible
                return null;
            };

            if (args.Length > 0 && args[0] == "--version")
            {
                Console.WriteLine("1.0.0");
                return;
            }

            using (var server = new Server())
            {
                server.Run();
            }
        }
    }
}
