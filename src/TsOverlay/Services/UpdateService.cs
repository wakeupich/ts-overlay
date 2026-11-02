using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace TsOverlay.Services;

public sealed record UpdateInfo(Version Version, string Tag, string PageUrl, string? MsiUrl);

/// <summary>
/// Проверка обновлений через GitHub Releases. Работает, когда репозиторий публичный:
/// для приватного API отдаёт 404, и мы честно сообщаем об этом.
/// </summary>
public sealed class UpdateService
{
    public const string Repository = "wakeupich/ts-overlay";

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TsOverlay/" + CurrentVersion);

            using var response = await http.GetAsync(
                $"https://api.github.com/repos/{Repository}/releases/latest", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Log.Info($"Обновления: GitHub вернул {(int)response.StatusCode}");
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() ?? "" : "";
            var page = root.TryGetProperty("html_url", out var pageElement) ? pageElement.GetString() ?? "" : "";

            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
            {
                return null;
            }

            string? msi = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) &&
                        asset.TryGetProperty("browser_download_url", out var url))
                    {
                        msi = url.GetString();
                        break;
                    }
                }
            }

            return new UpdateInfo(version, tag, page, msi);
        }
        catch (Exception ex)
        {
            Log.Info("Обновления: " + ex.Message);
            return null;
        }
    }
}
