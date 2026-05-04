using Schleusenwerk.Routing;

namespace Schleusenwerk.Persistence;

/// <summary>
/// Validates configuration commands before they are applied.
/// Returns Result types instead of throwing exceptions.
/// </summary>
public static class ConfigurationValidator
{
    public static ConfigurationResult ValidateAddDomain(DomainConfig config, ConfigurationState state)
    {
        var formatResult = ValidateDomainFormat(config.DomainName.Value);
        if (formatResult.IsFailure)
        {
            return formatResult;
        }

        if (state.HasDomain(config.DomainName))
        {
            return new ConfigurationResult.Failure($"Domain '{config.DomainName}' already exists.");
        }

        return ValidateRequestTimeout(config.RequestTimeout);
    }

    public static ConfigurationResult ValidateUpdateDomain(DomainConfig config, ConfigurationState state)
    {
        var formatResult = ValidateDomainFormat(config.DomainName.Value);
        if (formatResult.IsFailure)
        {
            return formatResult;
        }

        if (!state.HasDomain(config.DomainName))
        {
            return new ConfigurationResult.Failure($"Domain '{config.DomainName}' does not exist.");
        }

        return ValidateRequestTimeout(config.RequestTimeout);
    }

    public static ConfigurationResult ValidateRemoveDomain(DomainName domainName, ConfigurationState state)
    {
        if (!state.HasDomain(domainName))
        {
            return new ConfigurationResult.Failure($"Domain '{domainName}' does not exist.");
        }

        return ConfigurationResult.Success.Instance;
    }

    public static ConfigurationResult ValidateAddUpstream(
        DomainName domainName, UpstreamTarget upstream, ConfigurationState state)
    {
        if (!state.HasDomain(domainName))
        {
            return new ConfigurationResult.Failure($"Domain '{domainName}' does not exist.");
        }

        var urlResult = ValidateUpstreamUrlFormat(upstream.Url.Value.ToString());
        if (urlResult.IsFailure)
        {
            return urlResult;
        }

        if (state.HasUpstream(domainName, upstream.Url))
        {
            return new ConfigurationResult.Failure(
                $"Upstream '{upstream.Url}' already exists for domain '{domainName}'.");
        }

        if (upstream.Weight < 1)
        {
            return new ConfigurationResult.Failure("Upstream weight must be at least 1.");
        }

        if (upstream.MaxConnections < 1)
        {
            return new ConfigurationResult.Failure("Upstream max connections must be at least 1.");
        }

        return ConfigurationResult.Success.Instance;
    }

    public static ConfigurationResult ValidateRemoveUpstream(
        DomainName domainName, UpstreamUrl upstreamUrl, ConfigurationState state)
    {
        if (!state.HasDomain(domainName))
        {
            return new ConfigurationResult.Failure($"Domain '{domainName}' does not exist.");
        }

        if (!state.HasUpstream(domainName, upstreamUrl))
        {
            return new ConfigurationResult.Failure(
                $"Upstream '{upstreamUrl}' does not exist for domain '{domainName}'.");
        }

        return ConfigurationResult.Success.Instance;
    }

    public static ConfigurationResult ValidateDomainFormat(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return new ConfigurationResult.Failure("Domain name cannot be empty.");
        }

        if (!DomainName.TryParse(domain, out _))
        {
            return new ConfigurationResult.Failure($"Invalid domain name format: '{domain}'.");
        }

        return ConfigurationResult.Success.Instance;
    }

    public static ConfigurationResult ValidateUpstreamUrlFormat(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new ConfigurationResult.Failure("Upstream URL cannot be empty.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return new ConfigurationResult.Failure($"Invalid upstream URL format: '{url}'.");
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return new ConfigurationResult.Failure(
                $"Upstream URL must use http or https scheme, got '{uri.Scheme}'.");
        }

        return ConfigurationResult.Success.Instance;
    }

    private static ConfigurationResult ValidateRequestTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return new ConfigurationResult.Failure("Request timeout must be positive.");
        }

        if (timeout > TimeSpan.FromMinutes(10))
        {
            return new ConfigurationResult.Failure("Request timeout cannot exceed 10 minutes.");
        }

        return ConfigurationResult.Success.Instance;
    }
}
