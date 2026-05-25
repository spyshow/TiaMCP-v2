#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.Hmi.Screen;

namespace TiaMcpServer.Tia
{
    public class TiaManager : IDisposable
    {
        private TiaPortal _tiaPortal;
        private Project _currentProject;

        public string Connect(string version = null)
        {
            try
            {
                if (!string.IsNullOrEmpty(version))
                {
                    Program.UpdateTiaPath(version);
                }
                else if (!Program.AssembliesLoaded)
                {
                    // If no version specified and not loaded yet, try one more auto-detection
                    // in case TIA was opened after the server started.
                    // (Note: AutoDetectTiaPath is private in Program, but we can access TiaPath/CurrentVersion)
                    Console.Error.WriteLine($"Connecting using detected version: {Program.CurrentVersion}");
                }

                LogDebug($"Connecting to TIA Portal... (Version: {Program.CurrentVersion}, Path: {Program.TiaPath})");

                var processes = TiaPortal.GetProcesses();
                if (processes.Any())
                {
                    Console.Error.WriteLine("Found running TIA Portal process. Attempting to attach...");
                    Console.Error.WriteLine("NOTE: If this hangs, check TIA Portal window for a 'Grant Access' confirmation dialog.");
                    
                    _tiaPortal = processes.First().Attach();
                    string result = $"Attached to running TIA Portal {Program.CurrentVersion} (PID: {_tiaPortal.GetCurrentProcess().Id})";
                    
                    if (_tiaPortal.Projects.Any())
                    {
                        _currentProject = _tiaPortal.Projects.First();
                        result += $"\nDetected open project: {_currentProject.Name}";
                    }
                    return result;
                }
                else
                {
                    LogDebug("No running TIA Portal found. Launching new instance...");
                    _tiaPortal = new TiaPortal(TiaPortalMode.WithoutUserInterface);
                    return $"Launched new TIA Portal {Program.CurrentVersion} instance (WithoutUserInterface)";
                }
            }
            catch (Exception ex)
            {
                string msg = $"Error connecting to TIA Portal: {ex.Message}";
                if (ex.InnerException != null) msg += $" -> {ex.InnerException.Message}";
                
                if (ex.Message.Contains("Siemens.Engineering"))
                {
                    msg += $"\n\nPossible cause: Siemens.Engineering.dll version mismatch or file not found at {Program.TiaPath}.";
                    msg += "\nPlease ensure the TIA Portal version matches the installed assemblies.";
                }
                
                return msg;
            }
        }

        private void LogDebug(string msg) { Console.Error.WriteLine($"DEBUG: {msg}"); }

        public string ListProjects(string path)
        {
            if (!Directory.Exists(path)) return "Directory not found.";

            var files = Directory.GetFiles(path, "*.ap*");
            if (files.Length == 0) return "No TIA Portal projects found in the specified directory.";

            return "Available projects:\n" + string.Join("\n", files.Select(f => $"- {Path.GetFileName(f)}"));
        }

        public string OpenProject(string path)
        {
            if (_tiaPortal == null) return "TIA Portal not connected. Call connect_tia first.";

            try
            {
                // Check if any project is currently open in TIA Portal (source of truth)
                if (_tiaPortal.Projects.Any())
                {
                    var openProject = _tiaPortal.Projects.First();
                    if (string.Equals(openProject.Path.FullName, path, StringComparison.OrdinalIgnoreCase))
                    {
                         _currentProject = openProject;
                         return $"Project already open: {_currentProject.Name}";
                    }
                    
                    return $"A project is already open: {openProject.Name}. Please use 'close_project' first.";
                }

                var projectComposition = _tiaPortal.Projects;
                _currentProject = projectComposition.Open(new FileInfo(path));
                return $"Successfully opened project: {_currentProject.Name}";
            }
            catch (Exception ex)
            {
                return $"Error opening project: {ex.Message}";
            }
        }

        public string CloseProject()
        {
            if (_tiaPortal == null) return "TIA Portal not connected.";
            
            try
            {
                if (_tiaPortal.Projects.Any())
                {
                    var project = _tiaPortal.Projects.First();
                    string name = project.Name;
                    project.Close();
                    _currentProject = null;
                    return $"Successfully closed project: {name}";
                }
                return "No project is currently open.";
            }
            catch (Exception ex)
            {
                return $"Error closing project: {ex.Message}";
            }
        }

        public string GetProjectInfo()
        {
            if (_currentProject == null) return "No project is currently open in TIA Portal.";

            var allDevices = GetAllDevices(_currentProject).ToList();
            var summary = $"Project: {_currentProject.Name}\n" +
                          $"Path: {_currentProject.Path.FullName}\n" +
                          $"Device Count: {allDevices.Count}\n\n" +
                          "Devices:\n" +
                          string.Join("\n", allDevices.Select(d => $"- {d.Name} [{GetDeviceCategory(d.TypeIdentifier)}] ({d.TypeIdentifier})"));

            return summary;
        }

        public string ListDevices()
        {
            if (_currentProject == null) return "[]"; // No project open
            
            var devicesList = new List<object>();
            try
            {
                foreach (Device device in GetAllDevices(_currentProject))
                {
                    var deviceObj = new Dictionary<string, object>
                    {
                        { "Name", device.Name },
                        { "Type", device.TypeIdentifier },
                        { "Category", GetDeviceCategory(device.TypeIdentifier) }
                    };

                    // Try to get attributes from top level device if possible (though often they are on items)
                    try { var val = device.GetAttribute("ArticleNumber"); if (val != null) deviceObj["ArticleNumber"] = val; } catch { }
                    try { var val = device.GetAttribute("Version"); if (val != null) deviceObj["Version"] = val; } catch { }

                    deviceObj["Items"] = GetDeviceItemsHierarchy(device.DeviceItems);
                    
                    devicesList.Add(deviceObj);
                }
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { Error = ex.Message });
            }
            
            return JsonConvert.SerializeObject(devicesList, Formatting.Indented);
        }

        private List<object> GetDeviceItemsHierarchy(DeviceItemComposition items)
        {
            var list = new List<object>();
            foreach (DeviceItem item in items)
            {
                var itemObj = new Dictionary<string, object>
                {
                    { "Name", item.Name },
                    { "Classification", item.Classification.ToString() }
                };

                // Helper to safely get attribute and log failures
                void AddAttribute(string jsonKey, string attrName)
                {
                    try
                    {
                        var val = item.GetAttribute(attrName);
                        if (val != null)
                        {
                            itemObj[jsonKey] = val.ToString();
                        }
                    }
                    catch (Exception ex)
                    {
                        try 
                        { 
                            File.AppendAllText("tia_attributes_debug.log", $"[{item.Name}] Failed to get '{attrName}': {ex.Message}\n"); 
                        } catch { }
                    }
                }

                AddAttribute("ArticleNumber", "ArticleNumber");
                if (!itemObj.ContainsKey("ArticleNumber"))
                {
                    AddAttribute("ArticleNumber", "OrderNumber");
                }
                
                AddAttribute("Version", "Version");
                if (!itemObj.ContainsKey("Version")) AddAttribute("Version", "Firmware");
                if (!itemObj.ContainsKey("Version")) AddAttribute("Version", "FirmwareVersion");

                AddAttribute("TypeName", "TypeName");
                AddAttribute("Position", "PositionNumber");

                // Children
                var children = GetDeviceItemsHierarchy(item.DeviceItems);
                if (children.Any())
                {
                    itemObj["Children"] = children;
                }

                list.Add(itemObj);
            }
            return list;
        }

        private IEnumerable<Device> GetAllDevices(Project project)
        {
            var devices = new List<Device>();
            if (project == null) return devices;

            try 
            {
                foreach (Device d in project.Devices)
                {
                    devices.Add(d);
                }
                
                foreach (DeviceUserGroup group in project.DeviceGroups)
                {
                    GetDevicesFromGroup(group, devices);
                }

                if (project.UngroupedDevicesGroup != null)
                {
                    GetDevicesFromUngroupedGroup(project.UngroupedDevicesGroup, devices);
                }
            }
            catch (Exception)
            {
                // Silently continue or log if needed
            }

            return devices;
        }

        private void GetDevicesFromGroup(DeviceUserGroup group, List<Device> devices)
        {
            foreach (Device d in group.Devices)
            {
                devices.Add(d);
            }
            
            foreach (DeviceUserGroup subGroup in group.Groups)
                GetDevicesFromGroup(subGroup, devices);
        }

        private void GetDevicesFromUngroupedGroup(dynamic group, List<Device> devices)
        {
            foreach (Device d in group.Devices)
            {
                devices.Add(d);
            }

            foreach (var subGroup in group.Groups)
                GetDevicesFromUngroupedGroup(subGroup, devices);
        }

        private Dictionary<string, string> BuildIpToDeviceMap(Project project)
        {
            var map = new Dictionary<string, string>();
            var devices = GetAllDevices(project);

            foreach (var device in devices)
            {
                // Determine the "Preferred Name" for this device.
                // For GSD devices, the root name is generic (GSD device_X).
                // The "Real" name is often on a Head Module (HM) item item.
                string preferredName = device.Name;
                string hmName = FindHeadModuleName(device.DeviceItems);
                if (!string.IsNullOrEmpty(hmName))
                {
                    preferredName = hmName;
                }

                MapDeviceIpsRecursive(device.DeviceItems, preferredName, map);
            }
            return map;
        }

        private string FindHeadModuleName(DeviceItemComposition items)
        {
            foreach (DeviceItem item in items)
            {
                // In logs we saw Class: HM for the items with the correct names
                if (item.Classification.ToString().Equals("HM", StringComparison.OrdinalIgnoreCase))
                {
                    return item.Name;
                }
                
                // Recurse (shallowly? usually HM is top level or near top)
                string found = FindHeadModuleName(item.DeviceItems);
                if (found != null) return found;
            }
            return null;
        }

        private void MapDeviceIpsRecursive(DeviceItemComposition items, string deviceName, Dictionary<string, string> map)
        {
            foreach (DeviceItem item in items)
            {
                // ... (rest of logic uses 'deviceName' which is now the preferred name)
                
                try
                {
                    // File.AppendAllText("tia_debug_v2.log", $"[Scan] ...\n");

                    var netInterface = item.GetService<Siemens.Engineering.HW.Features.NetworkInterface>();
                    if (netInterface != null)
                    {
                        foreach (var node in netInterface.Nodes)
                        {
                            try
                            {
                                var ip = node.GetAttribute("Address");
                                if (ip != null)
                                {
                                    string ipStr = ip.ToString();
                                    if (!string.IsNullOrEmpty(ipStr) && !map.ContainsKey(ipStr))
                                    {
                                        map[ipStr] = deviceName;
                                    }
                                }
                            }
                            catch {}
                        }
                    }
                }
                catch {}
                
                MapDeviceIpsRecursive(item.DeviceItems, deviceName, map);
            }
        }

        public string ListNetworks()
        {
            if (_currentProject == null) return "[]";
            
            var list = new List<object>();
            try
            {
                var ipMap = BuildIpToDeviceMap(_currentProject);

                foreach (Subnet subnet in _currentProject.Subnets)
                {
                    var subnetObj = new Dictionary<string, object>
                    {
                        { "SubnetName", subnet.Name },
                        { "Type", subnet.GetType().Name },
                        { "Nodes", new List<object>() }
                    };

                    var nodesList = (List<object>)subnetObj["Nodes"];

                    foreach (Node node in subnet.Nodes)
                    {
                        var nodeObj = new Dictionary<string, object>();
                        string ip = null;
                        
                        try 
                        { 
                            var ipAttr = node.GetAttribute("Address"); 
                            if(ipAttr != null) 
                            { 
                                ip = ipAttr.ToString();
                                nodeObj["Address"] = ip; 
                            }
                        } catch {}
                        
                        try { var mask = node.GetAttribute("SubnetMask"); if(mask != null) nodeObj["SubnetMask"] = mask; } catch {}

                        // Enhanced Context Resolution
                        string deviceType = "Unknown";
                        string articleNumber = null;
                        string cpuName = null;
                        string stationName = null;
                        string resolvedName = null;

                        if (ip != null && ipMap.ContainsKey(ip))
                        {
                             resolvedName = ipMap[ip];
                        }

                        try
                        {
                            IEngineeringObject parent = node.Parent;
                            while (parent != null)
                            {
                                if (parent is DeviceItem di)
                                {
                                    string cls = di.Classification.ToString();
                                    if (cls.Equals("CPU", StringComparison.OrdinalIgnoreCase) || cls.Equals("HMI", StringComparison.OrdinalIgnoreCase))
                                    {
                                        cpuName = di.Name;
                                        deviceType = cls;
                                        try { articleNumber = di.GetAttribute("ArticleNumber")?.ToString() ?? di.GetAttribute("OrderNumber")?.ToString(); } catch {}
                                    }
                                    
                                    if (!nodeObj.ContainsKey("InterfaceName"))
                                    {
                                        nodeObj["InterfaceName"] = di.Name;
                                    }
                                }
                                else if (parent is Device dev)
                                {
                                    stationName = dev.Name;
                                    if (deviceType == "Unknown") deviceType = GetDeviceCategory(dev.TypeIdentifier);
                                    break;
                                }
                                parent = parent.Parent;
                            }
                        }
                        catch {}

                        if (!string.IsNullOrEmpty(resolvedName))
                        {
                             if (stationName != null && resolvedName == stationName && !string.IsNullOrEmpty(cpuName))
                                 nodeObj["DeviceName"] = cpuName;
                             else
                                 nodeObj["DeviceName"] = resolvedName;
                        }
                        else
                        {
                            nodeObj["DeviceName"] = cpuName ?? stationName ?? "Unknown";
                        }

                        nodeObj["DeviceType"] = deviceType;
                        if (!string.IsNullOrEmpty(articleNumber)) nodeObj["ArticleNumber"] = articleNumber;
                        if (stationName != null && nodeObj["DeviceName"].ToString() != stationName) nodeObj["StationName"] = stationName;

                        nodesList.Add(nodeObj);
                    }
                    
                    list.Add(subnetObj);
                }
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { Error = ex.Message });
            }
            
            return JsonConvert.SerializeObject(list, Formatting.Indented);
        }

        public string ListPlcBlocks(string deviceName)
        {
            if (_currentProject == null) return "[]";
            
            DeviceItem plcItem = FindDeviceItem(_currentProject, deviceName);
            if (plcItem == null) return JsonConvert.SerializeObject(new { Error = $"Device '{deviceName}' not found." });
            
            // Find SoftwareContainer info
            var softwareContainer = plcItem.GetService<Siemens.Engineering.HW.Features.SoftwareContainer>();
            if (softwareContainer == null || softwareContainer.Software == null)
            {
                return JsonConvert.SerializeObject(new { Error = $"Device '{deviceName}' does not contain software (or is not a PLC)." });
            }

            var software = softwareContainer.Software as PlcSoftware;
            if (software == null) return JsonConvert.SerializeObject(new { Error = $"Device '{deviceName}' software is not PlcSoftware." });

            var result = new Dictionary<string, object>
            {
                { "Device", deviceName },
                { "Blocks", GetBlockGroupHierarchy(software.BlockGroup) }
            };
            
            return JsonConvert.SerializeObject(result, Formatting.Indented);
        }

        private List<object> GetBlockGroupHierarchy(PlcBlockGroup group)
        {
            var list = new List<object>();
            foreach (dynamic block in group.Blocks)
            {
                string blockType = block.GetType().Name;
                if (blockType.Length <= 3) blockType = blockType.ToUpper();

                var blockObj = new Dictionary<string, object>
                {
                    { "Name", block.Name },
                    { "Type", blockType },
                    { "Number", block.Number }
                };

                string description = "";
                try { description = GetFirstComment(block.Comment); } catch {}
                
                if (string.IsNullOrEmpty(description))
                {
                    try
                    {
                        var attr = block.GetAttribute("Description");
                        if (attr != null) description = attr.ToString();
                    }
                    catch { }
                }

                if (!string.IsNullOrEmpty(description))
                {
                    blockObj["Description"] = description;
                }

                list.Add(blockObj);
            }
            foreach (var childGroup in group.Groups)
            {
                list.Add(new
                {
                    GroupName = childGroup.Name,
                    Blocks = GetBlockGroupHierarchy(childGroup)
                });
            }
            return list;
        }
        
        private DeviceItem FindDeviceItem(Project project, string name)
        {
             foreach (Device device in GetAllDevices(project))
             {
                 if (device.Name == name) 
                 {
                     // Deep search for a DeviceItem with this name
                     // Some devices have the software container on the root device item, some on a child.
                 }
                 
                 // Deep search for a DeviceItem with this name
                 var found = FindDeviceItemRecursive(device.DeviceItems, name);
                 if (found != null) return found;
             }
             return null;
        }

        private DeviceItem FindDeviceItemRecursive(DeviceItemComposition items, string name)
        {
             foreach (DeviceItem item in items)
             {
                 if (item.Name == name) return item;
                 var found = FindDeviceItemRecursive(item.DeviceItems, name);
                 if (found != null) return found;
             }
             return null;
        }

        public string ListPlcTags(string deviceName)
        {
            if (_currentProject == null) return "[]";
            
            DeviceItem plcItem = FindDeviceItem(_currentProject, deviceName);
            if (plcItem == null) return JsonConvert.SerializeObject(new { Error = $"Device '{deviceName}' not found." });
            
            var softwareContainer = plcItem.GetService<Siemens.Engineering.HW.Features.SoftwareContainer>();
            if (softwareContainer == null || softwareContainer.Software == null)
            {
                 return JsonConvert.SerializeObject(new { Error = $"Device '{deviceName}' does not contain software." });
            }
            
            var software = softwareContainer.Software as PlcSoftware;
            if (software == null) return JsonConvert.SerializeObject(new { Error = $"Device '{deviceName}' software is not PlcSoftware." });

            var result = new Dictionary<string, object>
            {
                { "Device", deviceName },
                { "Tags", GetTagTableGroupHierarchy(software.TagTableGroup) }
            };
            
            return JsonConvert.SerializeObject(result, Formatting.Indented);
        }

        public string ListConnectedHardware(string deviceName)
        {
            // Use Console.Error.WriteLine for logs to avoid breaking MCP JSON-RPC protocol on stdout
            void LogDebug(string msg) { Console.Error.WriteLine($"DEBUG_HW: {msg}"); }

            LogDebug("--- ListConnectedHardware Started ---");
            LogDebug($"Targeting device: {(string.IsNullOrEmpty(deviceName) ? "ALL" : deviceName)}");

            if (_currentProject == null)
            {
                LogDebug("currentProject is NULL. Returning empty.");
                return "[]";
            }

            // 1. Identify Target PLCs (Controllers)
            var potentialControllers = new List<DeviceItem>();
            var allDeviceItems = new List<DeviceItem>();

            // Helper to gather all items
            void CollectItems(DeviceItemComposition items, List<DeviceItem> list)
            {
                foreach(var item in items)
                {
                    list.Add(item);
                    CollectItems(item.DeviceItems, list);
                }
            }

            foreach (var d in GetAllDevices(_currentProject))
            {
                LogDebug($"Root Device Scan: {d.Name}");
                CollectItems(d.DeviceItems, allDeviceItems);
            }

            if (string.IsNullOrEmpty(deviceName))
            {
                potentialControllers.AddRange(allDeviceItems);
            }
            else
            {
                // Filter allDeviceItems for the specific name
                foreach(var item in allDeviceItems)
                {
                    if (item.Name == deviceName) 
                    {
                        potentialControllers.Add(item);
                    }
                }
                
                if (potentialControllers.Count == 0)
                {
                     var found = FindDeviceItem(_currentProject, deviceName);
                     if (found != null)
                     {
                         potentialControllers.Add(found);
                         CollectItems(found.DeviceItems, potentialControllers);
                     }
                }
            }

            if (potentialControllers.Count == 0 && !string.IsNullOrEmpty(deviceName)) 
            {
                LogDebug($"Device '{deviceName}' not found.");
                return JsonConvert.SerializeObject(new { Error = $"Device '{deviceName}' not found." });
            }

            // Helper class to avoid 'dynamic' and Microsoft.CSharp dependency
            var systemMap = new Dictionary<string, HardwareSystem>();
            var resultList = new List<HardwareSystem>();

            // 2. Find IO Systems owned by controllers
            foreach (var item in potentialControllers)
            {
                var netInterface = item.GetService<Siemens.Engineering.HW.Features.NetworkInterface>();
                if (netInterface == null) continue;

                try
                {
                    foreach (var ioController in netInterface.IoControllers)
                    {
                        if (ioController.IoSystem == null) continue;

                        var ioSystem = ioController.IoSystem;
                        string sysName = ioSystem.Name;

                        LogDebug($"Found IO System: '{sysName}' on Controller: '{item.Name}'");

                        if (systemMap.ContainsKey(sysName)) continue;

                        // Get IP/Address safely
                        string address = "";
                        try {
                            if (netInterface.Nodes.Count > 0)
                                address = netInterface.Nodes[0].GetAttribute("Address")?.ToString();
                        } catch {}

                        // Create System Entry
                        var systemObj = new HardwareSystem
                        {
                            ControllerName = item.Name,
                            InterfaceAddress = address,
                            SystemName = sysName,
                            Type = "Profinet",
                            Devices = new List<object>()
                        };

                        systemMap[sysName] = systemObj;
                        resultList.Add(systemObj);
                    }
                }
                catch (Exception ex) { LogDebug($"Error getting IO Controllers: {ex.Message}"); }
            }

            LogDebug($"Total AllDeviceItems to scan: {allDeviceItems.Count}");
            
            // 3. Scan Root devices to find who is connected to these systems
            if (systemMap.Count > 0)
            {
                foreach (var device in GetAllDevices(_currentProject))
                {
                    // DEBUG: If generic name, dump hierarchy to find potential real name
                    if (device.Name.StartsWith("GSD device"))
                    {
                        LogDebug($"DUMPING HIERARCHY FOR: {device.Name}");
                        void Dump(DeviceItemComposition items, string indent)
                        {
                            foreach(var itm in items)
                            {
                                LogDebug($"{indent}- Name: '{itm.Name}', Class: {itm.Classification}");
                                Dump(itm.DeviceItems, indent + "  ");
                            }
                        }
                        Dump(device.DeviceItems, "  ");
                    }

                    var foundInterfaces = new List<Tuple<string, string, string>>(); // Address, SystemName, Classification
                    string bestName = device.Name; // Default to root name

                    void ScanForInterfaces(DeviceItemComposition items) 
                    {
                        foreach(var itm in items) 
                        {
                            var ni = itm.GetService<Siemens.Engineering.HW.Features.NetworkInterface>();
                            if(ni != null) 
                            {
                                foreach(var conn in ni.IoConnectors) 
                                {
                                    if(conn.ConnectedToIoSystem != null) 
                                    {
                                        string addr = "";
                                        try { 
                                            if(ni.Nodes.Count > 0) 
                                                addr = ni.Nodes[0].GetAttribute("Address")?.ToString(); 
                                        } catch {}
                                        foundInterfaces.Add(Tuple.Create(addr, conn.ConnectedToIoSystem.Name, itm.Classification.ToString()));
                                    }
                                }
                            }
                            ScanForInterfaces(itm.DeviceItems);
                        }
                    }

                    ScanForInterfaces(device.DeviceItems);

                    foreach(var info in foundInterfaces) 
                    {
                        string addr = info.Item1;
                        string sysName = info.Item2;
                        
                        if(systemMap.ContainsKey(sysName)) 
                        {
                            var sysObj = systemMap[sysName];
                            
                            // For GSD devices, the root Device.Name is generic (e.g. "GSD device_1")
                            // The user-visible name is on the Head Module (HM) child item.
                            string displayName = device.Name;
                            if (displayName.StartsWith("GSD device"))
                            {
                                string hmName = FindHeadModuleName(device.DeviceItems);
                                if (!string.IsNullOrEmpty(hmName))
                                {
                                    displayName = hmName;
                                }
                            }
                            
                            sysObj.Devices.Add(new 
                            { 
                                Name = displayName,
                                Address = addr, 
                                Type = "IoDevice" 
                            });
                            LogDebug($"MATCH: Device '{displayName}' (Root: {device.Name}) -> System '{sysName}'");
                        }
                    }
                }
            }
            else
            {
                LogDebug("No IO Systems found to map against.");
            }

            return JsonConvert.SerializeObject(resultList, Formatting.Indented);
        }

        private class HardwareSystem
        {
            public string ControllerName { get; set; }
            public string InterfaceAddress { get; set; }
            public string SystemName { get; set; }
            public string Type { get; set; }
            public List<object> Devices { get; set; }
        }
        
        private List<object> GetTagTableGroupHierarchy(PlcTagTableGroup group)
        {
            var list = new List<object>();
            foreach (var table in group.TagTables)
            {
                var tableObj = new
                {
                    TableName = table.Name,
                    Tags = new List<object>()
                };
                
                var tagList = new List<object>();
                foreach (var tag in table.Tags)
                {
                    tagList.Add(new
                    {
                        Name = tag.Name,
                        DataType = tag.DataTypeName,
                        Address = tag.LogicalAddress,
                        Comment = GetFirstComment(tag.Comment)
                    });
                }
                // Using reflection or dynamic to add property to anonymous type is hard, so just creating clean structure
                list.Add(new { Table = table.Name, Tags = tagList });
            }
            foreach (var childGroup in group.Groups)
            {
                list.Add(new
                {
                    GroupName = childGroup.Name,
                    Content = GetTagTableGroupHierarchy(childGroup)
                });
            }
            return list;
        }

        private string GetFirstComment(MultilingualText text)
        {
            if (text == null) return "";
            // Try to find any text item
            foreach (var item in text.Items)
            {
                if (!string.IsNullOrEmpty(item.Text)) return item.Text;
            }
            return "";
        }

        public string ListHmiScreens(string deviceName)
        {
            return "Tool 'get_hmi_screens' is temporarily unavailable due to a build configuration issue.";
            /*
            if (_currentProject == null) return "No project open.";
            
            DeviceItem hmiItem = FindDeviceItem(_currentProject, deviceName);
            // If the user provided the top-level device name (e.g. "HMI_1"), we might need to drill down to the item with HmiTarget.
            // Often HmiTarget is on the same item, or a child.
            
            if (hmiItem == null) return $"Device '{deviceName}' not found.";
            
            // Try to get HmiTarget directly
            Siemens.Engineering.Hmi.HmiTarget hmiTarget = hmiItem.GetService<Siemens.Engineering.Hmi.HmiTarget>();
            
            // If not found, search children
            if (hmiTarget == null)
            {
                hmiTarget = FindHmiTargetRecursive(hmiItem);
            }
            
            if (hmiTarget == null) return $"Device '{deviceName}' (or its children) is not an HMI Target.";

            var result = new System.Text.StringBuilder();
            result.AppendLine($"HMI Screens in {deviceName}:");
            
            ListScreenFolder(hmiTarget.ScreenFolder, result, "");
            
            return result.ToString();
            */
        }
        
        /*
        private Siemens.Engineering.Hmi.HmiTarget FindHmiTargetRecursive(DeviceItem item)
        {
            var target = item.GetService<Siemens.Engineering.Hmi.HmiTarget>();
            if (target != null) return target;
            
            foreach(var child in item.DeviceItems)
            {
                target = FindHmiTargetRecursive(child);
                if (target != null) return target;
            }
            return null;
        }
        */

        private void ListScreenFolder(ScreenFolder folder, System.Text.StringBuilder sb, string indent)
        {
            foreach (var screen in folder.Screens)
            {
                sb.AppendLine($"{indent}- [Screen] {screen.Name}");
            }
            foreach (var childFolder in folder.Folders)
            {
                sb.AppendLine($"{indent}[Folder] {childFolder.Name}");
                ListScreenFolder(childFolder, sb, indent + "  ");
            }
        }

        public string AddHardwareDevice(string typeIdentifier, string deviceName, string version)
        {
            if (_currentProject == null) return "No project open.";
            try
            {
                // typeIdentifier is usually "OrderNumber:6ES7..."
                // For PLCs, we add to the DeviceComposition.
                Device device = _currentProject.Devices.CreateWithItem(typeIdentifier, deviceName, deviceName);
                return $"Successfully added hardware device: {deviceName} ({typeIdentifier})";
            }
            catch (Exception ex)
            {
                return $"Error adding hardware device: {ex.Message}";
            }
        }

        public string ImportPlcBlock(string deviceName, string xmlPath)
        {
            if (_currentProject == null) return "No project open.";
            try
            {
                DeviceItem plcItem = FindDeviceItem(_currentProject, deviceName);
                if (plcItem == null) return $"Device '{deviceName}' not found.";

                var softwareContainer = plcItem.GetService<Siemens.Engineering.HW.Features.SoftwareContainer>();
                if (softwareContainer == null || softwareContainer.Software == null)
                    return $"Device '{deviceName}' does not contain software.";

                var software = softwareContainer.Software as PlcSoftware;
                if (software == null) return $"Device '{deviceName}' software is not PlcSoftware.";

                FileInfo xmlFile = new FileInfo(xmlPath);
                if (!xmlFile.Exists) return $"XML file not found: {xmlPath}";

                PlcBlockComposition blocks = software.BlockGroup.Blocks;
                IList<PlcBlock> importedBlocks = blocks.Import(xmlFile, ImportOptions.None);
                
                return $"Successfully imported {importedBlocks.Count} block(s) to {deviceName}.";
            }
            catch (Exception ex)
            {
                return $"Error importing PLC block: {ex.Message}";
            }
        }

        public string CreatePlcTagTable(string deviceName, string tableName)
        {
            if (_currentProject == null) return "No project open.";
            try
            {
                DeviceItem plcItem = FindDeviceItem(_currentProject, deviceName);
                if (plcItem == null) return $"Device '{deviceName}' not found.";

                var softwareContainer = plcItem.GetService<Siemens.Engineering.HW.Features.SoftwareContainer>();
                if (softwareContainer == null || softwareContainer.Software == null)
                    return $"Device '{deviceName}' does not contain software.";

                var software = softwareContainer.Software as PlcSoftware;
                if (software == null) return $"Device '{deviceName}' software is not PlcSoftware.";

                PlcTagTableComposition tagTables = software.TagTableGroup.TagTables;
                if (tagTables.Any(t => t.Name == tableName)) return $"Tag table '{tableName}' already exists.";

                tagTables.Create(tableName);
                return $"Successfully created PLC tag table: {tableName}";
            }
            catch (Exception ex)
            {
                return $"Error creating PLC tag table: {ex.Message}";
            }
        }

        public string AddPlcTag(string deviceName, string tableName, string tagName, string dataType, string address)
        {
            if (_currentProject == null) return "No project open.";
            try
            {
                DeviceItem plcItem = FindDeviceItem(_currentProject, deviceName);
                if (plcItem == null) return $"Device '{deviceName}' not found.";

                var softwareContainer = plcItem.GetService<Siemens.Engineering.HW.Features.SoftwareContainer>();
                if (softwareContainer == null || softwareContainer.Software == null)
                    return $"Device '{deviceName}' does not contain software.";

                var software = softwareContainer.Software as PlcSoftware;
                if (software == null) return $"Device '{deviceName}' software is not PlcSoftware.";

                PlcTagTable tagTable = software.TagTableGroup.TagTables.FirstOrDefault(t => t.Name == tableName);
                if (tagTable == null) return $"Tag table '{tableName}' not found.";

                PlcTag tag = tagTable.Tags.Create(tagName);
                tag.DataTypeName = dataType;
                if (!string.IsNullOrEmpty(address))
                {
                    tag.LogicalAddress = address;
                }

                return $"Successfully added tag '{tagName}' to table '{tableName}'.";
            }
            catch (Exception ex)
            {
                return $"Error adding PLC tag: {ex.Message}";
            }
        }

        public void Dispose()

        {
            _tiaPortal?.Dispose();
        }

        private string GetDeviceCategory(string typeIdentifier)
        {
            if (string.IsNullOrEmpty(typeIdentifier)) return "Unknown";

            string lowerIdentifier = typeIdentifier.ToLower();

            if (lowerIdentifier.Contains("cpu") || lowerIdentifier.Contains("plc"))
            {
                return "PLC";
            }
            if (lowerIdentifier.Contains("hmi") || lowerIdentifier.Contains("panel") || lowerIdentifier.Contains("comfort_panel"))
            {
                return "HMI";
            }
            if (lowerIdentifier.Contains("pc_station") || lowerIdentifier.Contains("pc"))
            {
                return "PC Station";
            }
            if (lowerIdentifier.Contains("rt") || lowerIdentifier.Contains("runtime"))
            {
                return "HMI Runtime";
            }

            return "Other";
        }
    }
}
