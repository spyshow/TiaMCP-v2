# TiaMCP V2 Usage Guide

TiaMCP V2 is an enhanced Model Context Protocol (MCP) server that provides an interface between AI assistants and Siemens TIA Portal via Openness. This guide explains how to configure, connect, and use the server's tools.

## 1. Installation & Configuration

### Prerequisites
* **TIA Portal** (V17, V18, V19, or V20) installed.
* **TIA Portal Openness** installed and configured.
* **Openness Permissions**: Ensure the user running the server is in the `Siemens TIA Openness` user group.

### Adding to Claude Desktop
To use TiaMCP V2 with Claude Desktop, add the following to your `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "tiamcp-v2": {
      "command": "D:\\BackUp\\programing_projects\\TiaMCP V2\\TiaMcpServer\\bin\\Debug\\net8.0-windows\\TiaMcpServer.exe",
      "args": []
    }
  }
}
```
*(Adjust the path to `TiaMcpServer.exe` based on your actual installation directory.)*

## 2. Getting Started

### Connecting to TIA Portal
The first step is always to connect to TIA Portal. TiaMCP V2 supports multiple versions dynamically.

* **Default Connection (V20):**
  ```json
  connect_tia()
  ```

* **Dynamic Version Selection:**
  Use the `version` argument to specify which TIA Portal API to use (V17, V18, V19, or V20).
  ```json
  connect_tia(version: "V18")
  ```

> **Note:** If TIA Portal is already running, the server will attempt to attach to the existing process. Look for a "Grant Access" confirmation dialog in TIA Portal if the connection hangs.

### Project Management
Once connected, you can browse and open projects.

1.  **List Projects**: Find available projects in a directory.
    ```json
    list_projects(path: "C:\\Projects\\TIA")
    ```
2.  **Open Project**: Open a specific project file.
    ```json
    open_project(path: "C:\\Projects\\TIA\\MyProject\\MyProject.ap19")
    ```
3.  **Get Project Info**: View a summary of the open project, including device count and names.
    ```json
    get_project_info()
    ```

## 3. Core Tools & Enhanced Features

### Hardware Visibility
The `list_devices` tool provides a comprehensive view of all hardware in the project, including those in the **Ungrouped Devices** group.

```json
list_devices()
```

### Enhanced Networking
The `list_networks` tool generates detailed summaries of subnets and nodes, including resolved device names, types, and IP addresses.

```json
list_networks()
```

### IO Systems & Connected Hardware
To see which devices are connected to specific IO systems (PROFINET/PROFIBUS):

* **List Connected Hardware**: Scans for IO systems and lists all assigned devices.
  ```json
  list_connected_hardware(device_name: "PLC_1")
  ```
* **Add Hardware**: Add new devices (PLCs, etc.) to the project using their Article Number (MLFB).
  ```json
  add_hardware_device(
    type_identifier: "OrderNumber:6ES7 511-1AK02-0AB0/V2.8",
    device_name: "New_PLC",
    version: "V2.8"
  )
  ```

### PLC Programming & Blocks
The server provides deep visibility into PLC software components.

* **List PLC Blocks**: Lists all blocks (OB, FB, FC, DB) for a specific PLC. Includes block numbers and descriptions.
  ```json
  list_plc_blocks(device_name: "PLC_1")
  ```
* **Get PLC Tags**: Lists all tag tables and their tags, including addresses and comments.
  ```json
  get_plc_tags(device_name: "PLC_1")
  ```
* **Import Blocks**: Import a PLC block from an XML file.
  ```json
  import_plc_block(device_name: "PLC_1", xml_path: "C:\\Exports\\Main_FB.xml")
  ```
* **Tag Management**: Create tag tables and add tags dynamically.
  ```json
  create_plc_tag_table(device_name: "PLC_1", table_name: "IO_Tags")
  add_plc_tag(
    device_name: "PLC_1",
    table_name: "IO_Tags",
    tag_name: "Start_PB",
    data_type: "Bool",
    address: "I0.0"
  )
  ```

## 4. Troubleshooting

*   **"TIA Portal not connected"**: Ensure you have called `connect_tia` before any other project-related tools.
*   **"A project is already open"**: TIA Portal only allows one project to be open at a time. Call `close_project` before opening a different one.
*   **Hanging on `connect_tia`**: Check if TIA Portal has popped up a "Grant Access" dialog. This happens the first time the server attempts to connect to a specific project.
*   **"Device not found"**: Ensure you are using the exact `Name` of the device as shown in `get_project_info` or `list_devices`.
