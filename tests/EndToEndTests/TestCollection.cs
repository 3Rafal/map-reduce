using EndToEndTests.Fixtures;
using Xunit;

namespace EndToEndTests;

[CollectionDefinition("EndToEnd")]
public sealed class EndToEndCollection : ICollectionFixture<MinioFixture>
{
}
