namespace Schleusenwerk.Routing;

/// <summary>
/// Validated upstream URL. Must be an absolute HTTP or HTTPS URI.
/// </summary>
public readonly record struct UpstreamUrl
{
    public Uri Value { get; }
    public string Scheme => Value.Scheme;
    public string Host => Value.Host;
    public int Port => Value.Port;

    private UpstreamUrl(Uri value) => Value = value;

    public static UpstreamUrl Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new FormatException("Upstream URL cannot be empty.");

        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri))
            throw new FormatException($"Invalid upstream URL: '{input}'.");

        if (uri.Scheme is not ("http" or "https"))
            throw new FormatException($"Upstream URL must use http or https scheme, got '{uri.Scheme}'.");

        return new UpstreamUrl(uri);
    }

    public static bool TryParse(string input, out UpstreamUrl result)
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

    public override string ToString() => Value.ToString();
}
