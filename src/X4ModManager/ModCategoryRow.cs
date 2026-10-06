namespace X4ModManager;

public sealed record ModCategoryRow(string Name, int Count, bool CanDelete, bool IsExpanded)
{
    public string Glyph => IsExpanded ? "▾" : "▸";
}
