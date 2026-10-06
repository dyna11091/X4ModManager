namespace X4ModManager.Core.Models;

public sealed record ScanResult(
    string GameVersion,
    IReadOnlyList<ModInfo> Mods,
    IReadOnlyList<string> Warnings);

