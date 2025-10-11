using EndToEndTests.Fixtures;
using Xunit;

namespace EndToEndTests;

[CollectionDefinition("EndToEnd")]
public sealed class EndToEndCollection : ICollectionFixture<MinioFixture>
{
}

[CollectionDefinition("Kubernetes")]
public sealed class KubernetesCollection : ICollectionFixture<KubernetesFixture>
{
}
