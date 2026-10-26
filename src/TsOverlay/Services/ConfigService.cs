using System.Text.Json;
using System.Text.Json.Serialization;
using TsOverlay.Models;

namespace TsOverlay.Services;

/// <summary>Хранит настройки в %APPDATA%\TsOverlay\config.json, ключи API шифрует через DPAPI.</summary>
public sealed class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Directory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TsOverlay");

    public string FilePath => Path.Combine(Directory, "config.json");

    public AppConfig Config { get; private set; } = new();

    public bool Exists => File.Exists(FilePath);

    public void Load()
    {
        AppConfig cfg;
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            }
            else
            {
                cfg = new AppConfig();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось прочитать конфиг, беру настройки по умолчанию", ex);
            TryBackupBrokenConfig();
            cfg = new AppConfig();
        }

        cfg.Overlay ??= new OverlaySettings();
        cfg.Games ??= new List<GameProfile>();
        cfg.Games.RemoveAll(g => string.IsNullOrWhiteSpace(g.Exe));

        // В файле ключи лежат зашифрованными, в памяти — открытыми.
        cfg.Ts3ApiKey = Dpapi.Unprotect(cfg.Ts3ApiKey);
        cfg.Ts6ApiKey = Dpapi.Unprotect(cfg.Ts6ApiKey);

        if (string.IsNullOrWhiteSpace(cfg.Ts3ApiKey))
        {
            cfg.Ts3ApiKey = TryReadTs3ClientQueryKey();
        }

        Normalize(cfg);
        Config = cfg;
    }

    public void Save()
    {
        try
        {
            var copy = CloneForSave(Config);
            System.IO.Directory.CreateDirectory(Directory);
            var json = JsonSerializer.Serialize(copy, JsonOptions);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
            Log.Info("Настройки сохранены");
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось сохранить настройки", ex);
        }
    }

    private static AppConfig CloneForSave(AppConfig cfg)
    {
        var json = JsonSerializer.Serialize(cfg, JsonOptions);
        var copy = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
        copy.Ts3ApiKey = Dpapi.Protect(cfg.Ts3ApiKey);
        copy.Ts6ApiKey = Dpapi.Protect(cfg.Ts6ApiKey);
        return copy;
    }

    private static void Normalize(AppConfig cfg)
    {
        if (cfg.ClientMode is not ("Auto" or "Ts3" or "Ts6"))
        {
            cfg.ClientMode = "Auto";
        }

        cfg.Ts3Port = cfg.Ts3Port is > 0 and < 65536 ? cfg.Ts3Port : 25639;
        cfg.Ts6Port = cfg.Ts6Port is > 1024 and < 65536 ? cfg.Ts6Port : 5899;

        var o = cfg.Overlay;
        o.Width = Math.Clamp(o.Width, 140, 900);
        o.RowHeight = Math.Clamp(o.RowHeight, 16, 60);
        o.FontSize = Math.Clamp(o.FontSize, 8, 36);
        o.MaxRows = Math.Clamp(o.MaxRows, 1, 64);
        o.Scale = Math.Clamp(o.Scale, 0.5, 3.0);
        o.Opacity = Math.Clamp(o.Opacity, 0.2, 1.0);
        o.CornerRadius = Math.Clamp(o.CornerRadius, 0, 40);
        o.MarginX = Math.Clamp(o.MarginX, -4000, 4000);
        o.MarginY = Math.Clamp(o.MarginY, -4000, 4000);

        if (string.IsNullOrWhiteSpace(o.Background))
        {
            o.Background = "#D90E1116";
        }
    }

    private void TryBackupBrokenConfig()
    {
        try
        {
            var backup = FilePath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(FilePath, backup, overwrite: true);
        }
        catch
        {
            // не критично
        }
    }

    /// <summary>TS3 сам хранит ключ ClientQuery в открытом виде — подхватываем его автоматически.</summary>
    public static string TryReadTs3ClientQueryKey()
    {
        try
        {
            var ini = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TS3Client", "clientquery.ini");
            if (!File.Exists(ini))
            {
                return "";
            }

            foreach (var line in File.ReadAllLines(ini))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("api_key=", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed["api_key=".Length..].Trim();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Не удалось прочитать clientquery.ini", ex);
        }

        return "";
    }
}
