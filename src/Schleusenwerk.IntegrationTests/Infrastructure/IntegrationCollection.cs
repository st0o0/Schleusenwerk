using Xunit;

namespace Schleusenwerk.IntegrationTests.Infrastructure;

[CollectionDefinition("Integration")]
public sealed class IntegrationCollection :
    ICollectionFixture<SchleusenwerkTestHost>,
    ICollectionFixture<EchoServerFixture>,
    ICollectionFixture<NginxFixture>,
    ICollectionFixture<WebSocketEchoFixture>,
    ICollectionFixture<ToxiproxyFixture>;
