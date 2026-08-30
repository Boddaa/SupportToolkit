using System;
using System.Collections.Generic;
using System.Linq;

namespace NetworkDiscoveryTool.Helpers;

public static class OuiLookup
{
    private static readonly Dictionary<string, string> OuiMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // === Top PC, Servers & Workstations ===
        { "54BF64", "Dell Inc." }, { "B8CA3A", "Dell Inc." }, { "001422", "Dell Inc." }, { "1866DA", "Dell Inc." },
        { "5CF9DD", "Dell Inc." }, { "7486E2", "Dell Inc." }, { "F01FAF", "Dell Inc." }, { "00188B", "Dell Inc." },
        { "001150", "Dell Inc." }, { "00219B", "Dell Inc." }, { "0001E6", "Dell Inc." }, { "00144F", "Dell Inc." },
        { "F8DB88", "Dell Inc." }, { "B083FE", "Dell Inc." }, { "D4BED9", "Dell Inc." }, { "E454E8", "Dell Inc." },
        { "90B11C", "Dell Inc." }, { "3417EB", "Dell Inc." }, { "F48E38", "Dell Inc." }, { "14FEB5", "Dell Inc." },
        { "180373", "Dell Inc." }, { "ECF4BB", "Dell Inc." }, { "A44CC8", "Dell Inc." }, { "601895", "Dell Inc." },
        { "B42E99", "Giga-Byte Technology" }, { "18C04D", "Giga-Byte Technology" }, { "E0D55E", "Giga-Byte Technology" },
        { "D8BBC1", "Micro-Star (MSI)" }, { "003067", "Micro-Star (MSI)" }, { "001617", "Micro-Star (MSI)" },
        { "00E04C", "Realtek Semiconductor" }, { "54E6FC", "Realtek Semiconductor" }, { "704D7B", "Realtek Semiconductor" },
        { "68EDA4", "Shenzhen Seavo Technology" }, { "585B69", "Wistron InfoComm" }, { "A0AD9F", "Compal Information" },
        { "94659C", "Intel Corp." }, { "001500", "Intel Corp." }, { "001B21", "Intel Corp." },
        { "001E67", "Intel Corp." }, { "00266B", "Intel Corp." }, { "000393", "Intel Corp." },
        { "00155D", "Microsoft Hyper-V" }, { "001A11", "Microsoft" }, { "002248", "Microsoft" }, { "DC5360", "Microsoft" },
        { "005056", "VMware, Inc." }, { "000C29", "VMware, Inc." }, { "001C14", "VMware, Inc." }, { "0025C3", "VMware, Inc." },
        { "000569", "VMware, Inc." },
        { "001F04", "Lenovo" }, { "F0DCFC", "Lenovo" }, { "00216B", "Lenovo" }, { "645106", "Lenovo" },
        { "482C6A", "Lenovo" }, { "507B9D", "Lenovo" }, { "8C1645", "Lenovo" }, { "E86A64", "Lenovo" },
        { "A01D48", "Hewlett Packard" }, { "0090A6", "Hewlett Packard" }, { "002481", "Hewlett Packard" },
        { "001A4B", "Hewlett Packard" }, { "0017A4", "Hewlett Packard" }, { "001678", "Hewlett Packard" },
        { "00C0B6", "Hewlett Packard" }, { "B8253F", "Hewlett Packard" }, { "3CD92B", "Hewlett Packard" },
        { "2C44FD", "Hewlett Packard" }, { "9457A5", "Hewlett Packard" }, { "A0481C", "Hewlett Packard" }, { "D85D4C", "Hewlett Packard" },
        { "0455CA", "Acer" }, { "00168F", "Acer" }, { "000FAA", "Asus" }, { "60A4B0", "Asus" },
        { "DCDEAA", "Asus" }, { "3497F6", "Asus" }, { "0060B9", "Fujitsu" }, { "002665", "Toshiba" },

        // === Apple Devices ===
        { "001D4F", "Apple" }, { "0023DF", "Apple" }, { "385FC3", "Apple" }, { "5C969D", "Apple" },
        { "ACBC32", "Apple" }, { "A402B9", "Apple" }, { "846B15", "Apple" }, { "000748", "Apple" },
        { "001451", "Apple" }, { "ACDE48", "Apple" }, { "04D3CF", "Apple" }, { "3408BC", "Apple" },
        { "3C0754", "Apple" }, { "40A6E8", "Apple" }, { "48D705", "Apple" }, { "50BC96", "Apple" },
        { "685B35", "Apple" }, { "70CD60", "Apple" }, { "8C8590", "Apple" }, { "A483E7", "Apple" },
        { "C869CD", "Apple" }, { "E0ACCB", "Apple" }, { "F4F15A", "Apple" },

        // === CCTV, NVR, DVR & IP Cameras ===
        { "D42DC5", "Panasonic i-PRO (Camera)" }, { "BCC342", "Panasonic (Camera)" }, { "4C364E", "Panasonic (Camera)" },
        { "3C6FEA", "Panasonic (Camera)" }, { "0001D9", "Panasonic" }, { "00E03C", "Panasonic" },
        { "B8A44F", "Axis Communications AB" }, { "00508A", "Axis Communications AB" }, { "00408C", "Axis Communications AB" }, { "ACCC8E", "Axis Communications AB" },
        { "B898F7", "Hikvision" }, { "441EA1", "Hikvision" }, { "A4B1C1", "Hikvision" }, { "3822D6", "Hikvision" },
        { "C056E3", "Hikvision" }, { "54C415", "Hikvision" }, { "101248", "Hikvision" }, { "686DBC", "Hikvision" },
        { "E4246C", "Hikvision" }, { "1C61B4", "Hikvision" }, { "F832E4", "Hikvision" }, { "A41437", "Hikvision" },
        { "600308", "Hikvision" }, { "48EA63", "Hikvision" },
        { "24F5A2", "Dahua" }, { "3CDFA9", "Dahua" }, { "9CA134", "Dahua" }, { "E05A9F", "Dahua" },
        { "E0508B", "Dahua" }, { "4C11BF", "Dahua" }, { "9002A9", "Dahua" }, { "A0BDCD", "Dahua" },
        { "080090", "Acti" }, { "00036B", "Bosch" }, { "001E8C", "Bosch" }, { "A0086F", "Bosch" },
        { "AC7A4B", "Vivotek" }, { "0011F6", "GeoVision" }, { "0012B7", "Mobotix" }, { "0011E7", "Pelco" },
        { "0011F5", "Sony" }, { "001217", "Uniview" }, { "00306E", "Uniview" }, { "44334C", "Uniview" },

        // === Network Equipment & Routers ===
        { "58D56E", "D-Link International" }, { "04F021", "D-Link" }, { "001372", "D-Link" }, { "001880", "D-Link" },
        { "00218A", "D-Link" }, { "68DBCA", "D-Link" }, { "1C7EE5", "D-Link" }, { "28285D", "D-Link" }, { "B0C554", "D-Link" },
        { "CC32E5", "TP-Link Technologies" }, { "F86EEE", "TP-Link Technologies" }, { "F84C6F", "TP-Link Technologies" },
        { "14CF92", "TP-Link Technologies" }, { "54A050", "TP-Link Technologies" }, { "C0A0DE", "TP-Link Technologies" },
        { "A854B2", "TP-Link Technologies" }, { "50C7BF", "TP-Link Technologies" }, { "AC84C6", "TP-Link Technologies" },
        { "54EF44", "TP-Link Technologies" }, { "E848B8", "TP-Link Technologies" }, { "D84732", "TP-Link Technologies" },
        { "C4FF1F", "Huawei Technologies" }, { "E472E2", "Huawei Technologies" }, { "001B8C", "Huawei Technologies" },
        { "00259E", "Huawei Technologies" }, { "00E0FC", "Huawei Technologies" }, { "482AE3", "Huawei Technologies" },
        { "D4C9EF", "Huawei Technologies" }, { "18DED7", "Huawei Technologies" }, { "94772B", "Huawei Technologies" },
        { "00037F", "Cisco" }, { "0013C3", "Cisco" }, { "001E13", "Cisco" },
        { "0022BD", "Cisco" }, { "0026CB", "Cisco" }, { "0050B6", "Cisco" }, { "00909C", "Cisco" },
        { "00E0B0", "Cisco" }, { "001E4E", "Cisco" }, { "00195F", "Cisco" }, { "001A2F", "Cisco" },
        { "4C5E0C", "MikroTik" }, { "6400F1", "MikroTik" }, { "6C3B6B", "MikroTik" }, { "488F5A", "MikroTik" },
        { "001F9E", "Ubiquiti" }, { "24A43C", "Ubiquiti" }, { "68D247", "Ubiquiti" }, { "7492A4", "Ubiquiti" },
        { "C0B329", "Netgear" }, { "A021B7", "Netgear" }, { "0011B8", "Netgear" },
        { "000E8B", "Tenda" }, { "C82E47", "Tenda" }, { "502B73", "Tenda" },
        { "009069", "Juniper" }, { "00A0C5", "Fortinet" }, { "00D0A4", "Aruba" },

        // === Access Control, BMS & Specialized Hardware ===
        { "0017FC", "Suprema Inc. (Access Control)" },
        { "00602D", "Alerton Technologies (BMS)" },

        // === Printers & Multifunction ===
        { "9C934E", "Xerox Corporation" }, { "0012A0", "Xerox Corporation" }, { "00801C", "Xerox Corporation" },
        { "000850", "Brother" }, { "0022EA", "Brother" }, { "ACB315", "Brother" },
        { "00248C", "Canon" }, { "001AE2", "Canon" }, { "0016E2", "Canon" },
        { "08003A", "Epson" }, { "001BA1", "Epson" }, { "001EE3", "Epson" },
        { "00E02E", "Lexmark" }, { "001137", "Lexmark" }, { "001360", "Konica Minolta" },
        { "00093B", "Kyocera" }, { "0012C2", "Kyocera" }, { "000512", "Ricoh" },

        // === Smart Home & IoT ===
        { "240AC4", "Espressif" }, { "246F28", "Espressif" }, { "30AEA4", "Espressif" },
        { "3C71BF", "Espressif" }, { "483FDA", "Espressif" }, { "5CCF7F", "Espressif" },
        { "600194", "Espressif" }, { "840D8E", "Espressif" }, { "A4CF12", "Espressif" },
        { "DCA632", "Raspberry Pi" }, { "B827EB", "Raspberry Pi" }, { "E45F01", "Raspberry Pi" },
        { "0025A0", "Samsung" }, { "58A023", "Samsung" }, { "8C5CA0", "Samsung" }, { "34F39B", "Samsung" },
        { "00248A", "LG" }, { "001E10", "LG" }, { "448C52", "Motorola" },

        // === NAS & Storage ===
        { "001132", "Synology" }, { "001056", "Synology" }, { "9003B7", "Synology" },
        { "0015B6", "QNAP" }, { "00508B", "QNAP" }, { "00A037", "Western Digital" },
        { "0014EE", "Western Digital" }, { "000CCD", "Seagate" }, { "00A0B8", "NetApp" },
    };

    public static string Lookup(string? mac, string? hostname = null, IEnumerable<int>? openPorts = null)
    {
        if (!string.IsNullOrWhiteSpace(mac))
        {
            var clean = mac.Replace(":", "").Replace("-", "").Trim().ToUpperInvariant();
            if (clean.Length >= 6)
            {
                var prefix = clean[..6];
                if (OuiMap.TryGetValue(prefix, out var vendor))
                    return vendor;

                // Check for Private / Randomized MAC (bit 1 of 1st byte is set, e.g. x2, x6, xA, xE)
                if (clean.Length >= 2 && Uri.IsHexDigit(clean[1]))
                {
                    int secondNibble = Convert.ToInt32(clean[1].ToString(), 16);
                    if ((secondNibble & 0x2) != 0)
                    {
                        // Heuristic fallback for randomized MAC
                        return InferFromContext(hostname, openPorts) ?? "Mobile / Private MAC";
                    }
                }
            }
        }

        // Heuristic fallback from hostname / open ports
        return InferFromContext(hostname, openPorts) ?? "Unknown";
    }

    private static string? InferFromContext(string? hostname, IEnumerable<int>? openPorts)
    {
        var ports = openPorts?.ToHashSet() ?? [];

        // 1. Hostname heuristics
        if (!string.IsNullOrWhiteSpace(hostname))
        {
            if (hostname.Contains("apple", StringComparison.OrdinalIgnoreCase) ||
                hostname.Contains("iphone", StringComparison.OrdinalIgnoreCase) ||
                hostname.Contains("ipad", StringComparison.OrdinalIgnoreCase) ||
                hostname.Contains("macbook", StringComparison.OrdinalIgnoreCase))
                return "Apple";

            if (hostname.StartsWith("DESKTOP-", StringComparison.OrdinalIgnoreCase) ||
                hostname.StartsWith("LAPTOP-", StringComparison.OrdinalIgnoreCase) ||
                hostname.StartsWith("WIN-", StringComparison.OrdinalIgnoreCase))
                return "Microsoft Windows";

            if (hostname.Contains("android", StringComparison.OrdinalIgnoreCase) ||
                hostname.Contains("galaxy", StringComparison.OrdinalIgnoreCase))
                return "Samsung / Android";

            if (hostname.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            {
                if (ports.Contains(445) || ports.Contains(3389) || ports.Contains(139) || ports.Contains(135))
                    return "Microsoft Windows";
                if (ports.Contains(22))
                    return "Linux / Host";
            }
        }

        // 2. Open ports heuristics
        if (ports.Contains(554) || ports.Contains(8000) || ports.Contains(37777))
            return "IP Camera";

        if (ports.Contains(9100) || ports.Contains(515) || ports.Contains(631))
            return "Network Printer";

        if (ports.Contains(445) && ports.Contains(139))
            return "Windows Workstation";

        if (ports.Contains(22) && (ports.Contains(80) || ports.Contains(443)))
            return "Linux / Network Device";

        return null;
    }
}
