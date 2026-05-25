using System.Collections.Generic;
using Newtonsoft.Json;

namespace TiaMcpServer.Mcp
{
    public class JsonRpcRequest
    {
        public string jsonrpc { get; set; } = "2.0";
        public string method { get; set; } = "";
        [JsonProperty("params")]
        public object parameters { get; set; }
        public object id { get; set; }
    }

    public class JsonRpcResponse
    {
        public string jsonrpc { get; set; } = "2.0";
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public object result { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public object error { get; set; }
        public object id { get; set; }
    }

    public class McpTool
    {
        public string name { get; set; } = "";
        public string description { get; set; } = "";
        public object inputSchema { get; set; } = new object();
    }

    public class CallToolResult
    {
        public List<Content> content { get; set; } = new List<Content>();
        public bool isError { get; set; }
    }

    public class Content
    {
        public string type { get; set; } = "text";
        public string text { get; set; } = "";
    }

    public class ServerCapabilities
    {
        public Dictionary<string, object> tools { get; set; } = new Dictionary<string, object>();
    }

    public class InitializeResult
    {
        public string protocolVersion { get; set; } = "2024-11-05";
        public ServerCapabilities capabilities { get; set; } = new ServerCapabilities();
        public ServerInfo serverInfo { get; set; } = new ServerInfo();
    }

    public class ServerInfo
    {
        public string name { get; set; } = "TiaMcpServer";
        public string version { get; set; } = "1.0.0";
    }
}
