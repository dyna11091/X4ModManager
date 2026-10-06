using X4ModManager.Core.Models;
using X4ModManager.Core.Services;

var testRoot = Environment.GetEnvironmentVariable("X4MM_TEST_ROOT")
               ?? Path.Combine(Path.GetTempPath(), "x4-mod-manager-tests");
var runRoot = Path.Combine(testRoot, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(runRoot);

try
{
    TestProfileApply(runRoot);
    TestScanner(runRoot);
    TestLinks();
    TestChineseManifestAndNexusFolder(runRoot);
    TestXml11WorkshopIdTakesPriority(runRoot);
    TestFolderSettingsPersistence(runRoot);
    TestOfficialDlcCanBeDisabled(runRoot);
    Console.WriteLine("PASS: 7 core tests");
    return 0;
}
finally
{
    Directory.Delete(runRoot, true);
}

static void TestProfileApply(string root)
{
    var profileDirectory = Path.Combine(root, "profile");
    Directory.CreateDirectory(profileDirectory);
    var profilePath = Path.Combine(profileDirectory, "content.xml");
    File.WriteAllText(profilePath, "<?xml version=\"1.0\"?><content><extension id=\"alpha\" enabled=\"true\" custom=\"keep\"/><extension id=\"untouched\" enabled=\"true\"/></content>");

    var mods = new[]
    {
        NewMod("alpha", false),
        NewMod("beta", true),
        NewMod("ego_dlc_test", false, true)
    };
    var backup = ProfileService.Apply(profilePath, mods);
    var states = ProfileService.ReadEnabledStates(profilePath);

    Assert(File.Exists(backup), "Profile backup was not created.");
    Assert(states["alpha"] == false, "Existing mod was not disabled.");
    Assert(states["beta"], "Missing profile entry was not added.");
    Assert(!states["ego_dlc_test"], "Official DLC state was not written.");
    Assert(states["untouched"], "Unmanaged profile entry changed.");
    Assert(File.ReadAllText(profilePath).Contains("custom=\"keep\""), "Existing attributes were not preserved.");
}

static void TestScanner(string root)
{
    var game = Path.Combine(root, "game");
    var modDirectory = Path.Combine(game, "extensions", "sample");
    Directory.CreateDirectory(modDirectory);
    File.WriteAllText(Path.Combine(modDirectory, "content.xml"),
        "<content id=\"sample.mod\" name=\"Sample Mod\" author=\"Tester\" version=\"101\" description=\"See https://www.nexusmods.com/x4foundations/mods/42\" />");
    File.WriteAllText(Path.Combine(game, "X4.exe"), string.Empty);

    var profile = Path.Combine(root, "scan-profile", "content.xml");
    Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
    File.WriteAllText(profile, "<content><extension id=\"sample.mod\" enabled=\"false\"/></content>");

    var result = new ModScanner().Scan(game, profile);
    var mod = result.Mods.Single(item => item.Id == "sample.mod");
    Assert(mod.Name == "Sample Mod", "Manifest name was not read.");
    Assert(mod.Source == ModSource.Nexus, "Nexus source was not recognized.");
    Assert(!mod.IsEnabled, "Profile state was not read.");
}

static void TestLinks()
{
    var workshop = LinkService.BuildWorkshopUrl("12345");
    var translation = LinkService.BuildTranslationUrl("hello world");
    Assert(workshop.EndsWith("id=12345"), "Workshop URL is invalid.");
    Assert(translation.Contains("zh-CN") && translation.Contains("hello%20world"), "Translation URL is invalid.");
}

static void TestChineseManifestAndNexusFolder(string root)
{
    var game = Path.Combine(root, "localized-game");
    var archiveFolder = Path.Combine(game, "extensions", "Example Mod-2401-1-0-1781087140");
    var modDirectory = Path.Combine(archiveFolder, "example");
    Directory.CreateDirectory(modDirectory);
    File.WriteAllText(Path.Combine(modDirectory, "content.xml"),
        "<content id=\"example.mod\" name=\"Example\" description=\"English text\" author=\"Author\"><text language=\"86\" name=\"示例模组\" description=\"中文简介\"/></content>");
    File.WriteAllText(Path.Combine(game, "X4.exe"), string.Empty);

    var result = new ModScanner().Scan(game, string.Empty);
    var mod = result.Mods.Single(item => item.Id == "example.mod");
    Assert(mod.Name == "示例模组" && mod.Description == "中文简介", "Chinese manifest text was not preferred.");
    Assert(mod.Source == ModSource.Nexus && mod.SourceUrl?.EndsWith("/2401") == true, "Nexus archive folder was not recognized.");
}

static void TestXml11WorkshopIdTakesPriority(string root)
{
    var game = Path.Combine(root, "xml11-game");
    var modDirectory = Path.Combine(game, "extensions", "Terran Converter");
    Directory.CreateDirectory(modDirectory);
    File.WriteAllText(Path.Combine(modDirectory, "content.xml"),
        "<?xml version=\"1.1\" encoding=\"utf-8\"?><content id=\"ws_2042920500\" name=\"Workshop Mod\" description=\"Also mirrored at https://www.nexusmods.com/x4foundations/mods/99\" />");
    File.WriteAllText(Path.Combine(game, "X4.exe"), string.Empty);

    var result = new ModScanner().Scan(game, string.Empty);
    var mod = result.Mods.Single();
    Assert(result.Warnings.Count == 0, "XML 1.1 manifest produced a scan warning.");
    Assert(mod.Source == ModSource.SteamWorkshop, "ws_ ID was not recognized as Steam Workshop.");
    Assert(mod.SourceUrl?.EndsWith("id=2042920500") == true, "Workshop ID did not produce the correct source URL.");
}

static void TestFolderSettingsPersistence(string root)
{
    var settingsPath = Path.Combine(root, "settings", "settings.json");
    var service = new SettingsService(settingsPath);
    service.Save(new AppSettings
    {
        SelectedModFolder = "folder:船包",
        ModFolders = ["船包"],
        ModFolderAssignments = new Dictionary<string, string> { ["ws_123"] = "船包" },
        ModAliases = new Dictionary<string, string> { ["ws_123"] = "我的船包" }
    });

    var loaded = service.Load();
    Assert(loaded.SelectedModFolder == "folder:船包", "Selected functional folder was not persisted.");
    Assert(loaded.ModFolders.SequenceEqual(["船包"]), "Functional folder list was not persisted.");
    Assert(loaded.ModFolderAssignments["ws_123"] == "船包", "MOD folder assignment was not persisted.");
    Assert(loaded.ModAliases["ws_123"] == "我的船包", "MOD alias was not persisted.");
}

static void TestOfficialDlcCanBeDisabled(string root)
{
    var game = Path.Combine(root, "official-game");
    var dlcDirectory = Path.Combine(game, "extensions", "ego_dlc_test");
    Directory.CreateDirectory(dlcDirectory);
    File.WriteAllText(Path.Combine(dlcDirectory, "content.xml"),
        "<content id=\"ego_dlc_test\" name=\"Test DLC\" version=\"100\" />");
    File.WriteAllText(Path.Combine(game, "X4.exe"), string.Empty);

    var profile = Path.Combine(root, "official-profile", "content.xml");
    Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
    File.WriteAllText(profile, "<content><extension id=\"ego_dlc_test\" enabled=\"false\"/></content>");

    var dlc = new ModScanner().Scan(game, profile).Mods.Single();
    Assert(dlc.IsOfficial, "Official DLC was not recognized.");
    Assert(dlc.CanToggle && !dlc.IsEnabled, "Official DLC disabled state was not exposed as editable.");
}

static ModInfo NewMod(string id, bool enabled, bool official = false) => new()
{
    Id = id,
    Name = id,
    InstallPath = id,
    IsOfficial = official,
    IsEnabled = enabled,
    CurrentEnabled = !enabled
};

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
