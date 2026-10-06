using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace X4ModManager.Core.Services;

internal static partial class X4XmlLoader
{
    public static XDocument Load(string path, LoadOptions options = LoadOptions.None)
    {
        var xml = File.ReadAllText(path);
        xml = Xml11DeclarationRegex().Replace(
            xml,
            match => $"{match.Groups[1].Value}1.0{match.Groups[2].Value}",
            1);
        return XDocument.Parse(xml, options);
    }

    [GeneratedRegex("(<\\?xml\\s+version\\s*=\\s*[\\\"'])1\\.1([\\\"'])", RegexOptions.IgnoreCase)]
    private static partial Regex Xml11DeclarationRegex();
}

