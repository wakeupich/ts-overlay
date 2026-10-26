namespace TsOverlay.Models;

public sealed class AppConfig
{
    public int Version { get; set; } = 1;

    /// <summary>Auto | Ts3 | Ts6</summary>
    public string ClientMode { get; set; } = "Auto";

    public string Ts3ApiKey { get; set; } = "";
    public int Ts3Port { get; set; } = 25639;
    public string Ts3Path { get; set; } = "";
    public string Ts6ApiKey { get; set; } = "";
    public int Ts6Port { get; set; } = 5899;
    public string Ts6Path { get; set; } = "";

    public bool OverlayEnabled { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool AutoLaunchTeamSpeak { get; set; }
    public bool ExitWithTeamSpeak { get; set; }
    public bool ShowSelf { get; set; } = true;
    public bool NotifyOnPermissionRequest { get; set; } = true;

    /// <summary>Глобальные горячие клавиши, например "Ctrl+Alt+O".</summary>
    public string HotkeyToggleOverlay { get; set; } = "Ctrl+Alt+O";

    public string HotkeyTalkingHud { get; set; } = "Ctrl+Alt+H";

    public bool CheckUpdatesOnStart { get; set; } = true;

    public OverlaySettings Overlay { get; set; } = new();
    public List<GameProfile> Games { get; set; } = new();

    public GameProfile? FindGame(string exeName)
    {
        foreach (var g in Games)
        {
            if (g.Matches(exeName))
            {
                return g;
            }
        }

        return null;
    }
}

public sealed class GameProfile
{
    /// <summary>Имя процесса без .exe, например "CaliberWin64".</summary>
    public string Exe { get; set; } = "";

    public string DisplayName { get; set; } = "";
    public bool Enabled { get; set; } = true;

    /// <summary>Необязательное переопределение положения оверлея для конкретной игры.</summary>
    public string? Anchor { get; set; }

    public int? MarginX { get; set; }
    public int? MarginY { get; set; }

    public bool Matches(string exeName)
    {
        var a = Normalize(Exe);
        var b = Normalize(exeName);
        return a.Length > 0 && a.Equals(b, StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe))
        {
            return "";
        }

        var name = exe.Trim().Trim('"');
        var slash = name.LastIndexOfAny(new[] { '\\', '/' });
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name;
    }
}

public sealed class OverlaySettings
{
    /// <summary>TopLeft | TopCenter | TopRight | LeftCenter | RightCenter | BottomLeft | BottomCenter | BottomRight</summary>
    public string Anchor { get; set; } = "TopLeft";

    public int MarginX { get; set; } = 16;
    public int MarginY { get; set; } = 16;

    /// <summary>Позиция после перетаскивания мышью (DIP от левого верхнего угла окна/рабочей области).</summary>
    public int CustomOffsetX { get; set; }

    public int CustomOffsetY { get; set; }

    public double Width { get; set; } = 320;

    /// <summary>Прозрачность только фона панели (0.2–1), текст и иконки не меняются.</summary>
    public double Opacity { get; set; } = 0.95;

    /// <summary>Общий масштаб: шрифт, высота строки, отступы.</summary>
    public double Scale { get; set; } = 1.0;

    public double RowHeight { get; set; } = 26;
    public double FontSize { get; set; } = 14;
    public int MaxRows { get; set; } = 16;
    public double CornerRadius { get; set; } = 10;

    public bool ShowHeader { get; set; } = true;
    public bool ShowServerName { get; set; } = true;
    public bool ShowAvatars { get; set; } = true;
    public bool ShowGroupBadges { get; set; } = true;

    public string Background { get; set; } = "#E60E1116";
    public string TextColor { get; set; } = "#FFE9EDF2";
    public string DimColor { get; set; } = "#99B9C2CE";
    public string TalkingColor { get; set; } = "#FF4ADE80";
    public string MutedColor { get; set; } = "#FFFF6B6B";

    public OverlaySettings Clone() => new()
    {
        Anchor = Anchor,
        MarginX = MarginX,
        MarginY = MarginY,
        CustomOffsetX = CustomOffsetX,
        CustomOffsetY = CustomOffsetY,
        Width = Width,
        Opacity = Opacity,
        Scale = Scale,
        RowHeight = RowHeight,
        FontSize = FontSize,
        MaxRows = MaxRows,
        CornerRadius = CornerRadius,
        ShowHeader = ShowHeader,
        ShowServerName = ShowServerName,
        ShowAvatars = ShowAvatars,
        ShowGroupBadges = ShowGroupBadges,
        Background = Background,
        TextColor = TextColor,
        DimColor = DimColor,
        TalkingColor = TalkingColor,
        MutedColor = MutedColor,
    };
}
