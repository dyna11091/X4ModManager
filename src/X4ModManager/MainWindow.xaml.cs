using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using X4ModManager.Core.Models;
using X4ModManager.Core.Services;

namespace X4ModManager;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ModInfo> _mods = [];
    private readonly ObservableCollection<object> _displayItems = [];
    private readonly HashSet<string> _collapsedCategories = new(StringComparer.OrdinalIgnoreCase);
    private readonly ICollectionView _modsView;
    private readonly SettingsService _settingsService = new();
    private readonly ModScanner _scanner = new();
    private AppSettings _settings = new();
    private ModInfo? _selectedMod;
    private object? _dragCandidate;
    private bool _suppressCategoryClick;
    private Point _dragStart;

    public MainWindow()
    {
        InitializeComponent();
        _modsView = CollectionViewSource.GetDefaultView(_mods);
        _modsView.Filter = MatchesCurrentFilters;
        ModsGrid.ItemsSource = _displayItems;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = _settingsService.Load();
        _settings.ModOrder ??= [];
        _settings.ModFolders ??= [];
        _settings.ModFolderAssignments = new Dictionary<string, string>(
            _settings.ModFolderAssignments ?? [],
            StringComparer.OrdinalIgnoreCase);
        _settings.ModAliases = new Dictionary<string, string>(
            _settings.ModAliases ?? [],
            StringComparer.OrdinalIgnoreCase);
        _settings.SourceFilter = "all";

        ApplyTheme(_settings.IsDarkTheme);
        SourceFilterComboBox.SelectedValue = _settings.SourceFilter;
        if (!File.Exists(Path.Combine(_settings.GamePath, "X4.exe")))
        {
            _settings.GamePath = X4Locator.FindGamePath() ?? string.Empty;
        }

        if (!File.Exists(_settings.ProfilePath))
        {
            _settings.ProfilePath = X4Locator.FindProfilePath() ?? string.Empty;
        }

        UpdatePathDisplay();
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (!File.Exists(Path.Combine(_settings.GamePath, "X4.exe")))
        {
            StatusText.Text = "未找到 X4.exe，请选择游戏目录。";
            return;
        }

        SetBusy(true, "正在扫描本地扩展…");
        try
        {
            var result = await Task.Run(() => _scanner.Scan(_settings.GamePath, _settings.ProfilePath));
            foreach (var oldMod in _mods)
            {
                oldMod.PropertyChanged -= Mod_PropertyChanged;
            }

            _mods.Clear();
            var savedPositions = _settings.ModOrder
                .Select((id, index) => (id, index))
                .GroupBy(item => item.id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().index, StringComparer.OrdinalIgnoreCase);
            var orderedMods = result.Mods
                .Select((mod, index) => (mod, index))
                .OrderBy(item => savedPositions.TryGetValue(item.mod.Id, out var position) ? position : int.MaxValue)
                .ThenBy(item => item.index)
                .Select(item => item.mod);
            foreach (var mod in orderedMods)
            {
                if (_settings.ModAliases.TryGetValue(mod.Id, out var alias))
                {
                    mod.Alias = alias;
                }

                mod.PropertyChanged += Mod_PropertyChanged;
                _mods.Add(mod);
            }

            RemoveStaleFolderAssignments();
            RefreshDisplay();
            GameVersionText.Text = $"游戏版本：{result.GameVersion}";
            StatusText.Text = result.Warnings.Count == 0
                ? "扫描完成"
                : $"扫描完成，发现 {result.Warnings.Count} 条警告（首条：{result.Warnings[0]}）";
        }
        catch (Exception ex)
        {
            StatusText.Text = "扫描失败";
            MessageBox.Show(this, ex.Message, "扫描失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        RefreshButton.IsEnabled = !busy;
        ApplyButton.IsEnabled = !busy;
        if (status is not null)
        {
            StatusText.Text = status;
        }
    }

    private void UpdatePathDisplay()
    {
        GamePathTextBox.Text = _settings.GamePath;
        ProfilePathTextBox.Text = _settings.ProfilePath;
        _settingsService.Save(_settings);
    }

    private async void ChooseGamePath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择包含 X4.exe 的游戏目录",
            InitialDirectory = Directory.Exists(_settings.GamePath) ? _settings.GamePath : null
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!File.Exists(Path.Combine(dialog.FolderName, "X4.exe")))
        {
            MessageBox.Show(this, "所选目录中没有 X4.exe。", "目录无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.GamePath = dialog.FolderName;
        UpdatePathDisplay();
        await RefreshAsync();
    }

    private async void ChooseProfilePath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择玩家配置 content.xml",
            Filter = "X4 content.xml|content.xml|XML 文件|*.xml",
            InitialDirectory = File.Exists(_settings.ProfilePath) ? Path.GetDirectoryName(_settings.ProfilePath) : null
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _settings.ProfilePath = dialog.FileName;
        UpdatePathDisplay();
        await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void LaunchGame_Click(object sender, RoutedEventArgs e)
    {
        var executable = Path.Combine(_settings.GamePath, "X4.exe");
        if (!File.Exists(executable))
        {
            MessageBox.Show(this, "未找到 X4.exe，请先选择正确的游戏目录。", "无法启动", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WorkingDirectory = _settings.GamePath
            });
            StatusText.Text = "已启动 X4。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "启动 X4 失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EnableAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var mod in _mods.Where(mod => mod.CanToggle))
        {
            mod.IsEnabled = true;
        }

        StatusText.Text = "已选择全部启用；点击“应用更改”后写入。";
        RefreshDisplay();
    }

    private void DisableAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var mod in _mods.Where(mod => mod.CanToggle))
        {
            mod.IsEnabled = false;
        }

        StatusText.Text = "已选择全部禁用；点击“应用更改”后写入。";
        RefreshDisplay();
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_settings.ProfilePath))
        {
            MessageBox.Show(this, "请先选择有效的玩家配置 content.xml。", "无法应用", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var changedCount = _mods.Count(mod => mod.CanToggle && mod.CurrentEnabled != mod.IsEnabled);
        if (changedCount == 0)
        {
            StatusText.Text = "没有需要应用的更改。";
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"将修改 {changedCount} 个扩展的启用状态。X4 应处于关闭状态，继续吗？",
            "应用 MOD 配置",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true, "正在备份并写入玩家配置…");
        try
        {
            var backup = await Task.Run(() => ProfileService.Apply(_settings.ProfilePath, _mods));
            StatusText.Text = $"已应用 {changedCount} 项更改；备份：{backup}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "应用失败，原配置未被替换。";
            MessageBox.Show(this, ex.Message, "应用失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ModsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedMod = ModsGrid.SelectedItem as ModInfo;
        if (_selectedMod is null)
        {
            return;
        }

        UpdateModDetails(_selectedMod);
    }

    private void UpdateModDetails(ModInfo mod)
    {
        DetailNameText.Text = mod.DisplayName;
        DetailMetaText.Text = $"ID：{mod.Id}\n作者：{Fallback(mod.Author)}\n版本：{Fallback(mod.Version)}\n来源：{mod.SourceText}";
        DetailDescriptionText.Text = Fallback(mod.Description, "此 MOD 没有提供简介。");
        DetailPathText.Text = mod.InstallPath;
        OpenSourceButton.IsEnabled = mod.SourceUrl is not null;
        TranslateButton.IsEnabled = !string.IsNullOrWhiteSpace(mod.Description);
        OpenFolderButton.IsEnabled = Directory.Exists(mod.InstallPath);
    }

    private void SetAlias_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ModInfo mod)
        {
            return;
        }

        var dialog = new AliasDialog(mod.Name, mod.Alias) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        SetModAlias(mod, dialog.Alias);
    }

    private void ClearAlias_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ModInfo mod)
        {
            SetModAlias(mod, string.Empty);
        }
    }

    private void SetModAlias(ModInfo mod, string alias)
    {
        mod.Alias = alias.Equals(mod.Name, StringComparison.CurrentCultureIgnoreCase) ? string.Empty : alias;
        if (string.IsNullOrWhiteSpace(mod.Alias))
        {
            _settings.ModAliases.Remove(mod.Id);
        }
        else
        {
            _settings.ModAliases[mod.Id] = mod.Alias;
        }

        _settingsService.Save(_settings);
        RefreshDisplay();
        UpdateModDetails(mod);
        StatusText.Text = string.IsNullOrWhiteSpace(mod.Alias)
            ? $"已清除“{mod.Name}”的别名。"
            : $"已将“{mod.Name}”显示为“{mod.Alias}”。";
    }

    private void SourceFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || SourceFilterComboBox.SelectedValue is not string selectedFilter)
        {
            return;
        }

        _settings.SourceFilter = selectedFilter;
        _settingsService.Save(_settings);
        RefreshDisplay();
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(SearchTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        RefreshDisplay();
    }

    private bool MatchesCurrentFilters(object item)
    {
        if (item is not ModInfo mod)
        {
            return false;
        }

        var matchesSource = _settings.SourceFilter switch
        {
            "workshop" => mod.Source == ModSource.SteamWorkshop,
            "nexus" => mod.Source == ModSource.Nexus,
            "local" => mod.Source is ModSource.Local or ModSource.UserExtension,
            "official" => mod.Source == ModSource.Official,
            _ => true
        };

        if (!matchesSource)
        {
            return false;
        }

        var search = SearchTextBox?.Text?.Trim();
        return string.IsNullOrWhiteSpace(search)
               || mod.DisplayName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
               || mod.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
               || mod.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
               || mod.Author.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateCountText()
    {
        var visibleCount = _displayItems.OfType<ModInfo>().Count();
        CountText.Text = visibleCount == _mods.Count
            ? $"共 {_mods.Count} 个扩展"
            : $"显示 {visibleCount} / {_mods.Count}";
    }

    private void Mod_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ModInfo.IsEnabled))
        {
            return;
        }

        Dispatcher.BeginInvoke(UpdateCountText);
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var name = NewFolderTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (name.Length > 30)
        {
            MessageBox.Show(this, "分类名称最多 30 个字符。", "名称过长", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_settings.ModFolders.Any(folder => folder.Equals(name, StringComparison.CurrentCultureIgnoreCase)))
        {
            MessageBox.Show(this, "已存在同名分类。", "无法创建", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _settings.ModFolders.Add(name);
        NewFolderTextBox.Clear();
        _settingsService.Save(_settings);
        RefreshDisplay();
        StatusText.Text = $"已创建分类“{name}”；可将 MOD 拖到列表中的分类标题。";
    }

    private void DeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ModCategoryRow { CanDelete: true } category)
        {
            return;
        }

        _settings.ModFolders.RemoveAll(name => name.Equals(category.Name, StringComparison.OrdinalIgnoreCase));
        foreach (var modId in _settings.ModFolderAssignments
                     .Where(pair => pair.Value.Equals(category.Name, StringComparison.OrdinalIgnoreCase))
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _settings.ModFolderAssignments.Remove(modId);
        }

        _settingsService.Save(_settings);
        RefreshDisplay();
        StatusText.Text = $"已删除分类“{category.Name}”；其中的 MOD 已回到“未分类”。";
    }

    private void CategoryHeader_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ModDragPayload))
                    || e.Data.GetDataPresent(typeof(CategoryDragPayload))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void CategoryHeader_Drop(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ModCategoryRow category)
        {
            return;
        }

        if (e.Data.GetData(typeof(CategoryDragPayload)) is CategoryDragPayload categoryPayload)
        {
            ReorderCategory(categoryPayload.Name, category);
            e.Handled = true;
            return;
        }

        if (e.Data.GetData(typeof(ModDragPayload)) is not ModDragPayload modPayload)
        {
            return;
        }

        foreach (var mod in modPayload.Mods)
        {
            if (!category.CanDelete)
            {
                _settings.ModFolderAssignments.Remove(mod.Id);
            }
            else
            {
                _settings.ModFolderAssignments[mod.Id] = category.Name;
            }
        }

        _settingsService.Save(_settings);
        RefreshDisplay();
        SelectMods(modPayload.Mods);
        StatusText.Text = !category.CanDelete
            ? $"已将 {modPayload.Mods.Count} 个 MOD 移出功能分类。"
            : $"已将 {modPayload.Mods.Count} 个 MOD 放入“{category.Name}”。";
        e.Handled = true;
    }

    private void CategoryHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_suppressCategoryClick
            || (sender as FrameworkElement)?.DataContext is not ModCategoryRow category)
        {
            return;
        }

        if (!_collapsedCategories.Add(category.Name))
        {
            _collapsedCategories.Remove(category.Name);
        }

        RefreshDisplay();
        e.Handled = true;
    }

    private void ReorderCategory(string draggedName, ModCategoryRow targetCategory)
    {
        var oldIndex = _settings.ModFolders.FindIndex(name =>
            name.Equals(draggedName, StringComparison.OrdinalIgnoreCase));
        var targetIndex = targetCategory.CanDelete
            ? _settings.ModFolders.FindIndex(name => name.Equals(targetCategory.Name, StringComparison.OrdinalIgnoreCase))
            : 0;
        if (oldIndex < 0 || targetIndex < 0 || oldIndex == targetIndex)
        {
            return;
        }

        var name = _settings.ModFolders[oldIndex];
        _settings.ModFolders.RemoveAt(oldIndex);
        if (oldIndex < targetIndex)
        {
            targetIndex--;
        }

        _settings.ModFolders.Insert(Math.Clamp(targetIndex, 0, _settings.ModFolders.Count), name);
        _settingsService.Save(_settings);
        RefreshDisplay();
        StatusText.Text = $"已调整分类“{name}”的显示顺序。";
    }

    private void RefreshDisplay()
    {
        var selectedMods = ModsGrid.SelectedItems.OfType<ModInfo>().ToList();
        if (selectedMods.Count == 0 && _selectedMod is not null)
        {
            selectedMods.Add(_selectedMod);
        }

        _modsView.Refresh();
        var visibleMods = _modsView.Cast<ModInfo>().ToList();
        _displayItems.Clear();

        var uncategorized = visibleMods
            .Where(mod => !_settings.ModFolderAssignments.ContainsKey(mod.Id))
            .ToList();
        if (uncategorized.Count > 0)
        {
            var isExpanded = !_collapsedCategories.Contains("未分类");
            _displayItems.Add(new ModCategoryRow("未分类", uncategorized.Count, false, isExpanded));
            if (isExpanded)
            {
                foreach (var mod in uncategorized)
                {
                    _displayItems.Add(mod);
                }
            }
        }

        foreach (var name in _settings.ModFolders.Distinct(StringComparer.CurrentCultureIgnoreCase))
        {
            var categorized = visibleMods
                .Where(mod => _settings.ModFolderAssignments.TryGetValue(mod.Id, out var assigned)
                              && assigned.Equals(name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var isExpanded = !_collapsedCategories.Contains(name);
            _displayItems.Add(new ModCategoryRow(name, categorized.Count, true, isExpanded));
            if (isExpanded)
            {
                foreach (var mod in categorized)
                {
                    _displayItems.Add(mod);
                }
            }
        }

        SelectMods(selectedMods);

        UpdateCountText();
    }

    private void SelectMods(IEnumerable<ModInfo> mods)
    {
        ModsGrid.SelectedItems.Clear();
        foreach (var mod in mods.Where(_displayItems.Contains))
        {
            ModsGrid.SelectedItems.Add(mod);
        }
    }

    private void RemoveStaleFolderAssignments()
    {
        var modIds = _mods.Select(mod => mod.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var folderNames = _settings.ModFolders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var modId in _settings.ModFolderAssignments
                     .Where(pair => !modIds.Contains(pair.Key) || !folderNames.Contains(pair.Value))
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _settings.ModFolderAssignments.Remove(modId);
        }
    }

    private void ModsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(ModsGrid);
        var source = e.OriginalSource as DependencyObject;
        if (FindAncestor<CheckBox>(source) is not null)
        {
            _dragCandidate = null;
            return;
        }

        _dragCandidate = FindAncestor<ListBoxItem>(source)?.DataContext switch
        {
            ModInfo mod => mod,
            ModCategoryRow { CanDelete: true } category => category,
            _ => null
        };

        if (_dragCandidate is ModInfo selectedMod
            && ModsGrid.SelectedItems.Count > 1
            && ModsGrid.SelectedItems.Contains(selectedMod)
            && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
        }
    }

    private void ModsGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is null)
        {
            return;
        }

        var current = e.GetPosition(ModsGrid);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        object payload = _dragCandidate switch
        {
            ModInfo mod => new ModDragPayload(GetDraggedMods(mod)),
            ModCategoryRow category => new CategoryDragPayload(category.Name),
            _ => throw new InvalidOperationException("未知拖动项目。")
        };
        _suppressCategoryClick = _dragCandidate is ModCategoryRow;
        DragDrop.DoDragDrop(ModsGrid, payload, DragDropEffects.Move);
        _dragCandidate = null;
        Dispatcher.BeginInvoke(() => _suppressCategoryClick = false);
    }

    private IReadOnlyList<ModInfo> GetDraggedMods(ModInfo clickedMod)
    {
        var selectedIds = ModsGrid.SelectedItems
            .OfType<ModInfo>()
            .Select(mod => mod.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!selectedIds.Contains(clickedMod.Id))
        {
            return [clickedMod];
        }

        return _mods.Where(mod => selectedIds.Contains(mod.Id)).ToList();
    }

    private void ModsGrid_PreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ModDragPayload))
                    || e.Data.GetDataPresent(typeof(CategoryDragPayload))
            ? DragDropEffects.Move
            : DragDropEffects.None;

        var scrollViewer = FindDescendant<ScrollViewer>(ModsGrid);
        if (scrollViewer is null)
        {
            return;
        }

        var position = e.GetPosition(ModsGrid);
        const double edgeSize = 36;
        if (position.Y < edgeSize)
        {
            scrollViewer.LineUp();
        }
        else if (position.Y > ModsGrid.ActualHeight - edgeSize)
        {
            scrollViewer.LineDown();
        }
    }

    private void ModsGrid_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ModDragPayload)) is not ModDragPayload payload)
        {
            return;
        }

        var targetItem = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (targetItem?.DataContext is not ModInfo targetMod || payload.Mods.Contains(targetMod))
        {
            return;
        }

        var draggedMods = _mods.Where(payload.Mods.Contains).ToList();
        if (draggedMods.Count == 0)
        {
            return;
        }

        var targetHasCategory = _settings.ModFolderAssignments.TryGetValue(targetMod.Id, out var targetCategory);
        foreach (var mod in draggedMods)
        {
            if (targetHasCategory)
            {
                _settings.ModFolderAssignments[mod.Id] = targetCategory!;
            }
            else
            {
                _settings.ModFolderAssignments.Remove(mod.Id);
            }
        }

        foreach (var mod in draggedMods)
        {
            _mods.Remove(mod);
        }

        var newIndex = _mods.IndexOf(targetMod);
        foreach (var mod in draggedMods)
        {
            _mods.Insert(newIndex++, mod);
        }

        _settings.ModOrder = _mods.Select(mod => mod.Id).ToList();
        _settingsService.Save(_settings);
        RefreshDisplay();
        SelectMods(draggedMods);
        StatusText.Text = $"已将 {draggedMods.Count} 个 MOD 放入“{targetCategory ?? "未分类"}”并调整显示顺序。";
        e.Handled = true;
    }

    private static T? FindDescendant<T>(DependencyObject source) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(source); index++)
        {
            var child = VisualTreeHelper.GetChild(source, index);
            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = source switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(source),
                FrameworkContentElement contentElement => contentElement.Parent,
                _ => LogicalTreeHelper.GetParent(source)
            };
        }

        return null;
    }

    private void OpenSource_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMod?.SourceUrl is not null)
        {
            OpenWithShell(_selectedMod.SourceUrl);
        }
    }

    private void Translate_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_selectedMod?.Description))
        {
            OpenWithShell(LinkService.BuildTranslationUrl(_selectedMod.Description));
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var selectedMod = _selectedMod;
        if (selectedMod is not null && Directory.Exists(selectedMod.InstallPath))
        {
            OpenWithShell(selectedMod.InstallPath);
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        _settings.IsDarkTheme = !_settings.IsDarkTheme;
        ApplyTheme(_settings.IsDarkTheme);
        _settingsService.Save(_settings);
    }

    private void ApplyTheme(bool isDark)
    {
        var colors = isDark ? DarkThemeColors : LightThemeColors;
        foreach (var (key, value) in colors)
        {
            Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
        }

        ThemeToggleButton.Content = isDark ? "☀  白天" : "☾  夜间";
    }

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private static void OpenWithShell(string target) =>
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

    private static string Fallback(string? value, string fallback = "未提供") =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static readonly IReadOnlyDictionary<string, string> DarkThemeColors = new Dictionary<string, string>
    {
        ["WindowBackground"] = "#081019", ["SidebarBackground"] = "#0D1722",
        ["PanelBackground"] = "#111F2C", ["CardBackground"] = "#172B3A",
        ["PanelBorder"] = "#23445B", ["Primary"] = "#28A9FF", ["PrimaryHover"] = "#5BC2FF",
        ["TextPrimary"] = "#EFF8FF", ["TextSecondary"] = "#8EA8BA",
        ["TitleBarBackground"] = "#0D1C28", ["TitleBarBorder"] = "#1F4B68", ["OuterBorder"] = "#1D4E70",
        ["SidebarBorder"] = "#16364C", ["ButtonBackground"] = "#173047", ["ButtonBorder"] = "#285879",
        ["ButtonHover"] = "#204864", ["InputBackground"] = "#0A1621", ["ComboBackground"] = "#10283A",
        ["CheckBackground"] = "#0D1C29", ["CheckBorder"] = "#315D79", ["ActiveNavBackground"] = "#12314A",
        ["ActiveNavBorder"] = "#1C587D", ["PanelOutline"] = "#173B52", ["Divider"] = "#20445B",
        ["ItemHover"] = "#1B3548", ["ItemSelected"] = "#173B53", ["BadgeBackground"] = "#164668",
        ["BadgeBorder"] = "#24678D", ["BadgeText"] = "#BFE8FF", ["DragHandle"] = "#5E849C",
        ["Success"] = "#63D5A2", ["ScrollTrack"] = "#0C1924", ["ScrollThumb"] = "#31566E",
        ["ScrollThumbHover"] = "#4A7894"
    };

    private static readonly IReadOnlyDictionary<string, string> LightThemeColors = new Dictionary<string, string>
    {
        ["WindowBackground"] = "#EAF2F7", ["SidebarBackground"] = "#DFEAF0",
        ["PanelBackground"] = "#F9FCFE", ["CardBackground"] = "#FFFFFF",
        ["PanelBorder"] = "#B7CEDB", ["Primary"] = "#0078D4", ["PrimaryHover"] = "#168BE0",
        ["TextPrimary"] = "#102330", ["TextSecondary"] = "#526D7E",
        ["TitleBarBackground"] = "#F8FBFD", ["TitleBarBorder"] = "#B9CFDB", ["OuterBorder"] = "#7FB2CF",
        ["SidebarBorder"] = "#B8CDD8", ["ButtonBackground"] = "#E2EFF6", ["ButtonBorder"] = "#96BDD3",
        ["ButtonHover"] = "#CCE4F1", ["InputBackground"] = "#FFFFFF", ["ComboBackground"] = "#FFFFFF",
        ["CheckBackground"] = "#FFFFFF", ["CheckBorder"] = "#7FA8BF", ["ActiveNavBackground"] = "#CFE8F7",
        ["ActiveNavBorder"] = "#69ACD2", ["PanelOutline"] = "#B8D0DD", ["Divider"] = "#C6D9E3",
        ["ItemHover"] = "#E8F3F9", ["ItemSelected"] = "#D6ECF8", ["BadgeBackground"] = "#D8EEF9",
        ["BadgeBorder"] = "#89BDD7", ["BadgeText"] = "#075C8C", ["DragHandle"] = "#6C8796",
        ["Success"] = "#167A52", ["ScrollTrack"] = "#E3EDF2", ["ScrollThumb"] = "#9BBACB",
        ["ScrollThumbHover"] = "#6F9EB8"
    };

    private sealed record ModDragPayload(IReadOnlyList<ModInfo> Mods);

    private sealed record CategoryDragPayload(string Name);

}

