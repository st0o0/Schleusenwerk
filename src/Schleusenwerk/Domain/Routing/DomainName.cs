namespace Schleusenwerk.Domain.Routing;

/// <summary>
/// Validated domain name (e.g. "example.com", "*.example.com").
/// Supports exact and wildcard domains. Stored in lowercase.
/// </summary>
public readonly record struct DomainName
{
    public string Value { get; }
    public bool IsWildcard { get; }

    private DomainName(string value, bool isWildcard)
    {
        Value = value;
        IsWildcard = isWildcard;
    }

    public static DomainName Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new FormatException("Domain name cannot be empty.");

        var normalized = input.Trim().ToLowerInvariant();
        var isWildcard = normalized.StartsWith("*.");

        var hostPart = isWildcard ? normalized[2..] : normalized;

        if (hostPart.Length == 0)
            throw new FormatException($"Invalid domain name: '{input}'.");

        if (hostPart.StartsWith('.') || hostPart.EndsWith('.'))
            throw new FormatException($"Invalid domain name: '{input}'.");

        foreach (var label in hostPart.Split('.'))
        {
            if (label.Length == 0 || label.Length > 63)
                throw new FormatException($"Invalid domain label in '{input}'.");

            if (!IsValidLabel(label))
                throw new FormatException($"Invalid domain label '{label}' in '{input}'.");
        }

        return new DomainName(normalized, isWildcard);
    }

    public static bool TryParse(string input, out DomainName result)
    {
        try
        {
            result = Parse(input);
            return true;
        }
        catch (FormatException)
        {
            result = default;
            return false;
        }
    }

    public bool Matches(string host)
    {
        var normalized = host.Trim().ToLowerInvariant();

        if (!IsWildcard)
            return Value == normalized;

        // *.example.com matches sub.example.com but not example.com
        var suffix = Value[1..]; // .example.com
        return normalized.Length > suffix.Length && normalized.EndsWith(suffix);
    }

    private static bool IsValidLabel(ReadOnlySpan<char> label)
    {
        foreach (var c in label)
        {
            if (!char.IsLetterOrDigit(c) && c != '-')
                return false;
        }

        return label[0] != '-' && label[^1] != '-';
    }

    public override string ToString() => Value;
}
