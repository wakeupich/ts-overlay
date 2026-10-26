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
            var rowHeight = HasRows ? RowHeight : 22;
            return PanelPadding * 2 + (ShowHeader ? HeaderHeight : 0) + rows * rowHeight;
        }
    }

    public void Apply(TsSnapshot snapshot, AppConfig config)
    {
        var o = config.Overlay;
        var scale = o.Scale;

        RowHeight = o.RowHeight * scale;
        FontSize = o.FontSize * scale;
        SubFontSize = Math.Max(8, o.FontSize * 0.78 * scale);
        CornerRadius = o.CornerRadius;
        PanelOpacity = o.Opacity;
        PanelPadding = 10 * scale;
        ShowHeader = o.ShowHeader;
        HeaderHeight = 34 * scale;
        PanelBackground = Solid(o.Background, Color.FromArgb(217, 14, 17, 22));
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

        SyncRows(wanted);

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

    private void SyncRows(List<TsClientState> wanted)
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
                ConfigureRow(row, client);
                Rows.Add(row);
            }

            return;
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            ConfigureRow(Rows[i], wanted[i]);
        }
    }

    private void ConfigureRow(ClientRow row, TsClientState client)
    {
        row.Nickname = client.Nickname;
        row.IsSelf = client.IsSelf;
        row.Away = client.Away;
        row.RowHeight = RowHeight;
        row.FontSize = FontSize;
        row.MutedBrush = MutedBrush;
        row.TalkingBrush = TalkingBrush;
        row.NicknameBrush = client.Talking ? TalkingBrush : client.Away ? DimBrush : TextBrush;
        row.StatusBrush = client.Talking ? TalkingBrush : DimBrush;
        row.MicBrush = client.InputMuted ? MutedBrush : DimBrush;
        row.TalkingBackground = client.Talking ? Tint(TalkingBrush, 0x2E) : Brushes.Transparent;

        // Смена этих флагов перерисовывает иконки.
        row.MicMuted = client.InputMuted;
        row.Deafened = client.OutputMuted;
        row.IsTalking = client.Talking;
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
