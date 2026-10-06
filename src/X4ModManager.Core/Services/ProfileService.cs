using System.Text;
using System.Xml;
using System.Xml.Linq;
using X4ModManager.Core.Models;

namespace X4ModManager.Core.Services;

public static class ProfileService
{
    public static IReadOnlyDictionary<string, bool> ReadEnabledStates(string profilePath, ICollection<string>? warnings = null)
    {
        var states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(profilePath) || !File.Exists(profilePath))
        {
            return states;
        }

        try
        {
            var document = XDocument.Load(profilePath, LoadOptions.PreserveWhitespace);
            foreach (var extension in document.Descendants().Where(element => element.Name.LocalName == "extension"))
            {
                var id = extension.Attribute("id")?.Value;
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var rawEnabled = extension.Attribute("enabled")?.Value;
                states[id] = !string.Equals(rawEnabled, "false", StringComparison.OrdinalIgnoreCase)
                             && rawEnabled != "0";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            warnings?.Add($"无法读取玩家配置 {profilePath}: {ex.Message}");
        }

        return states;
    }

    public static string Apply(string profilePath, IEnumerable<ModInfo> mods)
    {
        if (!File.Exists(profilePath))
        {
            throw new FileNotFoundException("找不到玩家配置 content.xml。请先启动一次 X4，或手动选择正确的配置文件。", profilePath);
        }

        var document = XDocument.Load(profilePath, LoadOptions.PreserveWhitespace);
        var root = document.Root ?? throw new XmlException("玩家配置没有 XML 根节点。");
        if (!root.Name.LocalName.Equals("content", StringComparison.OrdinalIgnoreCase))
        {
            throw new XmlException("所选文件不是有效的 X4 玩家 content.xml。根节点应为 content。");
        }

        var directory = Path.GetDirectoryName(profilePath)!;
        var backupDirectory = Path.Combine(directory, "x4mm-backups");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(backupDirectory, $"content-{DateTime.Now:yyyyMMdd-HHmmss-fff}.xml");
        File.Copy(profilePath, backupPath, false);

        var editableMods = mods.ToList();
        foreach (var mod in editableMods)
        {
            var element = root.Elements()
                .FirstOrDefault(item => item.Name.LocalName == "extension"
                                        && string.Equals(item.Attribute("id")?.Value, mod.Id, StringComparison.OrdinalIgnoreCase));
            if (element is null)
            {
                element = new XElement("extension", new XAttribute("id", mod.Id));
                root.Add(element);
            }

            element.SetAttributeValue("enabled", mod.IsEnabled ? "true" : "false");
        }

        var temporaryPath = Path.Combine(directory, $".content.x4mm-{Guid.NewGuid():N}.tmp");
        try
        {
            var settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = true,
                OmitXmlDeclaration = document.Declaration is null
            };
            using (var writer = XmlWriter.Create(temporaryPath, settings))
            {
                document.Save(writer);
            }

            var verification = ReadEnabledStates(temporaryPath);
            foreach (var mod in editableMods)
            {
                if (!verification.TryGetValue(mod.Id, out var enabled) || enabled != mod.IsEnabled)
                {
                    throw new IOException($"写入校验失败：{mod.Name} 的状态不一致。");
                }
            }

            File.Move(temporaryPath, profilePath, true);
            foreach (var mod in editableMods)
            {
                mod.CurrentEnabled = mod.IsEnabled;
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return backupPath;
    }
}

