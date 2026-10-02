using System.Collections.Concurrent;

namespace SwiftBets.Casino.Simulator;

/// <summary>Faults a drill can arm: a report that hides transactions, and a win sent twice.</summary>
public sealed class Faults
{
    private readonly ConcurrentDictionary<string, int> _drop = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _duplicate = new(StringComparer.Ordinal);

    public void DropFromNextReport(string providerId, int count) => _drop[providerId] = count;

    public void DuplicateNextWin(string providerId) => _duplicate[providerId] = true;

    /// <summary>How many transactions the next report hides, once.</summary>
    public int TakeDrop(string providerId) => _drop.TryRemove(providerId, out var count) ? count : 0;

    public bool TakeDuplicate(string providerId) => _duplicate.TryRemove(providerId, out var armed) && armed;
}
