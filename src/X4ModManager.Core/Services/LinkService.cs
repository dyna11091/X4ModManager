using System.Text.RegularExpressions;

namespace X4ModManager.Core.Services;

public static partial class LinkService
{
    public static string? FindSupportedUrl(IEnumerable<string?> values)
    {
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var match = SupportedUrlRegex().Match(value!);
            if (match.Success)
            {
                return match.Value.TrimEnd('.', ',', ';', ')', ']');
            }
        }

        return null;
    }

    public static string BuildWorkshopUrl(string workshopId) =>
        $"https://steamcommunity.com/sharedfiles/filedetails/?id={Uri.EscapeDataString(workshopId)}";

    public static string? BuildNexusUrlFromPath(string path)
    {
        foreach (var segment in path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Reverse())
        {
            var match = NexusArchiveFolderRegex().Match(segment);
            if (match.Success)
            {
                return $"https://www.nexusmods.com/x4foundations/mods/{match.Groups[1].Value}";
            }
        }

        return null;
    }

    public static string BuildTranslationUrl(string text)
    {
        var limitedText = text.Length <= 4500 ? text : text[..4500];
        return $"https://translate.google.com/?sl=auto&tl=zh-CN&text={Uri.EscapeDataString(limitedText)}&op=translate";
    }

    [GeneratedRegex(@"https?://(?:www\.)?(?:nexusmods\.com|steamcommunity\.com)/[^\s<>\""']+", RegexOptions.IgnoreCase)]
    private static partial Regex SupportedUrlRegex();

    [GeneratedRegex(@"-(\d{2,6})-(?:\d+-){1,5}\d{9,}$", RegexOptions.IgnoreCase)]
    private static partial Regex NexusArchiveFolderRegex();
}

