using System;
using System.Collections.Generic;

namespace NetworkDiscoveryTool.Helpers;

public static class CameraOuiDatabase
{
    public sealed record CameraVendorInfo(string Brand, string CompanyName, string Category);

    private static readonly Dictionary<string, CameraVendorInfo> CameraOuiMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // =====================================================================
        // 1. i-PRO / Panasonic (Security, AI, Traffic & PTZ Cameras)
        // =====================================================================
        { "D42DC5", new CameraVendorInfo("i-PRO", "Panasonic / i-PRO Co., Ltd.", "Security & AI Camera") },
        { "BCC342", new CameraVendorInfo("i-PRO", "Panasonic System Networks", "IP Camera") },
        { "4C364E", new CameraVendorInfo("i-PRO", "Panasonic System Networks", "IP Camera") },
        { "3C6FEA", new CameraVendorInfo("i-PRO", "Panasonic / i-PRO Co., Ltd.", "IP Camera") },
        { "0001D9", new CameraVendorInfo("i-PRO", "Panasonic Corporation", "CCTV / Network Camera") },
        { "00E03C", new CameraVendorInfo("i-PRO", "Panasonic Communications", "Network Camera") },
        { "008045", new CameraVendorInfo("i-PRO", "Panasonic Communications", "Security Camera") },
        { "000B97", new CameraVendorInfo("i-PRO", "Panasonic Corporation", "IP Camera") },
        { "0001E3", new CameraVendorInfo("i-PRO", "Panasonic Corporation", "IP Camera") },
        { "080023", new CameraVendorInfo("i-PRO", "Panasonic Corporation", "IP Camera") },
        { "248000", new CameraVendorInfo("i-PRO", "i-PRO Co., Ltd.", "i-PRO AI Camera") },
        { "382C4A", new CameraVendorInfo("i-PRO", "i-PRO Co., Ltd.", "i-PRO AI Camera") },
        { "B4257A", new CameraVendorInfo("i-PRO", "i-PRO Co., Ltd.", "i-PRO Multi-Sensor Camera") },
        { "E04F43", new CameraVendorInfo("i-PRO", "i-PRO Co., Ltd.", "i-PRO Camera") },
        { "4432C8", new CameraVendorInfo("i-PRO", "i-PRO Co., Ltd.", "i-PRO Camera") },
        { "001EE3", new CameraVendorInfo("i-PRO", "Panasonic Corporation", "IP Camera") },
        { "0024D7", new CameraVendorInfo("i-PRO", "Panasonic Corporation", "IP Camera") },
        { "5C260A", new CameraVendorInfo("i-PRO", "Panasonic Corporation", "IP Camera") },
        { "700514", new CameraVendorInfo("i-PRO", "Panasonic Corporation", "IP Camera") },
        { "847BEA", new CameraVendorInfo("i-PRO", "i-PRO Co., Ltd.", "i-PRO Camera") },
        { "94F6D6", new CameraVendorInfo("i-PRO", "i-PRO Co., Ltd.", "i-PRO Camera") },
        { "D46E5C", new CameraVendorInfo("i-PRO", "i-PRO Co., Ltd.", "i-PRO Camera") },

        // =====================================================================
        // 2. Dahua Technology & Imou (CCTV, Thermal, ANPR, Radar)
        // =====================================================================
        { "24F5A2", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera / NVR") },
        { "3CDFA9", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },
        { "9CA134", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },
        { "E05A9F", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },
        { "E0508B", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },
        { "4C11BF", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },
        { "9002A9", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },
        { "A0BDCD", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },
        { "38AF29", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera / IPC") },
        { "BC325F", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera / IPC") },
        { "B0C554", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera / IPC") },
        { "A0BD1D", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera / IPC") },
        { "F4E2C6", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera / IPC") },
        { "001A07", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },
        { "14A78B", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },
        { "2C2617", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },
        { "3CEF8C", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },
        { "442C05", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },
        { "54C415", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },
        { "70B14E", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },
        { "84D679", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },
        { "9C1463", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },
        { "C03987", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },
        { "D8B04C", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },
        { "1C5216", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },
        { "54E43A", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },
        { "B44326", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },
        { "C074AD", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },
        { "C8A849", new CameraVendorInfo("Dahua", "Zhejiang Dahua Technology Co., Ltd.", "IP Camera") },

        // =====================================================================
        // 3. ARH (Adaptive Recognition / Carmen / ParkIT / FreewayCAM / ARH Informatics)
        // =====================================================================
        { "001E58", new CameraVendorInfo("ARH / LPR", "Adaptive Recognition Kft. (ARH)", "LPR / Traffic ANPR Camera") },
        { "70B3D5", new CameraVendorInfo("ARH / LPR", "Adaptive Recognition Kft. (ARH)", "Carmen FreeWay / MicroCAM") },
        { "0050C2", new CameraVendorInfo("ARH / LPR", "ARH Informatics / IEEE Registration", "ARH Traffic Sensor / LPR") },
        { "0001C0", new CameraVendorInfo("ARH / LPR", "Adaptive Recognition Embedded", "ANPR / Industrial Camera") },
        { "0004A3", new CameraVendorInfo("ARH / LPR", "ARH Hardware Partner", "Traffic / Radar Controller") },
        { "0080E1", new CameraVendorInfo("ARH / LPR", "ARH Sensor Controller", "MicroCAM / LPR Unit") },
        { "000E0C", new CameraVendorInfo("ARH / LPR", "ARH Industrial Controller", "ParkIT / LPR Camera") },

        // =====================================================================
        // 4. Radar, Traffic & Speed Enforcement Cameras
        // =====================================================================
        { "000732", new CameraVendorInfo("Radar / Traffic", "Vitronic GmbH (PoliScan Speed / Red Light)", "LIDAR / Radar Camera") },
        { "002085", new CameraVendorInfo("Radar / Traffic", "Jenoptik Robot GmbH (Multanova / TraffiStar)", "Speed Radar / LPR Camera") },
        { "000BAB", new CameraVendorInfo("Radar / Traffic", "Jenoptik Optical Systems", "Traffic Surveillance Sensor") },
        { "000D32", new CameraVendorInfo("Radar / Traffic", "Sensys Traffic AB", "Traffic Radar / Speed Camera") },
        { "0016B5", new CameraVendorInfo("Radar / Traffic", "Redflex Traffic Systems", "Traffic Enforcement Camera") },
        { "00224D", new CameraVendorInfo("Radar / Traffic", "IDETEC / Speed Radar", "Doppler Radar Sensor") },

        // =====================================================================
        // 5. Axis Communications (Security, LPR & PTZ)
        // =====================================================================
        { "B8A44F", new CameraVendorInfo("Axis", "Axis Communications AB", "Network Camera") },
        { "00508A", new CameraVendorInfo("Axis", "Axis Communications AB", "Network Camera") },
        { "00408C", new CameraVendorInfo("Axis", "Axis Communications AB", "Network Camera") },
        { "ACCC8E", new CameraVendorInfo("Axis", "Axis Communications AB", "Network Camera") },

        // =====================================================================
        // 6. Hikvision (CCTV, ANPR, Traffic Radar)
        // =====================================================================
        { "B898F7", new CameraVendorInfo("Hikvision", "Hangzhou Hikvision Digital Technology", "IP Camera / ANPR") },
        { "441EA1", new CameraVendorInfo("Hikvision", "Hangzhou Hikvision Digital Technology", "IP Camera") },
        { "A4B1C1", new CameraVendorInfo("Hikvision", "Hangzhou Hikvision Digital Technology", "IP Camera") },
        { "3822D6", new CameraVendorInfo("Hikvision", "Hangzhou Hikvision Digital Technology", "IP Camera") },
        { "C056E3", new CameraVendorInfo("Hikvision", "Hangzhou Hikvision Digital Technology", "IP Camera") },
        { "101248", new CameraVendorInfo("Hikvision", "Hangzhou Hikvision Digital Technology", "IP Camera") },
        { "686DBC", new CameraVendorInfo("Hikvision", "Hangzhou Hikvision Digital Technology", "IP Camera") },
        { "E4246C", new CameraVendorInfo("Hikvision", "Hangzhou Hikvision Digital Technology", "IP Camera") },
        { "1C61B4", new CameraVendorInfo("Hikvision", "Hangzhou Hikvision Digital Technology", "IP Camera") },
        { "F832E4", new CameraVendorInfo("Hikvision", "Hangzhou Hikvision Digital Technology", "IP Camera") },
        { "A41437", new CameraVendorInfo("Hikvision", "Hangzhou Hikvision Digital Technology", "IP Camera") },
        { "600308", new CameraVendorInfo("Hikvision", "Hangzhou Hikvision Digital Technology", "IP Camera") },
        { "48EA63", new CameraVendorInfo("Hikvision", "Hangzhou Hikvision Digital Technology", "IP Camera") },
        { "4419B6", new CameraVendorInfo("Hikvision", "Hangzhou Hikvision Digital Technology", "IP Camera") },
        { "C42F90", new CameraVendorInfo("Hikvision", "Hangzhou Hikvision Digital Technology", "IP Camera") },
        { "BC5436", new CameraVendorInfo("Hikvision", "Hangzhou Hikvision Digital Technology", "IP Camera") },
        { "2857BE", new CameraVendorInfo("Hikvision", "Hangzhou Hikvision Digital Technology", "IP Camera") },
        { "000F7D", new CameraVendorInfo("Hikvision", "Hangzhou Hikvision Digital Technology", "IP Camera") },
        { "B05ADA", new CameraVendorInfo("Hikvision", "Hangzhou Hikvision Digital Technology", "IP Camera") },
        { "D896E0", new CameraVendorInfo("Hikvision", "Hangzhou Hikvision Digital Technology", "IP Camera") },

        // =====================================================================
        // 7. Uniview / Hanwha / Bosch / Mobotix / Vivotek
        // =====================================================================
        { "001217", new CameraVendorInfo("Uniview", "Zhejiang Uniview Technologies", "IP Camera") },
        { "00306E", new CameraVendorInfo("Uniview", "Zhejiang Uniview Technologies", "IP Camera") },
        { "44334C", new CameraVendorInfo("Uniview", "Zhejiang Uniview Technologies", "IP Camera") },
        { "E0D31A", new CameraVendorInfo("Uniview", "Zhejiang Uniview Technologies", "IP Camera") },
        { "2CAB00", new CameraVendorInfo("Uniview", "Zhejiang Uniview Technologies", "IP Camera") },
        { "000918", new CameraVendorInfo("Hanwha", "Hanwha Techwin / Samsung Techwin", "IP Camera") },
        { "00166C", new CameraVendorInfo("Hanwha", "Hanwha Techwin / Samsung", "IP Camera") },
        { "F45C89", new CameraVendorInfo("Hanwha", "Hanwha Vision", "IP Camera") },
        { "E43022", new CameraVendorInfo("Hanwha", "Hanwha Vision", "IP Camera") },
        { "00036B", new CameraVendorInfo("Bosch", "Bosch Security Systems", "IP Camera / Video") },
        { "001E8C", new CameraVendorInfo("Bosch", "Bosch Security Systems", "IP Camera / Video") },
        { "A0086F", new CameraVendorInfo("Bosch", "Bosch Security Systems", "IP Camera / Video") },
        { "00075F", new CameraVendorInfo("Bosch", "Bosch Security Systems", "IP Camera / Video") },
        { "0012B7", new CameraVendorInfo("Mobotix", "Mobotix AG", "Decentralized IP Camera") },
        { "AC7A4B", new CameraVendorInfo("Vivotek", "Vivotek Inc.", "Network Camera") },
        { "0002D1", new CameraVendorInfo("Vivotek", "Vivotek Inc.", "Network Camera") },
        { "000F44", new CameraVendorInfo("FLIR", "FLIR Systems (Thermal & Security)", "Thermal Camera") }
    };

    public static CameraVendorInfo? Lookup(string? macAddress)
    {
        if (string.IsNullOrWhiteSpace(macAddress)) return null;

        var clean = macAddress.Replace(":", "").Replace("-", "").Trim().ToUpperInvariant();
        if (clean.Length < 6) return null;

        var prefix = clean.Substring(0, 6);
        return CameraOuiMap.GetValueOrDefault(prefix);
    }

    public static bool IsKnownCamera(string? macAddress) => Lookup(macAddress) != null;

    /// <summary>
    /// Checks if announcement text, model, or server headers indicate a PC, Workstation, or VMS server software rather than physical camera hardware.
    /// </summary>
    public static bool IsPcOrServerText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var lower = text.ToLowerInvariant();

        return lower.Contains("win32nt") ||
               lower.Contains("windows nt") ||
               lower.Contains("windows_nt") ||
               lower.Contains("microsoft") ||
               lower.Contains("axis communications server") ||
               lower.Contains("axis camera station") ||
               lower.Contains("camera station") ||
               lower.Contains("vms server") ||
               lower.Contains("recording server") ||
               lower.Contains("media server") ||
               lower.Contains("milestone") ||
               lower.Contains("xprotect") ||
               lower.Contains("genetec") ||
               lower.Contains("workstation") ||
               lower.Contains("desktop-") ||
               lower.Contains("desktop ") ||
               lower.Contains("server/1.") ||
               lower.Contains("server/2.") ||
               lower.Contains("server/3.") ||
               lower.Contains("blue iris") ||
               lower.Contains("ivms-4200") ||
               lower.Contains("hikcentral") ||
               lower.Contains("smartpss");
    }

    /// <summary>
    /// Known OUI prefixes for PC, Server, and Hypervisor hardware (e.g. Dell, HP, Lenovo, VMware, VirtualBox).
    /// Physical cameras will never use these network cards.
    /// </summary>
    private static readonly HashSet<string> PcServerOuiPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Dell Inc.
        "3417EB", "001422", "1866DA", "B82A72", "D4BED9", "F8BC12", "74867A", "989096",
        "141877", "24B6FD", "484D7E", "705A0F", "B083FE", "E4F004", "F0D5BF",
        "001E4F", "00219B", "002219", "0023AE", "0024E8", "0026B9", "00B0D0", "109836",
        "28F10E", "308D99", "44A842", "5CF9DD", "782BCB", "8C04BA", "90B11C", "A41F72",
        "B49691", "C81F66", "D09466", "ECF4BB",
        // HP / Hewlett Packard
        "001E0B", "00215A", "002264", "00237D", "002481", "0025B3", "002655", "3C5282",
        "A0D3C1", "D89D67", "ECB1D7", "F430B9", "2C44FD", "B439D6", "000E7F", "001871",
        "001B78", "001C23", "101F74", "18A905", "28924A", "3085A9", "3CD92B", "40A8F0",
        "480F2A", "645106", "78E7D1", "8851FB", "9CEBE8", "A45D36", "AC162D", "C8D3FF",
        // Lenovo
        "005907", "107B44", "28D244", "408D5C", "54EE75", "6CD447", "70723C", "80000B",
        "98E743", "AC7289", "C85B76",
        // Virtualization (VMware, VirtualBox, Hyper-V, QEMU)
        "000569", "000C29", "001C14", "005056", // VMware
        "080027",                               // VirtualBox
        "00155D",                               // Microsoft Hyper-V
        "525400"                                // QEMU / KVM
    };

    /// <summary>
    /// Checks if a MAC OUI prefix belongs to PC / Server motherboard manufacturers.
    /// </summary>
    public static bool IsPcOrServerMac(string? macAddress)
    {
        if (string.IsNullOrWhiteSpace(macAddress)) return false;
        var clean = macAddress.Replace(":", "").Replace("-", "").Trim().ToUpperInvariant();
        if (clean.Length < 6) return false;
        var prefix = clean.Substring(0, 6);

        // Safety: If it's explicitly in the camera database, it's a camera
        if (CameraOuiMap.ContainsKey(prefix)) return false;

        return PcServerOuiPrefixes.Contains(prefix);
    }

    public static string DetectBrandFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Generic ONVIF";

        if (IsPcOrServerText(text))
            return "PC / Server";

        var lower = text.ToLowerInvariant();

        if (lower.Contains("ipro") || lower.Contains("i-pro") || lower.Contains("panasonic") || lower.Contains("wv-"))
            return "i-PRO";

        if (lower.Contains("dahua") || lower.Contains("dh-") || lower.Contains("imou") || lower.Contains("lechange"))
            return "Dahua";

        if (lower.Contains("arh") || lower.Contains("carmen") || lower.Contains("freeway") || lower.Contains("parkit") || lower.Contains("microcam") || lower.Contains("anpr") || lower.Contains("lpr"))
            return "ARH / LPR";

        if (lower.Contains("radar") || lower.Contains("vitronic") || lower.Contains("poliscan") || lower.Contains("traffistar") || lower.Contains("jenoptik") || lower.Contains("sensys"))
            return "Radar / Traffic";

        if (lower.Contains("hikvision") || lower.Contains("ezviz"))
            return "Hikvision";

        if (lower.Contains("axis"))
            return "Axis";

        if (lower.Contains("uniview") || lower.Contains("unv"))
            return "Uniview";

        if (lower.Contains("hanwha") || lower.Contains("samsung"))
            return "Hanwha";

        if (lower.Contains("bosch"))
            return "Bosch";

        return "Generic ONVIF";
    }
}

