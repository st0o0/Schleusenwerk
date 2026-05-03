using System.Collections.Concurrent;

namespace Schleusenwerk.Certificates;

public sealed class AcmeChallengeStore
{
    private readonly ConcurrentDictionary<string, string> _challenges = new();

    public void SetChallenge(string token, string keyAuthorization)
    {
        _challenges[token] = keyAuthorization;
    }

    public string? GetChallenge(string token)
    {
        return _challenges.TryGetValue(token, out var value) ? value : null;
    }

    public void RemoveChallenge(string token)
    {
        _challenges.TryRemove(token, out _);
    }
}
