using Microsoft.AspNetCore.Mvc;
using Schleusenwerk.Api;
using Schleusenwerk.Persistence;

namespace Schleusenwerk.Controllers;

[ApiController]
[Route("api/settings")]
public sealed class SettingsController : ControllerBase
{
    private readonly IConfigurationStore _store;

    public SettingsController(IConfigurationStore store) => _store = store;

    [HttpGet]
    public async Task<ActionResult<ProxySettingsDto>> GetSettings(CancellationToken ct)
    {
        var settings = await _store.GetSettingsAsync(ct);
        return Ok(new ProxySettingsDto(
            Stage: settings.Stage.ToString().ToLowerInvariant(),
            AcmeEmail: settings.AcmeEmail,
            DnsProvider: settings.DnsProvider,
            DefaultRequestTimeoutSeconds: (int)settings.DefaultRequestTimeout.TotalSeconds,
            MaxConnectionsPerUpstream: settings.MaxConnectionsPerUpstream,
            ForceHttpsGlobally: settings.ForceHttpsGlobally));
    }

    [HttpPut]
    public async Task<ActionResult<CommandResultDto>> UpdateSettings(
        [FromBody] UpdateSettingsRequestDto request, CancellationToken ct)
    {
        var settings = await _store.GetSettingsAsync(ct);

        if (request.Stage is not null)
        {
            var parsed = request.Stage.ToLowerInvariant() switch
            {
                "local" => AcmeStage.Local,
                "staging" => AcmeStage.Staging,
                "production" => AcmeStage.Production,
                _ => (AcmeStage?)null,
            };
            if (parsed is null) { return Ok(CommandResultDto.Fail($"Invalid stage: {request.Stage}")); }
            settings = settings with { Stage = parsed.Value };
        }
        if (request.AcmeEmail is not null) { settings = settings with { AcmeEmail = request.AcmeEmail }; }
        if (request.DnsProvider is not null) { settings = settings with { DnsProvider = request.DnsProvider }; }
        if (request.DefaultRequestTimeoutSeconds.HasValue) { settings = settings with { DefaultRequestTimeout = TimeSpan.FromSeconds(request.DefaultRequestTimeoutSeconds.Value) }; }
        if (request.MaxConnectionsPerUpstream.HasValue) { settings = settings with { MaxConnectionsPerUpstream = request.MaxConnectionsPerUpstream.Value }; }
        if (request.ForceHttpsGlobally.HasValue) { settings = settings with { ForceHttpsGlobally = request.ForceHttpsGlobally.Value }; }

        await _store.UpdateSettingsAsync(settings, ct);
        return Ok(CommandResultDto.Ok());
    }
}
