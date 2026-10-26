namespace TsOverlay.Models;

public enum TsClientKind
{
    None = 0,
    Ts3 = 3,
    Ts6 = 6,
}

/// <summary>Снимок состояния клиента TeamSpeak: кто где, кто говорит, у кого выключен микрофон.</summary>
public sealed class TsSnapshot
{
    public static readonly TsSnapshot Empty = new();

    public TsClientKind Source { get; init; }
    public bool Connected { get; init; }
    public string Status { get; init; } = "TeamSpeak не запущен";
    public string ServerName { get; init; } = "";
    public int OwnClientId { get; init; }
    public int CurrentChannelId { get; init; }
    public string ChannelName { get; init; } = "";
    public IReadOnlyList<TsChannelState> Channels { get; init; } = Array.Empty<TsChannelState>();
    public IReadOnlyList<TsClientState> Clients { get; init; } = Array.Empty<TsClientState>();

    /// <summary>Имена серверных групп: sgid → название (TS3).</summary>
    public IReadOnlyDictionary<int, string> GroupNames { get; init; } = new Dictionary<int, string>();

    public int CountInCurrentChannel
    {
        get
        {
            var n = 0;
            foreach (var c in Clients)
            {
                if (c.ChannelId == CurrentChannelId && !c.IsQuery)
                {
                    n++;
                }
            }

            return n;
        }
    }
}

public sealed class TsClientState
{
    public int Id { get; init; }
    public int ChannelId { get; init; }
    public string Nickname { get; init; } = "";
    public bool Talking { get; init; }
    public bool InputMuted { get; init; }
    public bool OutputMuted { get; init; }
    public bool Away { get; init; }
    public bool PrioritySpeaker { get; init; }
    public bool ChannelCommander { get; init; }
    public bool IsSelf { get; init; }
    public bool IsQuery { get; init; }

    /// <summary>Хэш аватара (MD5) — одинаковый в TS3 и TS6.</summary>
    public string AvatarHash { get; init; } = "";

    /// <summary>ID серверных групп через запятую, как отдаёт клиент.</summary>
    public string ServerGroups { get; init; } = "";

    /// <summary>Копия с изменёнными полями (состояние неизменяемое, чтобы не ловить гонки потоков).</summary>
    public TsClientState Copy(
        int? channelId = null,
        string? nickname = null,
        bool? talking = null,
        bool? inputMuted = null,
        bool? outputMuted = null,
        bool? away = null,
        bool? prioritySpeaker = null,
        bool? channelCommander = null,
        bool? isSelf = null,
        bool? isQuery = null,
        string? avatarHash = null,
        string? serverGroups = null)
        => new()
        {
            Id = Id,
            ChannelId = channelId ?? ChannelId,
            Nickname = nickname ?? Nickname,
            Talking = talking ?? Talking,
            InputMuted = inputMuted ?? InputMuted,
            OutputMuted = outputMuted ?? OutputMuted,
            Away = away ?? Away,
            PrioritySpeaker = prioritySpeaker ?? PrioritySpeaker,
            ChannelCommander = channelCommander ?? ChannelCommander,
            IsSelf = isSelf ?? IsSelf,
            IsQuery = isQuery ?? IsQuery,
            AvatarHash = avatarHash ?? AvatarHash,
            ServerGroups = serverGroups ?? ServerGroups,
        };
}

public sealed class TsChannelState
{
    public int Id { get; init; }
    public int ParentId { get; init; }
    public string Name { get; init; } = "";
    public int Order { get; init; }
}
