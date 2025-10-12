extern alias MapperServiceAlias;

using EndToEndTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace EndToEndTests.Factories;

public sealed class MapperServiceFactory : WebApplicationFactory<MapperServiceAlias::Program>
{
    private readonly MinioFixture _minio;
    private readonly RabbitMqFixture _rabbitMq;

    public MapperServiceFactory(MinioFixture minio, RabbitMqFixture rabbitMq)
    {
        _minio = minio;
        _rabbitMq = rabbitMq;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["Minio:Endpoint"] = MinioFixture.Endpoint,
                ["Minio:Port"] = _minio.Port.ToString(),
                ["Minio:UseSsl"] = "false",
                ["Minio:AccessKey"] = MinioFixture.AccessKey,
                ["Minio:SecretKey"] = MinioFixture.SecretKey,
                ["Minio:BucketName"] = MinioFixture.BucketName,
                ["RabbitMq:HostName"] = _rabbitMq.HostName,
                ["RabbitMq:Port"] = _rabbitMq.Port.ToString(),
                ["RabbitMq:UserName"] = _rabbitMq.UserName,
                ["RabbitMq:Password"] = _rabbitMq.Password,
                ["RabbitMq:VirtualHost"] = _rabbitMq.VirtualHost,
                ["RabbitMq:UseSsl"] = "false"
            };

            config.AddInMemoryCollection(overrides);
        });
    }
}
