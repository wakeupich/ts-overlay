using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using TsOverlay.Models;
using TsOverlay.Services;

namespace TsOverlay.Views;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _live;
    private readonly AppConfig _working;
    private readonly Action<AppConfig> _applyPreview;
    private TextBox? _colorTarget;
    private readonly Action _restore;
    private readonly ObservableCollection<GameRow> _games = new();
    private bool _saved;

    public SettingsWindow(AppConfig live, string status, Action<AppConfig> applyPreview, Action restore)
    {
        _live = live;
        _applyPreview = applyPreview;
        _restore = restore;
        _working = Clone(live);

        InitializeComponent();
        GamesList.ItemsSource = _games;
        Fill(_working, status);

        Closed += (_, _) =>
        {
            if (!_saved)
            {
                _restore();
            }
        };
    }

    public event Action? Saved;

    /// <summary>Оверлей перетащили, пока открыты настройки: обновляем рабочую копию,
    /// иначе Save перезапишет позицию старым значением.</summary>
    public void SyncCustomPosition(int offsetX, int offsetY)
    {
        _working.Overlay.Anchor = "Custom";
        _working.Overlay.CustomOffsetX = offsetX;
        _working.Overlay.CustomOffsetY = offsetY;
        SelectTag(AnchorBox, "Custom");
    }

    private static AppConfig Clone(AppConfig config)
    {
        var json = JsonSerializer.Serialize(config);
        return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
    }

    private void Fill(AppConfig config, string status)
    {
        SelectTag(ClientModeBox, config.ClientMode);

        StartWithWindowsBox.IsChecked = StartupService.IsEnabled();
        AutoLaunchBox.IsChecked = config.AutoLaunchTeamSpeak;
        ExitWithTsBox.IsChecked = config.ExitWithTeamSpeak;
        NotifyBox.IsChecked = config.NotifyOnPermissionRequest;
        CheckUpdatesBox.IsChecked = config.CheckUpdatesOnStart;
        ShowSelfBox.IsChecked = config.ShowSelf;
        AlwaysShowBox.IsChecked = config.AlwaysShowOverlay;
        HotkeyToggleBox.Text = config.HotkeyToggleOverlay;
        HotkeyHudBox.Text = config.HotkeyTalkingHud;

        var o = config.Overlay;
        SelectTag(AnchorBox, o.Anchor);
        MarginXBox.Text = o.MarginX.ToString(CultureInfo.InvariantCulture);
        MarginYBox.Text = o.MarginY.ToString(CultureInfo.InvariantCulture);
        WidthBox.Text = o.Width.ToString("0.##", CultureInfo.InvariantCulture);
        ScaleBox.Text = o.Scale.ToString("0.##", CultureInfo.InvariantCulture);
        ScaleSlider.Value = Math.Clamp(o.Scale, 0.5, 3.0);
        RowHeightBox.Text = o.RowHeight.ToString("0.##", CultureInfo.InvariantCulture);
        FontSizeBox.Text = o.FontSize.ToString("0.##", CultureInfo.InvariantCulture);
        OpacitySlider.Value = Math.Clamp(o.Opacity, 0.2, 1.0);
        OpacityValueText.Text = o.Opacity.ToString("P0", CultureInfo.CurrentCulture);
        MaxRowsBox.Text = o.MaxRows.ToString(CultureInfo.InvariantCulture);
        CornerRadiusBox.Text = o.CornerRadius.ToString("0.##", CultureInfo.InvariantCulture);
        ShowHeaderBox.IsChecked = o.ShowHeader;
        ShowServerNameBox.IsChecked = o.ShowServerName;
        ShowAvatarsBox.IsChecked = o.ShowAvatars;
        ShowGroupBadgesBox.IsChecked = o.ShowGroupBadges;

        BackgroundBox.Text = o.Background;
        TextColorBox.Text = o.TextColor;
        DimColorBox.Text = o.DimColor;
        TalkingColorBox.Text = o.TalkingColor;
        MutedColorBox.Text = o.MutedColor;

        Ts3KeyBox.Text = config.Ts3ApiKey;
        Ts3PortBox.Text = config.Ts3Port.ToString(CultureInfo.InvariantCulture);
        Ts6KeyBox.Text = string.IsNullOrEmpty(config.Ts6ApiKey) ? "(будет получен при подключении)" : config.Ts6ApiKey;
        Ts6PortBox.Text = config.Ts6Port.ToString(CultureInfo.InvariantCulture);
        Ts3PathBox.Text = config.Ts3Path;
        Ts6PathBox.Text = config.Ts6Path;

        StatusText.Text = status;

        _games.Clear();
        foreach (var game in config.Games)
        {
            _games.Add(new GameRow
            {
                Enabled = game.Enabled,
                Exe = game.Exe,
                DisplayName = string.IsNullOrWhiteSpace(game.DisplayName) ? game.Exe : game.DisplayName,
            });
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        ReadInto(_working);

        foreach (var (name, gesture) in new[]
                 {
                     ("Показать/скрыть оверлей", _working.HotkeyToggleOverlay),
                     ("Кто говорит", _working.HotkeyTalkingHud),
                 })
        {
            if (gesture.Length > 0 && !HotkeyService.TryParse(gesture, out _, out _))
            {
                MessageBox.Show(this, $"Не удалось разобрать комбинацию «{gesture}» для «{name}». Пример: Ctrl+Alt+O.",
                    "TS Overlay", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        _live.ClientMode = _working.ClientMode;
        _live.AutoLaunchTeamSpeak = _working.AutoLaunchTeamSpeak;
        _live.ExitWithTeamSpeak = _working.ExitWithTeamSpeak;
        _live.NotifyOnPermissionRequest = _working.NotifyOnPermissionRequest;
        _live.CheckUpdatesOnStart = _working.CheckUpdatesOnStart;
        _live.ShowSelf = _working.ShowSelf;
        _live.AlwaysShowOverlay = _working.AlwaysShowOverlay;
        _live.HotkeyToggleOverlay = _working.HotkeyToggleOverlay;
        _live.HotkeyTalkingHud = _working.HotkeyTalkingHud;
        _live.Ts3ApiKey = _working.Ts3ApiKey.Trim();
        _live.Ts3Port = _working.Ts3Port;
        _live.Ts6Port = _working.Ts6Port;
        _live.Ts3Path = _working.Ts3Path.Trim();
        _live.Ts6Path = _working.Ts6Path.Trim();
        _live.Overlay = _working.Overlay;
        _live.Games = _working.Games;

        if (StartWithWindowsBox.IsChecked == true)
        {
            StartupService.SetEnabled(true);
        }
        else
        {
            StartupService.SetEnabled(false);
        }

        _saved = true;
        Saved?.Invoke();
        Close();
    }

    private void ReadInto(AppConfig config)
    {
        config.ClientMode = SelectedTag(ClientModeBox, "Auto");
        config.AutoLaunchTeamSpeak = AutoLaunchBox.IsChecked == true;
        config.ExitWithTeamSpeak = ExitWithTsBox.IsChecked == true;
        config.NotifyOnPermissionRequest = NotifyBox.IsChecked == true;
        config.CheckUpdatesOnStart = CheckUpdatesBox.IsChecked == true;
        config.ShowSelf = ShowSelfBox.IsChecked == true;
        config.AlwaysShowOverlay = AlwaysShowBox.IsChecked == true;
        config.HotkeyToggleOverlay = HotkeyToggleBox.Text.Trim();
        config.HotkeyTalkingHud = HotkeyHudBox.Text.Trim();

        var o = config.Overlay;
        o.Anchor = SelectedTag(AnchorBox, "TopLeft");
        o.MarginX = Clamp(IntValue(MarginXBox, o.MarginX), -4000, 4000);
        o.MarginY = Clamp(IntValue(MarginYBox, o.MarginY), -4000, 4000);
        o.Width = Clamp(DoubleValue(WidthBox, o.Width), 140, 900);
        o.Scale = Clamp(DoubleValue(ScaleBox, o.Scale), 0.5, 3.0);
        o.RowHeight = Clamp(DoubleValue(RowHeightBox, o.RowHeight), 16, 60);
        o.FontSize = Clamp(DoubleValue(FontSizeBox, o.FontSize), 8, 36);
        o.Opacity = Clamp(OpacitySlider.Value, 0.2, 1.0);
        o.MaxRows = Clamp(IntValue(MaxRowsBox, o.MaxRows), 1, 64);
        o.CornerRadius = Clamp(DoubleValue(CornerRadiusBox, o.CornerRadius), 0, 40);
        o.ShowHeader = ShowHeaderBox.IsChecked == true;
        o.ShowServerName = ShowServerNameBox.IsChecked == true;
        o.ShowAvatars = ShowAvatarsBox.IsChecked == true;
        o.ShowGroupBadges = ShowGroupBadgesBox.IsChecked == true;

        o.Background = NormalizeColor(BackgroundBox.Text, o.Background);
        o.TextColor = NormalizeColor(TextColorBox.Text, o.TextColor);
        o.DimColor = NormalizeColor(DimColorBox.Text, o.DimColor);
        o.TalkingColor = NormalizeColor(TalkingColorBox.Text, o.TalkingColor);
        o.MutedColor = NormalizeColor(MutedColorBox.Text, o.MutedColor);

        config.Ts3ApiKey = Ts3KeyBox.Text;
        config.Ts3Port = Clamp(IntValue(Ts3PortBox, config.Ts3Port), 1, 65535);
        config.Ts6Port = Clamp(IntValue(Ts6PortBox, config.Ts6Port), 1025, 65535);
        config.Ts3Path = Ts3PathBox.Text;
        config.Ts6Path = Ts6PathBox.Text;

        config.Games = _games.Select(g => new GameProfile
        {
            Enabled = g.Enabled,
            Exe = g.Exe,
            DisplayName = g.DisplayName,
        }).ToList();
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityValueText != null)
        {
            OpacityValueText.Text = e.NewValue.ToString("P0", CultureInfo.CurrentCulture);
        }

        if (IsLoaded)
        {
            ReadInto(_working);
            _applyPreview(_working);
        }
    }

    private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || ScaleBox == null)
        {
            return;
        }

        ScaleBox.Text = e.NewValue.ToString("0.##", CultureInfo.InvariantCulture);
        ReadInto(_working);
        _applyPreview(_working);
    }

    private void ScaleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || ScaleSlider == null)
        {
            return;
        }

        var text = (ScaleBox.Text ?? string.Empty).Trim().Replace(',', '.');
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            ScaleSlider.Value = Math.Clamp(value, ScaleSlider.Minimum, ScaleSlider.Maximum);
        }
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !ReferenceEquals(sender, MainTabs))
        {
            return;
        }

        if (ReferenceEquals(MainTabs.SelectedItem, OverlayTab))
        {
            // Зашли на вкладку оверлея — сразу показываем предпросмотр.
            ReadInto(_working);
            _applyPreview(_working);
        }
    }

    private void ColorPalette_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not TextBox target)
        {
            return;
        }

        _colorTarget = target;
        if (TryFindResource("ColorPaletteMenu") is ContextMenu menu)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void ColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (_colorTarget != null && sender is MenuItem item && item.Tag is string hex)
        {
            _colorTarget.Text = hex;
            ReadInto(_working);
            _applyPreview(_working);
        }
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (sender is not System.Windows.Controls.TextBox box)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
        {
            return;
        }

        if (key is Key.Back or Key.Delete)
        {
            box.Text = "";
            return;
        }

        box.Text = HotkeyService.Format(key, Keyboard.Modifiers);
    }

    private void OnPreviewClick(object sender, RoutedEventArgs e)    {
        ReadInto(_working);
        _applyPreview(_working);
    }

    private void OnAddRunning(object sender, RoutedEventArgs e)
    {
        var dialog = new RunningAppsWindow { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        foreach (var item in dialog.SelectedProcesses)
        {
            if (_games.Any(g => g.Exe.Equals(item.Name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _games.Add(new GameRow
            {
                Enabled = true,
                Exe = item.Name,
                DisplayName = string.IsNullOrWhiteSpace(item.Title) ? item.Name : item.Title,
            });
        }
    }

    private void OnBrowseExe(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите исполняемый файл игры",
            Filter = "Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var exe = GameProfile.Normalize(dialog.FileName);
        if (exe.Length == 0)
        {
            return;
        }

        if (_games.Any(g => g.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var display = exe;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(dialog.FileName);
            if (!string.IsNullOrWhiteSpace(info.ProductName))
            {
                display = info.ProductName!;
            }
        }
        catch
        {
            // имя процесса останется отображаемым
        }

        _games.Add(new GameRow { Enabled = true, Exe = exe, DisplayName = display });
    }

    private void OnRemoveGame(object sender, RoutedEventArgs e)
    {
        var selected = GamesList.SelectedItems.Cast<GameRow>().ToList();
        foreach (var row in selected)
        {
            _games.Remove(row);
        }
    }

    private void OnReadClientQueryKey(object sender, RoutedEventArgs e)
    {
        var key = ConfigService.TryReadTs3ClientQueryKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            MessageBox.Show(this, "Не удалось прочитать ключ из clientquery.ini. Проверьте, что TeamSpeak 3 запускался и плагин ClientQuery включён.",
                "TS Overlay", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Ts3KeyBox.Text = key;
    }

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Path.Combine(ConfigService.Directory, "overlay.log");
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true });
            }
            else
            {
                Directory.CreateDirectory(ConfigService.Directory);
                Process.Start(new ProcessStartInfo { FileName = ConfigService.Directory, UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "TS Overlay", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void SelectTag(System.Windows.Controls.ComboBox box, string tag)
    {
        foreach (var item in box.Items)
        {
            if (item is System.Windows.Controls.ComboBoxItem { Tag: string value } && value == tag)
            {
                box.SelectedItem = item;
                return;
            }
        }

        box.SelectedIndex = 0;
    }

    private static string SelectedTag(System.Windows.Controls.ComboBox box, string fallback)
        => box.SelectedItem is System.Windows.Controls.ComboBoxItem { Tag: string value } ? value : fallback;

    private static double DoubleValue(System.Windows.Controls.TextBox box, double fallback)
    {
        var text = box.Text.Trim().Replace(',', '.');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    private static int IntValue(System.Windows.Controls.TextBox box, int fallback)
        => int.TryParse(box.Text.Trim(), out var value) ? value : fallback;

    private static double Clamp(double value, double min, double max) => Math.Clamp(value, min, max);

    private static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);

    private static string NormalizeColor(string text, string fallback)
    {
        var value = text.Trim();
        if (!value.StartsWith('#'))
        {
            value = "#" + value;
        }

        if (value.Length is 7 or 9 && value[1..].All(Uri.IsHexDigit))
        {
            return value.ToUpperInvariant();
        }

        return fallback;
    }

    public sealed class GameRow
    {
        public bool Enabled { get; set; }

        public string Exe { get; set; } = "";

        public string DisplayName { get; set; } = "";
    }
}
