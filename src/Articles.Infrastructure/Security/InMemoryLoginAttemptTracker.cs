using System.Collections.Concurrent;
using Articles.Application.Common;

namespace Articles.Infrastructure.Security;

/// <summary>
/// Sliding-window failure counter per account key. In memory is deliberate: one API instance serves
/// all traffic, and losing the counters on a restart only gives an attacker a fresh window, not access.
/// If the API is ever scaled out, move this behind a shared store.
/// </summary>
public sealed class InMemoryLoginAttemptTracker : ILoginAttemptTracker
{
    public const int MaxFailures = 10;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    /// <summary>Bounds memory when someone sprays random emails; expired entries are swept first.</summary>
    private const int MaxTrackedKeys = 20_000;

    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _failures = new();
    private readonly TimeProvider _time;
    private readonly int _maxFailures;
    private readonly TimeSpan _window;

    public InMemoryLoginAttemptTracker(TimeProvider? time = null) : this(time, MaxFailures, Window) { }

    public InMemoryLoginAttemptTracker(TimeProvider? time, int maxFailures, TimeSpan window)
    {
        _time = time ?? TimeProvider.System;
        _maxFailures = maxFailures;
        _window = window;
    }

    public bool IsLockedOut(string key)
    {
        if (!_failures.TryGetValue(key, out var queue)) return false;

        lock (queue)
        {
            Prune(queue);
            return queue.Count >= _maxFailures;
        }
    }

    public void RecordFailure(string key)
    {
        if (_failures.Count >= MaxTrackedKeys) SweepExpired();
        if (_failures.Count >= MaxTrackedKeys) return;

        var queue = _failures.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
        lock (queue)
        {
            Prune(queue);
            queue.Enqueue(_time.GetUtcNow());
        }
    }

    public void Clear(string key) => _failures.TryRemove(key, out _);

    private void Prune(Queue<DateTimeOffset> queue)
    {
        var cutoff = _time.GetUtcNow() - _window;
        while (queue.Count > 0 && queue.Peek() < cutoff) queue.Dequeue();
    }

    private void SweepExpired()
    {
        foreach (var (key, queue) in _failures)
        {
            lock (queue)
            {
                Prune(queue);
                if (queue.Count == 0) _failures.TryRemove(key, out _);
            }
        }
    }
}

/// <summary>
/// Admin sign-in is an email plus a 6-digit code, and a code can be guessed where a password cannot.
/// Five wrong codes lock the account for the rest of an hour, which holds a determined guesser to
/// about 120 tries a day against a million possibilities.
/// </summary>
public sealed class AdminLoginAttemptTracker : IAdminLoginAttemptTracker
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly InMemoryLoginAttemptTracker _inner;

    public AdminLoginAttemptTracker(TimeProvider? time = null) => _inner = new InMemoryLoginAttemptTracker(time, MaxFailures, Window);

    public bool IsLockedOut(string key) => _inner.IsLockedOut(key);
    public void RecordFailure(string key) => _inner.RecordFailure(key);
    public void Clear(string key) => _inner.Clear(key);
}
