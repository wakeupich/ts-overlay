using TsOverlay.Models;

namespace TsOverlay.Services;

public interface ITeamSpeakSource : IAsyncDisposable
{
    TsClientKind Kind { get; }

    TsSnapshot Current { get; }

    bool IsRunning { get; }

    event Action<TsSnapshot>? SnapshotChanged;

    /// <summary>Сообщение, которое стоит показать пользователю (нет ключа, нужно разрешение и т.п.).</summary>
    event Action<string>? UserNotice;

    Task StartAsync();

    Task StopAsync();
}
