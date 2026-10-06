using System.Windows;
using System.Windows.Controls;

namespace X4ModManager;

public sealed class AliasDialog : Window
{
    private readonly TextBox _aliasTextBox;

    public AliasDialog(string originalName, string currentAlias)
    {
        Title = "设置 MOD 别名";
        Width = 420;
        Height = 190;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI");
        SetResourceReference(BackgroundProperty, "PanelBackground");
        SetResourceReference(ForegroundProperty, "TextPrimary");

        var layout = new Grid { Margin = new Thickness(20) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "为此 MOD 设置显示别名",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold
        };
        layout.Children.Add(title);

        var original = new TextBlock
        {
            Text = $"原始名称：{originalName}",
            Margin = new Thickness(0, 7, 0, 8),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        original.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        Grid.SetRow(original, 1);
        layout.Children.Add(original);

        _aliasTextBox = new TextBox
        {
            Text = currentAlias,
            MaxLength = 80,
            Height = 34,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(_aliasTextBox, 2);
        layout.Children.Add(_aliasTextBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancelButton = new Button { Content = "取消", Width = 82, Margin = new Thickness(0, 0, 8, 0) };
        cancelButton.Click += (_, _) => DialogResult = false;
        var saveButton = new Button { Content = "保存", Width = 82, Margin = new Thickness(0) };
        saveButton.SetResourceReference(Button.BackgroundProperty, "Primary");
        saveButton.SetResourceReference(Button.BorderBrushProperty, "Primary");
        saveButton.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(saveButton);
        Grid.SetRow(buttons, 4);
        layout.Children.Add(buttons);

        Content = layout;
        Loaded += (_, _) =>
        {
            _aliasTextBox.Focus();
            _aliasTextBox.SelectAll();
        };
    }

    public string Alias => _aliasTextBox.Text.Trim();
}
