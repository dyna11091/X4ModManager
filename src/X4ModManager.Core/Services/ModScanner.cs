using System.Diagnostics;
using System.Xml.Linq;
using X4ModManager.Core.Models;

namespace X4ModManager.Core.Services;

public sealed class ModScanner
{
    public ScanResult Scan(string gamePath, string profilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);

        var warnings = new List<string>();
        var enabledStates = ProfileService.ReadEnabledStates(profilePath, warnings);
        var candidates = FindCandidates(gamePath, profilePath);
        var mods = new List<ModInfo>();

        foreach (var candidate in candidates)
        {
            try
            {
                var mod = ReadManifest(candidate.ManifestPath, candidate.SourceRoot, candidate.Source, candidate.WorkshopId, enabledStates);
                if (mod is not null)
                {
                    mods.Add(mod);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                warnings.Add($"无法读取 {candidate.ManifestPath}: {ex.Message}");
            }
        }

        var duplicateIds = mods.GroupBy(mod => mod.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1);
        foreach (var duplicate in duplicateIds)
        {
            warnings.Add($"检测到重复扩展 ID“{duplicate.Key}”：{string.Join("；", duplicate.Select(mod => mod.InstallPath))}");
        }

        return new ScanResult(
            ReadGameVersion(gamePath),
            mods.OrderBy(mod => mod.IsOfficial ? 0 : 1).ThenBy(mod => mod.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            warnings);
    }

    private static IReadOnlyList<ModCandidate> FindCandidates(string gamePath, string profilePath)
    {
        var candidates = new List<ModCandidate>();
        AddExtensionDirectory(candidates, Path.Combine(gamePath, "extensions"), ModSource.Local);

        if (!string.IsNullOrWhiteSpace(profilePath))
        {
            var profileDirectory = Path.GetDirectoryName(profilePath);
            if (profileDirectory is not null)
            {
                AddExtensionDirectory(candidates, Path.Combine(profileDirectory, "extensions"), ModSource.UserExtension);
            }
        }

        var workshopPath = X4Locator.FindWorkshopPath(gamePath);
        if (workshopPath is not null && Directory.Exists(workshopPath))
        {
            foreach (var directory in Directory.EnumerateDirectories(workshopPath))
            {
                var manifest = FindManifest(directory);
                if (manifest is not null)
                {
                    candidates.Add(new ModCandidate(manifest, directory, ModSource.SteamWorkshop, Path.GetFileName(directory)));
                }
            }
        }

        return candidates
            .GroupBy(candidate => Path.GetFullPath(candidate.ManifestPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static void AddExtensionDirectory(List<ModCandidate> candidates, string extensionsPath, ModSource source)
    {
        if (!Directory.Exists(extensionsPath))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(extensionsPath))
        {
            var manifest = FindManifest(directory);
            if (manifest is not null)
            {
                candidates.Add(new ModCandidate(manifest, directory, source, null));
            }
        }
    }

    private static string? FindManifest(string directory)
    {
        var direct = Path.Combine(directory, "content.xml");
        if (File.Exists(direct))
        {
            return direct;
        }

        return Directory.EnumerateFiles(directory, "content.xml", SearchOption.AllDirectories).FirstOrDefault();
    }

    private static ModInfo? ReadManifest(
        string manifestPath,
        string sourceRoot,
        ModSource source,
        string? workshopId,
        IReadOnlyDictionary<string, bool> enabledStates)
    {
        var document = X4XmlLoader.Load(manifestPath, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        var root = document.Root;
        if (root is null || !root.Name.LocalName.Equals("content", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string RootAttribute(string name) => root.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Trim()
            ?? string.Empty;

        var localizedText = root.Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals("text", StringComparison.OrdinalIgnoreCase)
                                       && element.Attribute("language")?.Value == "86");
        string Attribute(string name)
        {
            var localized = localizedText?.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Trim();
            return string.IsNullOrWhiteSpace(localized) ? RootAttribute(name) : localized;
        }

        var id = Attribute("id");
        if (string.IsNullOrWhiteSpace(id))
        {
            id = Path.GetFileName(Path.GetDirectoryName(manifestPath)) ?? "unknown";
        }

        var name = Attribute("name");
        if (string.IsNullOrWhiteSpace(name))
        {
            name = id;
        }

        var description = Attribute("description");
        var official = id.StartsWith("ego_dlc_", StringComparison.OrdinalIgnoreCase)
                       || Path.GetFileName(Path.GetDirectoryName(manifestPath))?.StartsWith("ego_dlc_", StringComparison.OrdinalIgnoreCase) == true;
        var workshopIdFromManifest = id.StartsWith("ws_", StringComparison.OrdinalIgnoreCase)
            && id.Length > 3
            && id[3..].All(char.IsDigit)
                ? id[3..]
                : null;
        var effectiveWorkshopId = workshopIdFromManifest ?? workshopId;
        var sourceUrl = effectiveWorkshopId is not null
            ? LinkService.BuildWorkshopUrl(effectiveWorkshopId)
            : LinkService.FindSupportedUrl(new[]
            {
                Attribute("url"), Attribute("website"), Attribute("homepage"), Attribute("source"), description
            }) ?? LinkService.BuildNexusUrlFromPath(sourceRoot);

        if (!official && effectiveWorkshopId is not null)
        {
            source = ModSource.SteamWorkshop;
        }
        else if (!official && sourceUrl?.Contains("nexusmods.com", StringComparison.OrdinalIgnoreCase) == true)
        {
            source = ModSource.Nexus;
        }

        if (official)
        {
            source = ModSource.Official;
        }

        var enabled = !enabledStates.TryGetValue(id, out var profileEnabled) || profileEnabled;
        return new ModInfo
        {
            Id = id,
            Name = name,
            Author = Attribute("author"),
            Version = Attribute("version"),
            Description = description,
            InstallPath = Path.GetDirectoryName(manifestPath)!,
            Source = source,
            SourceUrl = sourceUrl,
            IsOfficial = official,
            CurrentEnabled = enabled,
            IsEnabled = enabled
        };
    }

    private static string ReadGameVersion(string gamePath)
    {
        var executable = Path.Combine(gamePath, "X4.exe");
        if (!File.Exists(executable))
        {
            return "未知";
        }

        var version = FileVersionInfo.GetVersionInfo(executable);
        return version.ProductVersion ?? version.FileVersion ?? "未知";
    }

    private sealed record ModCandidate(string ManifestPath, string SourceRoot, ModSource Source, string? WorkshopId);
}

