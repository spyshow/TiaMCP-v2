# TIA Portal MCP Server (v2.0)

Bridge the gap between AI assistants and Industrial Automation. This MCP (Model Context Protocol) server allows AI tools like Gemini CLI, Claude Code, and Codex CLI to interact directly with Siemens TIA Portal via the Openness API.

## 🚀 Introduction

TIA Portal MCP Server provides a standardized interface for AI models to:
*   **Explore**: List projects, devices, networks, and PLC blocks.
*   **Read**: Extract PLC tags, block descriptions, and hardware configurations.
*   **Write**: Create tag tables, add tags, and import PLC blocks from XML.
*   **Configure**: Add hardware devices using MLFB/Article numbers.

This tool is designed for automation engineers who want to leverage AI for code generation, documentation, project auditing, and hardware configuration within the Siemens ecosystem.

---

## 🛠️ Mandatory Setup (Windows & TIA Portal)

Before installing the MCP server, you **must** perform these steps on your Windows machine to allow the Openness API to function.

### 1. Add User to 'Siemens TIA Openness' Group
1.  Open **Computer Management** (Right-click Start -> Computer Management).
2.  Navigate to **System Tools -> Local Users and Groups -> Groups**.
3.  Find the group named **Siemens TIA Openness**.
4.  Double-click it and click **Add...**.
5.  Enter your Windows username and click **OK**.
6.  **Crucial**: You must **Sign Out and Sign In** (or restart) for this change to take effect.

### 2. Enable TIA Portal Openness
Ensure that "TIA Portal Openness" was selected during the installation of TIA Portal. If not, you must run the TIA Portal installer again and select "Modify" to add the Openness component.

### 3. "Grant Access" Popup
The first time an AI tool attempts to connect to a project, TIA Portal will show a security popup asking to "Grant Access". 
*   You must click **Yes** or **Yes to all**.
*   If the AI tool seems to "hang" during the `connect_tia` call, check your taskbar for this hidden TIA Portal dialog.

---

## 📦 Installation

This server requires **.NET Framework 4.8**. The .NET 8.0 version is currently not supported due to Siemens assembly dependencies.

### 1. Download
Download the `TiaMcpServer.exe` (and its dependencies) from the [GitHub Releases](https://github.com/spyshow/TiaMCP-v2/releases) page. Ensure you are using the **net48** version.

### 2. Configure your AI CLI

#### **Gemini CLI**
Add the server to your `settings.json` or `.geminirc`:

```json
{
  "mcpServers": {
    "tiamcp": {
      "command": "C:\\Path\\To\\net48\\TiaMcpServer.exe",
      "args": []
    }
  }
}
```

#### **Claude Code (Anthropic)**
Claude Code typically uses the configuration from Claude Desktop. Add it to:
`%APPDATA%\Claude\claude_desktop_config.json`

```json
{
  "mcpServers": {
    "tiamcp": {
      "command": "C:\\Path\\To\\net48\\TiaMcpServer.exe",
      "args": []
    }
  }
}
```

#### **Codex CLI**
Add to your Codex configuration file:

```toml
[mcp.servers.tiamcp]
command = "C:\\Path\\To\\net48\\TiaMcpServer.exe"
args = []
```

---

## 🧰 Available Tools & Usage Examples

Once installed, you can use the following tools through your AI assistant.

| Tool | Description | Example CLI Instruction |
| :--- | :--- | :--- |
| `connect_tia` | Connects to a running TIA instance or launches a new one. | "Connect to TIA Portal V19" |
| `list_projects` | Lists all `.ap17`, `.ap18`, `.ap19`, etc., files in a folder. | "List all TIA projects in C:\Automation\Projects" |
| `open_project` | Opens a specific project file. | "Open project C:\Projects\Main.ap19" |
| `close_project` | Closes the currently open project. | "Close the current project" |
| `get_project_info` | Shows project name, path, and device summary. | "Give me a summary of the open project" |
| `list_devices` | Returns a JSON hierarchy of all hardware stations. | "What devices are in this project?" |
| `list_networks` | Lists all subnets, IPs, and connected nodes. | "Show me the network configuration and IP addresses" |
| `list_plc_blocks` | Lists all OBs, FBs, FCs, and DBs in a PLC. | "List all PLC blocks for 'PLC_1'" |
| `get_plc_tags` | Exports all tag tables and tags from a PLC. | "Show me all tags in 'PLC_1'" |
| `list_connected_hardware` | Shows IO systems and assigned IO devices. | "What hardware is connected to the PROFINET system of 'PLC_1'?" |
| `add_hardware_device` | Adds a new device via Article Number (MLFB). | "Add a CPU 1511 (6ES7 511-1AK02-0AB0) named 'NewPLC'" |
| `import_plc_block` | Imports a block from an XML file. | "Import the block from C:\Exports\MotorControl.xml into 'PLC_1'" |
| `create_plc_tag_table` | Creates a new empty tag table. | "Create a new tag table called 'SafetyTags' in 'PLC_1'" |
| `add_plc_tag` | Adds a single tag to a specific table. | "Add a Bool tag 'Start_Button' at I0.0 to 'Inputs' table in 'PLC_1'" |

## 💬 Conversational Examples (Try these phrases!)

You don't need to know the tool names. Just talk to your AI assistant naturally:

*   **Project Exploration**: 
    > "List all the TIA projects in my Documents folder and open the one named 'Station_A'."
*   **Hardware Audit**:
    > "What devices are currently in my project, and what are their Article Numbers?"
*   **Network Mapping**:
    > "Show me the IP addresses of all CPUs and tell me which IO devices are connected to them."
*   **PLC Programming**:
    > "Find the PLC named 'Main_Controller' and list all its Function Blocks. Are there any comments explaining what 'FB10' does?"
*   **Tag Management**:
    > "I need to add a new sensor. Create a tag called 'Level_Sensor' with type 'Real' at memory address 'MD20' in the 'Process_Tags' table."
*   **Bulk Operations**:
    > "Import all the XML files from 'C:\Exports\Library' into my PLC and then give me a summary of what was added."

---

## ⚠️ Requirements & Limitations
*   **OS**: Windows 10 or Windows 11.
*   **Software**: TIA Portal V17, V18, V19, or V20 (Professional or Basic).
*   **Framework**: .NET Framework 4.8.
*   **Openness**: TIA Portal Openness must be installed and the user must be in the `Siemens TIA Openness` group.
*   **Concurrency**: Only one TIA Portal project can be opened by the server at a time.
