# 🌐 SupportToolKit - Enterprise Network Discovery & IT Operations Suite

[![.NET Version](https://img.shields.io/badge/.NET-10.0%20WPF-512BD4?style=flat&logo=dotnet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D6?style=flat&logo=windows)](https://www.microsoft.com/)
[![Architecture](https://img.shields.io/badge/Architecture-MVVM%20%2B%20Clean%20Code-brightgreen?style=flat)]()
[![Database](https://img.shields.io/badge/Database-SQLite%20%2B%20EF%20Core-003B57?style=flat&logo=sqlite)](https://www.sqlite.org/)
[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

**SupportToolKit** is an enterprise-grade desktop application engineered for network administrators, IT operations engineers, and system support specialists. It combines high-speed asynchronous network discovery, intelligent hardware categorization, real-time interactive vector topology mapping, and a suite of mission-critical IT operation tools into a single, cohesive, modern desktop workstation.

---

## 🚀 Key Highlights & Capabilities

### 🔍 1. High-Speed Multi-Range Network Scanner
- **Concurrent Asynchronous Sweep**: Scans single subnets or multiple CIDR/IP ranges simultaneously using async sockets and ARP cache resolution.
- **Smart Device Auto-Classifier**: Automatically classifies discovered nodes into **Routers, Switches, Servers, Workstations, IP Cameras, Printers, and Access Controllers** based on MAC OUI vendor tables, NetBIOS names, mDNS, open ports, and HTTP service banners.
- **Instant Port & Service Fingerprinting**: Analyzes active TCP ports (HTTP, HTTPS, SSH, RDP, RTSP, SQL, SMB, SNMP, etc.) with latency profiling.
- **Numerical IP Sorting & KPI Dashboards**: Real-time KPI metric cards with live active/offline ratios and multi-criteria category filtering.

---

### 🗺️ 2. Live Interactive Network Topology Map
- **Hierarchical Switch-Endpoint Tree**: Real-time rendering of core routers, distribution switches, and categorized device clusters.
- **Hardware-Aware Layout**: Physical scanned switches appear as top-tier distribution nodes directly beneath the core gateway with connected endpoint nodes.
- **Deep Inspection Side-Drawer**: Click on any node to view real-time MAC address, vendor, open ports, service banners, uptime, and latency.
- **High-Definition Export Engine**:
  - 📸 **PNG Image Export**: High-resolution vector/bitmap snapshot of the network layout.
  - 📄 **JSON Topology Map**: Structured network graph schema for integration with external monitoring systems.
  - 📊 **CSV Device Inventory**: Full spreadsheet inventory containing device names, IPs, MACs, switch associations, and open ports.

---

### 🛠️ 3. Integrated IT Operations Diagnostic Suite
| Tool | Description |
| :--- | :--- |
| **🗄️ SQL Server Connection Tester** | Test connection strings, execute diagnostic SQL queries, evaluate latency, and view tabular query results with error diagnostics. |
| **⚙️ Windows Services Controller** | Enumerate local and remote Windows services, monitor runtime status, and perform Start / Stop / Restart actions with elevated privileges. |
| **🌐 IIS Web Server Monitor** | Inspect local IIS websites and Application Pools, view runtime states, and manage pool lifecycle operations. |
| **📶 Ping Diagnostic & Latency Graph** | Continuous ICMP ping with round-trip latency graphing, packet loss monitoring, and historical stats. |
| **🔌 Port Checker & Banner Grabbing** | Audit common and custom TCP ports with immediate response code and banner acquisition. |
| **📑 Log Collector & Event Auditor** | Collect, filter, and inspect Windows Event Logs, application logs, and system crash diagnostics. |
| **📸 Screen Capture Tool** | Integrated screenshot utility for documenting network anomalies and error dialogs. |

---

### 🎨 4. Modern Fluent UI & Theme Engine
- **Dark, Light & Deep Blue Themes**: Instant dynamic theme switching using XAML Resource Dictionaries and Fluent design aesthetics.
- **Glassmorphism & Responsive Layout**: Clean card elevations, smooth vector typography (Segoe MDL2 Assets), and intuitive navigation.
- **Secure Authentication & RBAC**: Local user management with encrypted passwords and role-based session controls (Admin / Operator).

---

## 🏗️ Architecture & Technology Stack

```
NetworkDiscoveryTool/
├── NetworkDiscoveryTool.Core/         # Domain Models, Interfaces, Enums & Abstractions
├── NetworkDiscoveryTool.Data/         # Entity Framework Core DbContext, SQLite Migrations & Repositories
├── NetworkDiscoveryTool.Helpers/      # OUI Vendor Lookup, ARP Resolver, NetBIOS, IP Parsers
├── NetworkDiscoveryTool.Services/     # Network Scanner, SNMP, Ping, Port Checker, Windows Services API
└── NetworkDiscoveryTool.UI/           # Modern WPF Desktop App (MVVM, XAML Views, ViewModels, Themes)
```

- **Framework**: .NET 10.0 (C# 14)
- **UI Framework**: Windows Presentation Foundation (WPF)
- **MVVM Framework**: `CommunityToolkit.Mvvm` (Source Generators, Observable Properties, Relay Commands)
- **Data Access**: `Microsoft.EntityFrameworkCore.Sqlite` (Code-First)
- **Logging**: `Serilog` with daily rolling file log sinks
- **Packaging**: Self-contained Single-File executable (`win-x64`)

---

## ⚡ Quick Start & Installation

### Option A: Portable Standalone Executable (Recommended)
1. Download `SupportToolKit_v2.5_Portable.zip` from the latest [Releases](../../releases) tab.
2. Extract the `.zip` archive to any directory.
3. Run `SupportToolKit.exe` (No .NET Runtime installation required).
4. *(Optional)* Run `Install_SupportToolKit.bat` to create a Desktop and Start Menu shortcut.

---

- **If You Want The Username And Password Chat With me**
- **My Whatsapp: +201022938245**

---
## 💻 Building from Source

### Prerequisites
- [Windows 10/11 (x64)](https://www.microsoft.com/)
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or Visual Studio 2025+ with WPF workload.

Developed by **Bodda**. Contributions, feature suggestions, and pull requests are welcome!
