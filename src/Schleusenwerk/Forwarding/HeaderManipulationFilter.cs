namespace Schleusenwerk.Forwarding;

/// <summary>
/// Defines a single header manipulation rule: add, remove, or replace a header.
/// </summary>
internal sealed record HeaderRule
{
    public required HeaderRuleAction Action { get; init; }
    public required string HeaderName { get; init; }
    public string? Value { get; init; }
}

internal enum HeaderRuleAction
{
    Add,
    Remove,
    Replace,
}

/// <summary>
/// Manipulates response headers before sending them to the client.
/// Removes upstream server identification headers by default and sets the Via header.
/// </summary>
internal sealed class HeaderManipulationFilter
{
    private static readonly string[] DefaultRemoveHeaders = ["Server", "X-Powered-By"];
    private const string ProxyIdentification = "Schleusenwerk";

    private readonly IReadOnlyList<HeaderRule> _rules;
    private readonly string _protocolVersion;

    public HeaderManipulationFilter(IReadOnlyList<HeaderRule> rules, string protocolVersion = "1.1")
    {
        _rules = rules;
        _protocolVersion = protocolVersion;
    }

    public HeaderManipulationFilter() : this([])
    {
    }

    public void Apply(IHeaderDictionary responseHeaders)
    {
        RemoveDefaultHeaders(responseHeaders);
        SetViaHeader(responseHeaders);
        ApplyCustomRules(responseHeaders);
    }

    private static void RemoveDefaultHeaders(IHeaderDictionary headers)
    {
        foreach (var headerName in DefaultRemoveHeaders)
        {
            headers.Remove(headerName);
        }
    }

    private void SetViaHeader(IHeaderDictionary headers)
    {
        headers.Via = $"{_protocolVersion} {ProxyIdentification}";
    }

    private void ApplyCustomRules(IHeaderDictionary headers)
    {
        foreach (var rule in _rules)
        {
            switch (rule.Action)
            {
                case HeaderRuleAction.Add:
                    if (!headers.ContainsKey(rule.HeaderName) && rule.Value is not null)
                    {
                        headers[rule.HeaderName] = rule.Value;
                    }
                    break;

                case HeaderRuleAction.Remove:
                    headers.Remove(rule.HeaderName);
                    break;

                case HeaderRuleAction.Replace:
                    if (rule.Value is not null)
                    {
                        headers[rule.HeaderName] = rule.Value;
                    }
                    break;
            }
        }
    }
}
