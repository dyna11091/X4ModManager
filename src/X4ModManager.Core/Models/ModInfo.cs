using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace X4ModManager.Core.Models;

public sealed class ModInfo : INotifyPropertyChanged
{
    private bool _isEnabled;
    private string _alias = string.Empty;

    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Author { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Alias
    {
        get => _alias;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (_alias == normalized)
            {
                return;
            }

            _alias = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? Name : Alias;

    public required string InstallPath { get; init; }

    public ModSource Source { get; init; }

    public string? SourceUrl { get; init; }

    public bool IsOfficial { get; init; }

    public bool CurrentEnabled { get; set; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            _isEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StateText));
        }
    }

    public bool CanToggle => true;

    public string SourceText => Source switch
    {
        ModSource.Official => "官方 DLC",
        ModSource.SteamWorkshop => "创意工坊",
        ModSource.Nexus => "Nexus Mods",
        ModSource.UserExtension => "玩家目录",
        _ => "本地安装"
    };

    public string StateText => IsEnabled ? "已启用" : "已禁用";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

