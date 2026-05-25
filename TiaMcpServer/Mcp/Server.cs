#nullable disable
#nullable disable
using System.IO;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TiaMcpServer.Tia;

namespace TiaMcpServer.Mcp
{
    public class Server : IDisposable
    {
        private TiaManager _tiaManager;

        private TiaManager GetTiaManager()
        {
            if (_tiaManager == null)
            {
                _tiaManager = new TiaManager();
            }
            return _tiaManager;
        }

        public Server()
        {
            // Defer creation of TiaManager until a tool is called or connect_tia is used
        }

        public void Run()
        {
            Log("Server started.");
            while (true)
            {
                try
                {
                    string line = Console.ReadLine();
                    if (line == null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    
                    Log($"Received: {line}");

                    JsonRpcRequest request = null;
                    try
                    {
                        request = JsonConvert.DeserializeObject<JsonRpcRequest>(line);
                    }
                    catch (Exception ex)
                    {
                        Log($"JSON Parse Error: {ex.Message}");
                        continue;
                    }

                    if (request == null) continue;

                    object responseResult = null;
                    object error = null;

                    try
                    {
                        switch (request.method)
                        {
                            case "initialize":
                                responseResult = new InitializeResult();
                                break;
                            case "notifications/initialized":
                                break;
                            case "tools/list":
                                responseResult = GetToolsList();
                                break;
                            case "tools/call":
                                responseResult = HandleToolCall(request.parameters);
                                break;
                            default:
                                if (request.id != null)
                                {
                                    error = new { code = -32601, message = $"Method not found: {request.method}" };
                                }
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Execution Error: {ex.Message}\n{ex.StackTrace}");
                        error = new { code = -32000, message = ex.Message };
                    }

                    if (request.id != null)
                    {
                        var response = new JsonRpcResponse
                        {
                            id = request.id,
                            result = responseResult,
                            error = error
                        };
                        string jsonResponse = JsonConvert.SerializeObject(response);
                        Log($"Sending: {jsonResponse}");
                        Console.WriteLine(jsonResponse);
                        Console.Out.Flush();
                    }
                }
                catch (Exception ex)
                {
                    Log($"Critical Loop Error: {ex.Message}");
                    break;
                }
            }
        }
        
        private void Log(string message)
        {
            try
            {
                File.AppendAllText("server_debug.log", $"[{DateTime.Now:HH:mm:ss}] {message}\n");
            }
            catch {}
        }

        private object GetToolsList() 
        {
             return new
             {
                 tools = new List<McpTool>
                 {
                     new McpTool { name = "connect_tia", description = "ONLY connects to TIA Portal. Does NOT open projects or list anything. Use this ONLY when explicitly asked to connect.", inputSchema = new { type = "object", properties = new { version = new { type = "string", description = "TIA Portal version (e.g., V17, V18, V19, V20). Default is V20." } } } },
                     new McpTool { name = "list_projects", description = "Lists projects", inputSchema = new { type = "object", properties = new { path = new { type = "string" } }, required = new[] { "path" } } },
                     new McpTool { name = "open_project", description = "Opens project", inputSchema = new { type = "object", properties = new { path = new { type = "string" } }, required = new[] { "path" } } },
                     new McpTool { name = "close_project", description = "Closes project", inputSchema = new { type = "object", properties = new { } } },
                     new McpTool { name = "get_project_info", description = "Gets project info", inputSchema = new { type = "object", properties = new { } } },
                     new McpTool { name = "list_devices", description = "Lists devices (JSON)", inputSchema = new { type = "object", properties = new { } } },
                     new McpTool { name = "list_networks", description = "Lists networks", inputSchema = new { type = "object", properties = new { } } },
                     new McpTool { name = "list_plc_blocks", description = "Lists PLC blocks as (JSON)", inputSchema = new { type = "object", properties = new { device_name = new { type = "string" } }, required = new[] { "device_name" } } },
                     new McpTool { name = "get_plc_tags", description = "Gets PLC tags (JSON)", inputSchema = new { type = "object", properties = new { device_name = new { type = "string" } }, required = new[] { "device_name" } } },
                     new McpTool { name = "get_hmi_screens", description = "Gets HMI screens (Disabled)", inputSchema = new { type = "object", properties = new { device_name = new { type = "string" } }, required = new[] { "device_name" } } },
                      new McpTool { name = "list_connected_hardware", description = "Lists hardware connected via PROFINET/PROFIBUS IO systems", inputSchema = new { type = "object", properties = new { device_name = new { type = "string", description = "Name of the PLC/Station to scan. Empty scans all." } } } },
                      new McpTool { name = "add_hardware_device", description = "Adds a new hardware device (PLC, etc) using its MLFB/Article Number", inputSchema = new { type = "object", properties = new { type_identifier = new { type = "string", description = "e.g. OrderNumber:6ES7 511-1AK02-0AB0/V2.8" }, device_name = new { type = "string" }, version = new { type = "string" } }, required = new[] { "type_identifier", "device_name" } } },
                      new McpTool { name = "import_plc_block", description = "Imports a PLC block from an XML file", inputSchema = new { type = "object", properties = new { device_name = new { type = "string" }, xml_path = new { type = "string" } }, required = new[] { "device_name", "xml_path" } } },
                      new McpTool { name = "create_plc_tag_table", description = "Creates a new PLC tag table", inputSchema = new { type = "object", properties = new { device_name = new { type = "string" }, table_name = new { type = "string" } }, required = new[] { "device_name", "table_name" } } },
                      new McpTool { name = "add_plc_tag", description = "Adds a tag to a PLC tag table", inputSchema = new { type = "object", properties = new { device_name = new { type = "string" }, table_name = new { type = "string" }, tag_name = new { type = "string" }, data_type = new { type = "string" }, address = new { type = "string" } }, required = new[] { "device_name", "table_name", "tag_name", "data_type" } } }
                 }
             };
        }

        private CallToolResult HandleToolCall(object parameters)
        {
            if (parameters is JObject jobj)
            {
                if (jobj.TryGetValue("name", out var nameToken))
                {
                    string toolName = nameToken.ToString();
                    JObject argsObj = null;
                    if (jobj.TryGetValue("arguments", out var argsToken))
                    {
                        argsObj = argsToken as JObject;
                    }

                    string resultText = "";
                    try
                    {
                        switch (toolName)
                        {
                            case "connect_tia":
                                string connectVersion = GetStringArg(argsObj, "version");
                                // Update path BEFORE creating manager if possible
                                if (!string.IsNullOrEmpty(connectVersion))
                                {
                                    Program.UpdateTiaPath(connectVersion);
                                }
                                resultText = GetTiaManager().Connect(null);
                                break;
                            case "list_projects":
                                string listPath = GetStringArg(argsObj, "path");
                                resultText = GetTiaManager().ListProjects(listPath);
                                break;
                            case "open_project":
                                string openPath = GetStringArg(argsObj, "path");
                                resultText = GetTiaManager().OpenProject(openPath);
                                break;
                            case "close_project":
                                resultText = GetTiaManager().CloseProject();
                                break;
                            case "get_project_info":
                                resultText = GetTiaManager().GetProjectInfo();
                                break;
                            case "list_devices":
                                resultText = GetTiaManager().ListDevices();
                                break;
                            case "list_networks":
                                resultText = GetTiaManager().ListNetworks();
                                break;
                            case "list_plc_blocks":
                                string deviceName = GetStringArg(argsObj, "device_name");
                                resultText = GetTiaManager().ListPlcBlocks(deviceName);
                                break;
                            case "get_plc_tags":
                                string tagDevNameInfo = GetStringArg(argsObj, "device_name");
                                resultText = GetTiaManager().ListPlcTags(tagDevNameInfo);
                                break;

                            case "get_hmi_screens":
                                string hmiName = GetStringArg(argsObj, "device_name");
                                resultText = GetTiaManager().ListHmiScreens(hmiName);
                                break;
                            case "list_connected_hardware":
                                string hwDeviceName = GetStringArg(argsObj, "device_name");
                                resultText = GetTiaManager().ListConnectedHardware(hwDeviceName);
                                break;
                            case "add_hardware_device":
                                string typeId = GetStringArg(argsObj, "type_identifier");
                                string devName = GetStringArg(argsObj, "device_name");
                                string ver = GetStringArg(argsObj, "version");
                                resultText = GetTiaManager().AddHardwareDevice(typeId, devName, ver);
                                break;
                            case "import_plc_block":
                                string impDevName = GetStringArg(argsObj, "device_name");
                                string xmlPath = GetStringArg(argsObj, "xml_path");
                                resultText = GetTiaManager().ImportPlcBlock(impDevName, xmlPath);
                                break;
                            case "create_plc_tag_table":
                                string ctDevName = GetStringArg(argsObj, "device_name");
                                string ctTableName = GetStringArg(argsObj, "table_name");
                                resultText = GetTiaManager().CreatePlcTagTable(ctDevName, ctTableName);
                                break;
                            case "add_plc_tag":
                                string tagDevName = GetStringArg(argsObj, "device_name");
                                string tagTableName = GetStringArg(argsObj, "table_name");
                                string tagName = GetStringArg(argsObj, "tag_name");
                                string dataType = GetStringArg(argsObj, "data_type");
                                string address = GetStringArg(argsObj, "address");
                                resultText = GetTiaManager().AddPlcTag(tagDevName, tagTableName, tagName, dataType, address);
                                break;

                            default:
                                return new CallToolResult { isError = true, content = new List<Content> { new Content { text = $"Unknown tool: {toolName}" } } };
                        }
                    }
                    catch (Exception ex)
                    {
                        return new CallToolResult { isError = true, content = new List<Content> { new Content { text = $"Error executing tool {toolName}: {ex.Message}" } } };
                    }

                    return new CallToolResult { content = new List<Content> { new Content { text = resultText } } };
                }
            }

            return new CallToolResult { isError = true, content = new List<Content> { new Content { text = "Invalid parameters for tools/call" } } };
        }

        private string GetStringArg(JObject args, string propName)
        {
            if (args != null && args.TryGetValue(propName, out var token))
            {
                return token.ToString();
            }
            return "";
        }

        public void Dispose()
        {
            _tiaManager?.Dispose();
        }
    }
}
