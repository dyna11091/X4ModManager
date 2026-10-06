namespace X4ModManager.Core.Models;

public sealed class AppSettings
{
    public string GamePath { get; set; } = string.Empty;

    public string ProfilePath { get; set; } = string.Empty;

    public string CategoryFilter { get; set; } = "all";

    public string SourceFilter { get; set; } = "all";

    public string SelectedModFolder { get; set; } = "all";

    public List<string> ModFolders { get; set; } = [];

    public Dictionary<string, string> ModFolderAssignments { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> ModAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> ModOrder { get; set; } = [];

    public bool IsDarkTheme { get; set; } = true;
}

