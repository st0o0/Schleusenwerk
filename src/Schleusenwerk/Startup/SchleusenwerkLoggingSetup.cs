using Serilog;
using Serilog.Sinks.OpenTelemetry;
using Servus.Core.Application.Startup;

namespace Schleusenwerk.Startup;

public sealed class SchleusenwerkLoggingSetup : IHostBuilderSetupContainer
{
    public void ConfigureHostBuilder(IHostBuilder hostBuilder)
    {
        hostBuilder.UseSerilog((context, configuration) =>
        {
            configuration.ReadFrom.Configuration(context.Configuration);

            var otlpEndpoint = context.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                configuration.WriteTo.OpenTelemetry(options =>
                {
                    options.Endpoint = $"{otlpEndpoint.TrimEnd('/')}/v1/logs";
                    options.Protocol = OtlpProtocol.HttpProtobuf;
                    options.ResourceAttributes = new Dictionary<string, object>
                    {
                        ["service.name"] = context.Configuration["OTEL_SERVICE_NAME"] ?? "schleusenwerk",
                    };
                });
            }
        });
    }
}
