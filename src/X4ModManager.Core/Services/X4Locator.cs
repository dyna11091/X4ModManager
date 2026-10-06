using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace X4ModManager.Core.Services;

public static partial class X4Locator
{
    private const string SteamAppId = "392160";

    public static string? FindGamePath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        foreach (var candidate in FindSteamLibraries().Select(path => Path.Combine(path, "steamapps", "common", "X4 Foundations")))
        {
            if (File.Exists(Path.Combine(candidate, "X4.exe")))
            {
                return candidate;
            }
        }

        foreach (var registryPath in new[]
                 {
                     @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\GOG.com\Games\1395669635",
                     @"HKEY_LOCAL_MACHINE\SOFTWARE\GOG.com\Games\1395669635"
                 })
        {
            if (Registry.GetValue(registryPath, "PATH", null) is string candidate
                && File.Exists(Path.Combine(candidate, "X4.exe")))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string? FindProfilePath()
    {
        var x4Documents = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Egosoft",
            "X4");
        if (!Directory.Exists(x4Documents))
        {
            return null;
        }

        return Directory.EnumerateFiles(x4Documents, "content.xml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}extensions{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    public static string? FindWorkshopPath(string gamePath)
    {
        var directory = new DirectoryInfo(gamePath);
        while (directory is not null && !directory.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            return null;
        }

        return Path.Combine(directory.FullName, "workshop", "content", SteamAppId);
    }

    private static IEnumerable<string> FindSteamLibraries()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<string>();
        }

        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                        ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        if (string.IsNullOrWhiteSpace(steamPath))
        {
            return libraries;
        }

        libraries.Add(Path.GetFullPath(steamPath));
        var libraryFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFile))
        {
            return libraries;
        }

        foreach (Match match in SteamLibraryRegex().Matches(File.ReadAllText(libraryFile)))
        {
            libraries.Add(match.Groups[1].Value.Replace("\\\\", "\\"));
        }

        return libraries;
    }

    [GeneratedRegex("\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase)]
    private static partial Regex SteamLibraryRegex();
}

