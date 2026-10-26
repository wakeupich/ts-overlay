using System.Windows.Media;

namespace TsOverlay.ViewModels;

/// <summary>Строка оверлея: ник, говорит/молчит, выключен ли микрофон.</summary>
public sealed class ClientRow : ObservableObject
{
    private string _nickname = "";
    private bool _isTalking;
    private bool _micMuted;
    private bool _deafened;
    private bool _isSelf;
    private bool _away;
    private Brush _nicknameBrush = Brushes.White;
    private Brush _statusBrush = Brushes.Gray;
    private Brush _talkingBackground = Brushes.Transparent;
    private Brush _micBrush = Brushes.Gray;
    private Brush _mutedBrush = Brushes.IndianRed;
    private Brush _talkingBrush = Brushes.LimeGreen;
    private double _rowHeight = 26;
    private double _fontSize = 14;

    public int Id { get; init; }

    public string Nickname
    {
        get => _nickname;
        set => Set(ref _nickname, value);
    }

    public bool IsTalking
    {
        get => _isTalking;
        set => Set(ref _isTalking, value);
    }

    public bool MicMuted
    {
        get => _micMuted;
        set => Set(ref _micMuted, value);
    }

    public bool Deafened
    {
        get => _deafened;
        set => Set(ref _deafened, value);
    }

    public bool IsSelf
    {
        get => _isSelf;
        set => Set(ref _isSelf, value);
    }

    public bool Away
    {
        get => _away;
        set => Set(ref _away, value);
    }

    public Brush NicknameBrush
    {
        get => _nicknameBrush;
        set => Set(ref _nicknameBrush, value);
    }

    public Brush TalkingBackground
    {
        get => _talkingBackground;
        set => Set(ref _talkingBackground, value);
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        set => Set(ref _statusBrush, value);
    }

    public Brush MicBrush
    {
        get => _micBrush;
        set => Set(ref _micBrush, value);
    }

    public Brush MutedBrush
    {
        get => _mutedBrush;
        set => Set(ref _mutedBrush, value);
    }

    public Brush TalkingBrush
    {
        get => _talkingBrush;
        set => Set(ref _talkingBrush, value);
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

    public string Tooltip
    {
        get
        {
            var parts = new List<string> { Nickname };
            if (IsSelf)
            {
                parts.Add("это вы");
            }

            if (MicMuted)
            {
                parts.Add("микрофон выключен");
            }

            if (Deafened)
            {
                parts.Add("звук выключен");
            }

            if (Away)
            {
                parts.Add("отошёл");
            }

            return string.Join(" · ", parts);
        }
    }
}
