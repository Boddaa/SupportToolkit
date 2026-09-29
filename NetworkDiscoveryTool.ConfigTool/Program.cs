using System;
using System.IO;
using NetworkDiscoveryTool.Services.Services;

namespace NetworkDiscoveryTool.ConfigTool;

internal class Program
{
    private static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Title = "SupportToolKit - Telegram Config Encryptor";

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==================================================================");
        Console.WriteLine("        SupportToolKit - Encrypted Telegram Config Generator       ");
        Console.WriteLine("==================================================================");
        Console.ResetColor();
        Console.WriteLine("This tool generates an encrypted 'telegram.enc' file using AES-256.");
        Console.WriteLine("You can distribute or replace this file next to SupportToolKit.exe");
        Console.WriteLine("anytime your Bot Token or Chat ID changes without recompiling.");
        Console.WriteLine();

        string botToken = "";
        string adminChatId = "";
        string outputPath = "";

        if (args.Length >= 2)
        {
            botToken = args[0].Trim();
            adminChatId = args[1].Trim();
            outputPath = args.Length >= 3 ? args[2].Trim() : TelegramConfigCrypto.DefaultFileName;
        }
        else
        {
            const string currentDefaultToken = "8914418594:AAGMMqY91qu0MnkEM453gjclAm_RyOyGxfc";
            const string currentDefaultChatId = "1119565273";

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[Default Bot Token]: {currentDefaultToken}");
            Console.ResetColor();
            Console.Write("Enter Telegram Bot Token (press Enter to use default): ");
            var inputToken = Console.ReadLine()?.Trim();
            botToken = string.IsNullOrWhiteSpace(inputToken) ? currentDefaultToken : inputToken;

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[Default Admin Chat ID]: {currentDefaultChatId}");
            Console.ResetColor();
            Console.Write("Enter Telegram Admin Chat ID (press Enter to use default): ");
            var inputChatId = Console.ReadLine()?.Trim();
            adminChatId = string.IsNullOrWhiteSpace(inputChatId) ? currentDefaultChatId : inputChatId;

            Console.WriteLine();
            Console.Write($"Output filename (press Enter for '{TelegramConfigCrypto.DefaultFileName}'): ");
            var inputPath = Console.ReadLine()?.Trim();
            outputPath = string.IsNullOrWhiteSpace(inputPath) ? TelegramConfigCrypto.DefaultFileName : inputPath;
        }

        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(adminChatId))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("\n[Error] Bot Token and Admin Chat ID cannot be empty!");
            Console.ResetColor();
            PauseIfInteractive(args);
            return;
        }

        try
        {
            Console.WriteLine("\nEncrypting credentials...");
            TelegramConfigCrypto.SaveToFile(outputPath, botToken, adminChatId);

            var fullPath = Path.GetFullPath(outputPath);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("==================================================================");
            Console.WriteLine("✓ SUCCESS! Encrypted configuration file created successfully.");
            Console.WriteLine($"✓ File Path: {fullPath}");
            Console.WriteLine("==================================================================");
            Console.ResetColor();

            // Self-test verification
            if (TelegramConfigCrypto.TryLoadFromFile(outputPath, out var verifyToken, out var verifyChatId) &&
                verifyToken == botToken && verifyChatId == adminChatId)
            {
                Console.ForegroundColor = ConsoleColor.DarkGreen;
                Console.WriteLine("✓ Self-Test Passed: File decrypted and verified successfully.");
                Console.ResetColor();
            }

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("How to use this file:");
            Console.WriteLine("1. Copy 'telegram.enc' to the same folder as 'SupportToolKit.exe'.");
            Console.WriteLine("2. Start or restart SupportToolKit.exe.");
            Console.WriteLine("3. The application will automatically detect and apply the new credentials!");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[Error] Failed to generate encrypted file: {ex.Message}");
            Console.ResetColor();
        }

        PauseIfInteractive(args);
    }

    private static void PauseIfInteractive(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("\nPress any key to exit...");
            try { Console.ReadKey(); } catch { }
        }
    }
}
