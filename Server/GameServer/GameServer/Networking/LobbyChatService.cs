using System.Collections.Concurrent;
using GameServer.Protocol;

namespace GameServer.Networking;

public interface ILobbyChatService
{
    IReadOnlyList<LobbyChatMessageDto> ListMessages();
    LobbyChatMessageDto AddMessage(string playerId, string? displayName, string message);
}

public sealed class LobbyChatService : ILobbyChatService
{
    private const int MaxMessages = 40;
    private readonly ConcurrentQueue<LobbyChatMessageDto> _messages = new();

    public IReadOnlyList<LobbyChatMessageDto> ListMessages() =>
        _messages.OrderBy(message => message.SentAt).ToArray();

    public LobbyChatMessageDto AddMessage(string playerId, string? displayName, string message)
    {
        var trimmed = message.Trim();
        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("MessageRequired");
        }

        var dto = new LobbyChatMessageDto(
            Guid.NewGuid().ToString("N"),
            playerId,
            displayName,
            trimmed[..Math.Min(trimmed.Length, 280)],
            DateTimeOffset.UtcNow);

        _messages.Enqueue(dto);
        while (_messages.Count > MaxMessages && _messages.TryDequeue(out _))
        {
        }

        return dto;
    }
}
