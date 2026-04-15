using Microsoft.AspNetCore.Http;
using Schleusenwerk.Forwarding;
using Xunit;

namespace Schleusenwerk.Tests.Forwarding;

public sealed class HeaderManipulationFilterSpec
{
    private static IHeaderDictionary CreateHeaders(params (string Name, string Value)[] headers)
    {
        var dict = new HeaderDictionary();
        foreach (var (name, value) in headers)
        {
            dict[name] = value;
        }
        return dict;
    }

    [Fact(Timeout = 5000)]
    public void Apply_should_remove_server_header_by_default()
    {
        var filter = new HeaderManipulationFilter();
        var headers = CreateHeaders(("Server", "nginx/1.24"), ("Content-Type", "text/html"));

        filter.Apply(headers);

        Assert.False(headers.ContainsKey("Server"));
        Assert.True(headers.ContainsKey("Content-Type"));
    }

    [Fact(Timeout = 5000)]
    public void Apply_should_remove_x_powered_by_header_by_default()
    {
        var filter = new HeaderManipulationFilter();
        var headers = CreateHeaders(("X-Powered-By", "ASP.NET"), ("Content-Type", "text/html"));

        filter.Apply(headers);

        Assert.False(headers.ContainsKey("X-Powered-By"));
        Assert.True(headers.ContainsKey("Content-Type"));
    }

    [Fact(Timeout = 5000)]
    public void Apply_should_remove_both_server_and_x_powered_by()
    {
        var filter = new HeaderManipulationFilter();
        var headers = CreateHeaders(
            ("Server", "Apache/2.4"),
            ("X-Powered-By", "PHP/8.2"),
            ("X-Custom", "keep"));

        filter.Apply(headers);

        Assert.False(headers.ContainsKey("Server"));
        Assert.False(headers.ContainsKey("X-Powered-By"));
        Assert.Equal("keep", headers["X-Custom"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void Apply_should_set_via_header_with_proxy_identification()
    {
        var filter = new HeaderManipulationFilter();
        var headers = CreateHeaders();

        filter.Apply(headers);

        Assert.Equal("1.1 Schleusenwerk", headers["Via"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void Apply_should_use_custom_protocol_version_in_via_header()
    {
        var filter = new HeaderManipulationFilter([], "2.0");
        var headers = CreateHeaders();

        filter.Apply(headers);

        Assert.Equal("2.0 Schleusenwerk", headers["Via"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void Apply_should_add_header_when_not_present()
    {
        var rules = new[]
        {
            new HeaderRule
            {
                Action = HeaderRuleAction.Add,
                HeaderName = "X-Frame-Options",
                Value = "DENY",
            },
        };
        var filter = new HeaderManipulationFilter(rules);
        var headers = CreateHeaders();

        filter.Apply(headers);

        Assert.Equal("DENY", headers["X-Frame-Options"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void Apply_should_not_overwrite_existing_header_on_add()
    {
        var rules = new[]
        {
            new HeaderRule
            {
                Action = HeaderRuleAction.Add,
                HeaderName = "X-Frame-Options",
                Value = "DENY",
            },
        };
        var filter = new HeaderManipulationFilter(rules);
        var headers = CreateHeaders(("X-Frame-Options", "SAMEORIGIN"));

        filter.Apply(headers);

        Assert.Equal("SAMEORIGIN", headers["X-Frame-Options"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void Apply_should_remove_header_via_custom_rule()
    {
        var rules = new[]
        {
            new HeaderRule
            {
                Action = HeaderRuleAction.Remove,
                HeaderName = "X-Debug-Info",
            },
        };
        var filter = new HeaderManipulationFilter(rules);
        var headers = CreateHeaders(("X-Debug-Info", "trace-id-123"));

        filter.Apply(headers);

        Assert.False(headers.ContainsKey("X-Debug-Info"));
    }

    [Fact(Timeout = 5000)]
    public void Apply_should_replace_existing_header()
    {
        var rules = new[]
        {
            new HeaderRule
            {
                Action = HeaderRuleAction.Replace,
                HeaderName = "Cache-Control",
                Value = "no-store",
            },
        };
        var filter = new HeaderManipulationFilter(rules);
        var headers = CreateHeaders(("Cache-Control", "public, max-age=3600"));

        filter.Apply(headers);

        Assert.Equal("no-store", headers["Cache-Control"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void Apply_should_set_header_via_replace_even_when_absent()
    {
        var rules = new[]
        {
            new HeaderRule
            {
                Action = HeaderRuleAction.Replace,
                HeaderName = "Strict-Transport-Security",
                Value = "max-age=31536000",
            },
        };
        var filter = new HeaderManipulationFilter(rules);
        var headers = CreateHeaders();

        filter.Apply(headers);

        Assert.Equal("max-age=31536000", headers["Strict-Transport-Security"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void Apply_should_process_multiple_rules_in_order()
    {
        var rules = new[]
        {
            new HeaderRule
            {
                Action = HeaderRuleAction.Remove,
                HeaderName = "X-Internal",
            },
            new HeaderRule
            {
                Action = HeaderRuleAction.Add,
                HeaderName = "X-Proxy",
                Value = "schleusenwerk",
            },
            new HeaderRule
            {
                Action = HeaderRuleAction.Replace,
                HeaderName = "Cache-Control",
                Value = "private",
            },
        };
        var filter = new HeaderManipulationFilter(rules);
        var headers = CreateHeaders(
            ("X-Internal", "secret"),
            ("Cache-Control", "public"));

        filter.Apply(headers);

        Assert.False(headers.ContainsKey("X-Internal"));
        Assert.Equal("schleusenwerk", headers["X-Proxy"].ToString());
        Assert.Equal("private", headers["Cache-Control"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void Apply_should_handle_empty_response_headers()
    {
        var filter = new HeaderManipulationFilter();
        var headers = CreateHeaders();

        filter.Apply(headers);

        Assert.Equal("1.1 Schleusenwerk", headers["Via"].ToString());
    }

    [Fact(Timeout = 5000)]
    public void Apply_should_not_fail_when_default_remove_headers_are_absent()
    {
        var filter = new HeaderManipulationFilter();
        var headers = CreateHeaders(("Content-Type", "text/plain"));

        filter.Apply(headers);

        Assert.False(headers.ContainsKey("Server"));
        Assert.False(headers.ContainsKey("X-Powered-By"));
        Assert.True(headers.ContainsKey("Content-Type"));
    }
}
