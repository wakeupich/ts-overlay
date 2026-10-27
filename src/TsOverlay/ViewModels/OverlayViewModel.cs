using System.Collections.ObjectModel;
using System.Windows.Media;
using TsOverlay.Models;

namespace TsOverlay.ViewModels;

/// <summary>Состояние оверлея, привязанное к XAML: строки канала, шапка, цвета и размеры.</summary>
public sealed class OverlayViewModel : ObservableObject
{
    private string _headerText = "TeamSpeak";
    private string _subHeaderText = "нет подключения";
    private bool _isConnected;
    private bool _hasRows;
    private string _emptyText = "Нет данных";
    private bool _showEmpty = true;
    private double _rowHeight = 26;
    private double _fontSize = 14;
    private double _subFontSize = 11;
    private double _cornerRadius = 10;
    private double _panelOpacity = 0.95;
    private double _panelPadding = 10;
    private double _headerHeight = 34;
    private bool _showHeader = true;
    private Brush _panelBackground = Brushes.Black;
    private Brush _textBrush = Brushes.White;
    private Brush _dimBrush = Brushes.Gray;
    private Brush _talkingBrush = Brushes.LimeGreen;
    private Brush _mutedBrush = Brushes.IndianRed;

    public ObservableCollection<ClientRow> Rows { get; } = new();

    public string HeaderText
    {
        get => _headerText;
        set => Set(ref _headerText, value);
    }

    public string SubHeaderText
    {
        get => _subHeaderText;
        set => Set(ref _subHeaderText, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set => Set(ref _isConnected, value);
    }

    public bool HasRows
    {
        get => _hasRows;
        set => Set(ref _hasRows, value);
    }

    public bool ShowEmpty
    {
        get => _showEmpty;
        set => Set(ref _showEmpty, value);
    }

    public string EmptyText
    {
        get => _emptyText;
        set => Set(ref _emptyText, value);
    }

    public double RowHeight
    {
        get => _rowHeight;
        set => Set(ref _rowHeight, value);
    }

    public double FontSize
    {
        get => _fontSize;
        set => Set(ref _fontSize, value);
    }

    public double SubFontSize
    {
        get => _subFontSize;
        set => Set(ref _subFontSize, value);
    }

    public double CornerRadius
    {
        get => _cornerRadius;
        set => Set(ref _cornerRadius, value);
    }

    public double PanelOpacity
    {
        get => _panelOpacity;
        set => Set(ref _panelOpacity, value);
    }

    public double PanelPadding
    {
        get => _panelPadding;
        set => Set(ref _panelPadding, value);
    }

    public double HeaderHeight
    {
        get => _headerHeight;
        set => Set(ref _headerHeight, value);
    }

    public bool ShowHeader
    {
        get => _showHeader;
        set => Set(ref _showHeader, value);
    }

    public Brush PanelBackground
    {
        get => _panelBackground;
        set => Set(ref _panelBackground, value);
    }

    public Brush TextBrush
    {
        get => _textBrush;
        set => Set(ref _textBrush, value);
    }

    public Brush DimBrush
    {
        get => _dimBrush;
        set => Set(ref _dimBrush, value);
    }

    public Brush TalkingBrush
    {
        get => _talkingBrush;
        set => Set(ref _talkingBrush, value);
    }

    public Brush MutedBrush
    {
        get => _mutedBrush;
        set => Set(ref _mutedBrush, value);
    }

    /// <summary>Высота панели в DIP (без учёта масштаба монитора).</summary>
    public double PanelHeight
    {
        get
        {
            var rows = HasRows ? Rows.Count : 1;
            var rowHeight = HasRows ? RowHeight : Math.Max(22, FontSize * 1.55);
            return PanelPadding * 2 + (ShowHeader ? HeaderHeight : 0) + rows * rowHeight;
        }
    }

    public void Apply(TsSnapshot snapshot, AppConfig config)
    {
        var o = config.Overlay;
        var scale = o.Scale;

        FontSize = o.FontSize * scale;
        SubFontSize = Math.Max(8, FontSize * 0.78);

        // Строка не может быть ниже текста: иначе при большом шрифте строки наезжают друг на друга.
        RowHeight = Math.Max(o.RowHeight * scale, FontSize * 1.55 + 2);
        HeaderHeight = Math.Max(34 * scale, FontSize * 1.25 + SubFontSize * 1.35 + 10);
        CornerRadius = o.CornerRadius;
        PanelOpacity = o.Opacity;
        PanelPadding = 10 * scale;
        ShowHeader = o.ShowHeader;
        PanelBackground = SolidWithOpacity(o.Background, o.Opacity, Color.FromArgb(230, 14, 17, 22));
        TextBrush = Solid(o.TextColor, Color.FromArgb(255, 233, 237, 242));
        DimBrush = Solid(o.DimColor, Color.FromArgb(153, 185, 194, 206));
        TalkingBrush = Solid(o.TalkingColor, Color.FromArgb(255, 74, 222, 128));
        MutedBrush = Solid(o.MutedColor, Color.FromArgb(255, 255, 107, 107));

        IsConnected = snapshot.Connected;

        var self = snapshot.OwnClientId;
        var wanted = snapshot.Clients
            .Where(c => c.ChannelId == snapshot.CurrentChannelId && !c.IsQuery && (config.ShowSelf || c.Id != self))
            .OrderByDescending(c => c.Id == self)
            .ThenBy(c => c.Nickname, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (!snapshot.Connected)
        {
            wanted.Clear();
        }

        var totalInChannel = wanted.Count;
        if (wanted.Count > o.MaxRows)
        {
            wanted = wanted.Take(o.MaxRows).ToList();
        }

        _groupNames = snapshot.GroupNames;
        SyncRows(wanted, o);

        HasRows = Rows.Count > 0;
        ShowEmpty = !HasRows;
        EmptyText = snapshot.Connected ? "В канале никого" : snapshot.Status;

        if (snapshot.Connected)
        {
            HeaderText = string.IsNullOrWhiteSpace(snapshot.ChannelName) ? "Текущий канал" : snapshot.ChannelName;
            var parts = new List<string>();
            if (o.ShowServerName && !string.IsNullOrWhiteSpace(snapshot.ServerName))
            {
                parts.Add(snapshot.ServerName);
            }

            parts.Add(totalInChannel + " " + Plural(totalInChannel, "человек", "человека", "человек"));
            if (snapshot.Source == TsClientKind.Ts3)
            {
                parts.Add("TS3");
            }
            else if (snapshot.Source == TsClientKind.Ts6)
            {
                parts.Add("TS6");
            }

            SubHeaderText = string.Join(" · ", parts);
        }
        else
        {
            HeaderText = "TeamSpeak";
            SubHeaderText = snapshot.Status;
        }

        Raise(nameof(PanelHeight));
    }

    private void SyncRows(List<TsClientState> wanted, OverlaySettings settings)
    {
        var sameOrder = Rows.Count == wanted.Count;
        if (sameOrder)
        {
            for (var i = 0; i < wanted.Count; i++)
            {
                if (Rows[i].Id != wanted[i].Id)
                {
                    sameOrder = false;
                    break;
                }
            }
        }

        if (!sameOrder)
        {
            Rows.Clear();
            foreach (var client in wanted)
            {
                var row = new ClientRow { Id = client.Id };
                ConfigureRow(row, client, settings);
                Rows.Add(row);
            }

            return;
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            ConfigureRow(Rows[i], wanted[i], settings);
        }
    }

    private void ConfigureRow(ClientRow row, TsClientState client, OverlaySettings settings)
    {
        // «Говорит» только если реально передаёт голос: status=2 (talking while disabled)
        // и flagTalking при выключенном микрофоне не подсвечиваем.
        var speaking = client.Talking && !client.InputMuted;

        row.Nickname = client.Nickname;
        row.IsSelf = client.IsSelf;
        row.Away = client.Away;
        row.RowHeight = RowHeight;
        row.FontSize = FontSize;
        row.MutedBrush = MutedBrush;
        row.TalkingBrush = TalkingBrush;
        row.NicknameBrush = speaking ? TalkingBrush : client.Away ? DimBrush : TextBrush;
        row.StatusBrush = speaking ? TalkingBrush : DimBrush;
        row.MicBrush = client.InputMuted ? MutedBrush : DimBrush;
        row.TalkingBackground = speaking ? Tint(TalkingBrush, 0x2E) : Brushes.Transparent;
        row.ShowAvatar = settings.ShowAvatars;
        row.AvatarText = Initials(client.Nickname);
        row.AvatarBrush = AvatarBrushFor(client.AvatarHash, client.Nickname);
        row.AvatarSize = Math.Clamp(RowHeight - 6, 14, 22);
        row.IconSize = Math.Clamp(RowHeight * 0.7, 16, 30);
        row.AvatarFontSize = Math.Max(8, row.AvatarSize * 0.44);
        UpdateBadges(row, client, settings.ShowGroupBadges);

        // Смена этих флагов перерисовывает иконки.
        row.MicMuted = client.InputMuted;
        row.Deafened = client.OutputMuted;
        row.IsTalking = speaking;
    }

    private static readonly Color[] AvatarPalette =
    {
        Color.FromRgb(0x4C, 0x8B, 0xF5),
        Color.FromRgb(0x34, 0xA8, 0x53),
        Color.FromRgb(0xEA, 0x43, 0x35),
        Color.FromRgb(0xF9, 0xAB, 0x00),
        Color.FromRgb(0x9C, 0x27, 0xB0),
        Color.FromRgb(0x00, 0xAC, 0xC1),
        Color.FromRgb(0x8D, 0x6E, 0x63),
        Color.FromRgb(0x5C, 0x6B, 0xC0),
        Color.FromRgb(0x43, 0xA0, 0x47),
        Color.FromRgb(0xEC, 0x40, 0x7A),
    };

    private IReadOnlyDictionary<int, string> _groupNames = new Dictionary<int, string>();

    private static string Initials(string nickname)
    {
        var letters = new List<char>();
        foreach (var part in nickname.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ch in part)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    letters.Add(char.ToUpper(ch, System.Globalization.CultureInfo.CurrentCulture));
                    break;
                }
            }

            if (letters.Count >= 2)
            {
                break;
            }
        }

        if (letters.Count == 0)
        {
            return "?";
        }

        if (letters.Count == 1)
        {
            foreach (var ch in nickname)
            {
                if (char.IsLetterOrDigit(ch) && char.ToUpper(ch) != letters[0])
                {
                    letters.Add(char.ToUpper(ch, System.Globalization.CultureInfo.CurrentCulture));
                    break;
                }
            }
        }

        return new string(letters.ToArray());
    }

    private static Brush AvatarBrushFor(string avatarHash, string nickname)
    {
        var seed = avatarHash.Length > 0 ? avatarHash : nickname;
        var hash = 17;
        foreach (var ch in seed)
        {
            hash = unchecked(hash * 31 + ch);
        }

        var color = AvatarPalette[Math.Abs(hash) % AvatarPalette.Length];
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void UpdateBadges(ClientRow row, TsClientState client, bool enabled)
    {
        row.Badges.Clear();
        if (!enabled || client.ServerGroups.Length == 0 || _groupNames.Count == 0)
        {
            row.ShowBadges = false;
            return;
        }

        foreach (var raw in client.ServerGroups.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(raw.Trim(), out var id) || !_groupNames.TryGetValue(id, out var name) || name.Length == 0)
            {
                continue;
            }

            var color = AvatarPalette[Math.Abs(id) % AvatarPalette.Length];
            var brush = new SolidColorBrush(Color.FromArgb(0xD0, color.R, color.G, color.B));
            brush.Freeze();
            row.Badges.Add(new GroupBadge
            {
                Text = name.Length <= 10 ? name : name[..9] + "…",
                Tooltip = name,
                Brush = brush,
                FontSize = Math.Max(8, FontSize * 0.62),
            });

            if (row.Badges.Count >= 2)
            {
                break;
            }
        }

        row.ShowBadges = row.Badges.Count > 0;
    }

    private static string Plural(int n, string one, string few, string many)
    {
        var mod100 = n % 100;
        if (mod100 is >= 11 and <= 14)
        {
            return many;
        }

        return (n % 10) switch
        {
            1 => one,
            2 or 3 or 4 => few,
            _ => many,
        };
    }

    private static Brush SolidWithOpacity(string hex, double opacity, Color fallback)
    {
        Color color;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            color = fallback;
        }

        var alpha = (byte)Math.Clamp(color.A * Math.Clamp(opacity, 0.2, 1.0), 0, 255);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static Brush Solid(string hex, Color fallback)
    {
        Color color;
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            color = fallback;
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Brush Tint(Brush source, byte alpha)
    {
        if (source is SolidColorBrush solid)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, solid.Color.R, solid.Color.G, solid.Color.B));
            brush.Freeze();
            return brush;
        }

        return Brushes.Transparent;
    }
}
