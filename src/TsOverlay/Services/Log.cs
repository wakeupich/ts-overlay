using System.Text;

namespace TsOverlay.Services;

/// <summary>Простой файловый лог с ротацией: %APPDATA%\TsOverlay\overlay.log.</summary>
public static class Log
{
    private static readonly object Sync = new();
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TsOverlay");

    private static string FilePath => Path.Combine(Dir, "overlay.log");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERR ", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Dir);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > 1_000_000)
                {
                    var old = FilePath + ".old";
                    if (File.Exists(old))
                    {
                        File.Delete(old);
                    }

                    File.Move(FilePath, old);
                }

                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(" | ").Append(level).Append(" | ").Append(message);
                if (ex != null)
                {
                    sb.AppendLine().Append(ex);
                }

                File.AppendAllText(FilePath, sb.ToString() + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Лог не должен ронять приложение.
        }
    }
}
