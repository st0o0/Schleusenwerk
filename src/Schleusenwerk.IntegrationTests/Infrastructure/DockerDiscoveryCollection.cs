using Xunit;

namespace Schleusenwerk.IntegrationTests.Infrastructure;

[CollectionDefinition("DockerDiscovery")]
public sealed class DockerDiscoveryCollection :
    ICollectionFixture<DockerDiscoveryTestHost>;
