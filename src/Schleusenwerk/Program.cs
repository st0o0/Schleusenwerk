using Schleusenwerk.Startup;
using Servus.Core.Application.Startup;

var runner = AppBuilder.Create()
    .WithSetup<SchleusenwerkActorSystemSetup>()
    .WithSetup<SchleusenwerkServicesSetup>()
    .WithSetup<SchleusenwerkApplicationSetup>()
    .Build();

await runner.RunAsync();
