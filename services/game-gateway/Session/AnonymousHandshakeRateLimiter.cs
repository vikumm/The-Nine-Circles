namespace Divinity.GameGateway.Session;

public sealed class AnonymousHandshakeRateLimiter
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _attemptsByRemoteAddress = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public AnonymousHandshakeRateLimiter(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool TryAcquire(string remoteAddress)
    {
        var now = _timeProvider.GetUtcNow();

        lock (_gate)
        {
            if (!_attemptsByRemoteAddress.TryGetValue(remoteAddress, out var attempts))
            {
                attempts = new Queue<DateTimeOffset>();
                _attemptsByRemoteAddress[remoteAddress] = attempts;
            }

            while (attempts.Count > 0 && now - attempts.Peek() >= GatewaySessionDefaults.AnonymousHandshakeWindow)
            {
                attempts.Dequeue();
            }

            if (attempts.Count >= GatewaySessionDefaults.AnonymousHandshakeLimit)
            {
                return false;
            }

            attempts.Enqueue(now);
            return true;
        }
    }
}
