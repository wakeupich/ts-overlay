using System.Diagnostics;
using System.Globalization;
using System.Text;
using TsOverlay.Models;

namespace TsOverlay.Services;

/// <summary>
/// Диагностический режим: подключается к клиенту TeamSpeak и пишет состояние в probe.log.
/// Запуск: TsOverlay.exe --probe ts3|ts6 [--probe-seconds N]
/// </summary>
public static class Probe
{
    private static readonly object WriteLock = new();

    public static void Run(string[] args)
    {
        var kind = "auto";
        var seconds = 25;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--probe", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length &&
                (args[i + 1].Equals("ts3", StringComparison.OrdinalIgnoreCase) || args[i + 1].Equals("ts6", StringComparison.OrdinalIgnoreCase)))
            {
                kind = args[i + 1].ToLowerInvariant();
                i++;
            }
            else if (args[i].Equals("--probe-seconds", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length &&
                     int.TryParse(args[i + 1], out var parsed))
            {
                seconds = Math.Clamp(parsed, 3, 300);
                i++;
            }
        }

        var configService = new ConfigService();
        configService.Load();
        var config = configService.Config;

        if (kind == "auto")
        {
            kind = Process.GetProcessesByName("ts3client_win64").Length > 0 ? "ts3"
                : Process.GetProcessesByName("TeamSpeak").Length > 0 ? "ts6" : "ts3";
        }

        var logPath = Path.Combine(ConfigService.Directory, "probe.log");
        Directory.CreateDirectory(ConfigService.Directory);
        File.WriteAllText(logPath, "");
        Write(logPath, "=== TS Overlay probe ===");
        Write(logPath, $"клиент: {kind}, длительность: {seconds} c, лог: {logPath}");

        ITeamSpeakSource source;
        if (kind == "ts6")
        {
            Write(logPath, "TS6: если в клиенте появится запрос доступа — нажмите «Разрешить».");
            source = new Ts6RemoteAppSource(config.Ts6Port, () => config.Ts6ApiKey, key =>
            {
                config.Ts6ApiKey = key;
                configService.Save();
                Write(logPath, "TS6: получен ключ API, сохранён в настройках");
            });
        }
        else
        {
            Write(logPath, "TS3: используется плагин ClientQuery на 127.0.0.1:" + config.Ts3Port);
            source = new Ts3ClientQuerySource(config.Ts3ApiKey, config.Ts3Port);
        }

        source.SnapshotChanged += snapshot => Write(logPath, Format(snapshot));
        source.UserNotice += message => Write(logPath, "УВЕДОМЛЕНИЕ: " + message);

        try
        {
            Write(logPath, "запуск источника…");
            source.StartAsync().GetAwaiter().GetResult();
            Write(logPath, $"ожидание {seconds} c…");
            Thread.Sleep(TimeSpan.FromSeconds(seconds));
        }
        catch (Exception ex)
        {
            Write(logPath, "ОШИБКА: " + ex);
        }
        finally
        {
            Write(logPath, "остановка источника…");
            try
            {
                // StopAsync нельзя блокировать на UI-потоке: его await-продолжения
                // требуют диспетчера, который мы бы и заблокировали.
                if (!Task.Run(() => source.StopAsync()).Wait(TimeSpan.FromSeconds(5)))
                {
                    Write(logPath, "ПРЕДУПРЕЖДЕНИЕ: остановка не завершилась за 5 c");
                }
            }
            catch (Exception ex)
            {
                Write(logPath, "ОШИБКА остановки: " + ex);
            }
        }

        Write(logPath, "=== готово ===");
    }

    private static string Format(TsSnapshot snapshot)
    {
        var sb = new StringBuilder();
        sb.Append(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
        sb.Append(" | ").Append(snapshot.Source);
        sb.Append(" | connected=").Append(snapshot.Connected ? "1" : "0");
        sb.Append(" | status=").Append(snapshot.Status);
        if (snapshot.ServerName.Length > 0)
        {
            sb.Append(" | server=").Append(snapshot.ServerName);
        }

        if (snapshot.ChannelName.Length > 0)
        {
            sb.Append(" | channel=").Append(snapshot.ChannelName);
        }

        sb.Append(" | clients=").Append(snapshot.CountInCurrentChannel);
        sb.AppendLine();
        foreach (var client in snapshot.Clients)
        {
            if (client.ChannelId != snapshot.CurrentChannelId)
            {
                continue;
            }

            sb.Append("        ").Append(client.Nickname);
            var flags = new List<string>();
            if (client.IsSelf) flags.Add("self");
            if (client.Talking) flags.Add("TALK");
            if (client.InputMuted) flags.Add("mic-off");
            if (client.OutputMuted) flags.Add("deaf");
            if (client.Away) flags.Add("away");
            if (client.PrioritySpeaker) flags.Add("priority");
            if (flags.Count > 0)
            {
                sb.Append("  [").Append(string.Join(", ", flags)).Append(']');
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static void Write(string path, string text)
    {
        try
        {
            lock (WriteLock)
            {
                File.AppendAllText(path, text + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // диагностика не должна падать
        }
    }
}
