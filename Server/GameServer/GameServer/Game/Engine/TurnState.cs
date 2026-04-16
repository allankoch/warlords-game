using System.Collections.Immutable;

namespace GameServer.Game.Engine;

public sealed record TurnState(
    ImmutableArray<string> Order,
    int CurrentIndex,
    bool Started,
    int TurnNumber)
{
    public static TurnState Empty { get; } = new(ImmutableArray<string>.Empty, 0, false, 0);

    public string? CurrentPlayerId =>
        Order.IsDefaultOrEmpty ? null : Order[CurrentIndex];

    public bool IsPlayersTurn(string playerId) =>
        CurrentPlayerId is not null && string.Equals(CurrentPlayerId, playerId, StringComparison.Ordinal);

    public TurnState EnsurePlayerInOrder(string playerId)
    {
        if (IndexOf(playerId) >= 0)
        {
            return this;
        }

        return this with { Order = Order.Add(playerId) };
    }

    public TurnState RemovePlayer(string playerId)
    {
        var index = IndexOf(playerId);
        if (index < 0)
        {
            return this;
        }

        var newOrder = Order.RemoveAt(index);
        if (newOrder.IsDefaultOrEmpty)
        {
            return Empty;
        }

        var newIndex = CurrentIndex;
        if (index < newIndex)
        {
            newIndex--;
        }

        if (newIndex >= newOrder.Length)
        {
            newIndex = 0;
        }

        return this with { Order = newOrder, CurrentIndex = newIndex };
    }

    public TurnState Start(Func<string, bool> isEligible)
    {
        if (Order.IsDefaultOrEmpty)
        {
            return Empty;
        }

        for (var i = 0; i < Order.Length; i++)
        {
            if (isEligible(Order[i]))
            {
                return this with { Started = true, TurnNumber = 1, CurrentIndex = i };
            }
        }

        return this with { Started = true, TurnNumber = 1, CurrentIndex = 0 };
    }

    public TurnState AdvanceTurn(Func<string, bool> isEligible)
    {
        if (!Started || Order.IsDefaultOrEmpty)
        {
            return this;
        }

        var startIndex = CurrentIndex;
        var index = startIndex;
        do
        {
            index = (index + 1) % Order.Length;
            if (isEligible(Order[index]))
            {
                return this with { CurrentIndex = index, TurnNumber = TurnNumber + 1 };
            }
        } while (index != startIndex);

        return this with { TurnNumber = TurnNumber + 1 };
    }

    private int IndexOf(string playerId)
    {
        for (var i = 0; i < Order.Length; i++)
        {
            if (string.Equals(Order[i], playerId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }
}
