extern alias ReducerServiceAlias;

using EndToEndTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EndToEndTests.Factories;

public sealed class ReducerServiceFactory : WebApplicationFactory<ReducerServiceAlias::Program>
{
    private readonly MinioFixture _minio;
    private readonly RabbitMqFixture _rabbitMq;

    public ReducerServiceFactory(MinioFixture minio, RabbitMqFixture rabbitMq)
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
                ["Minio:Endpoint"] = _minio.Endpoint,
                ["Minio:Port"] = _minio.Port.ToString(),
                ["Minio:UseSsl"] = "false",
                ["Minio:AccessKey"] = _minio.AccessKey,
                ["Minio:SecretKey"] = _minio.SecretKey,
                ["Minio:BucketName"] = _minio.BucketName,
                ["RabbitMq:HostName"] = _rabbitMq.HostName,
                ["RabbitMq:Port"] = _rabbitMq.Port.ToString(),
                ["RabbitMq:UserName"] = _rabbitMq.UserName,
                ["RabbitMq:Password"] = _rabbitMq.Password,
                ["RabbitMq:VirtualHost"] = _rabbitMq.VirtualHost,
                ["RabbitMq:UseSsl"] = "false"
            };

            config.AddInMemoryCollection(overrides);
        });

        builder.ConfigureServices(services =>
        {
            // Remove callback HTTP client as we now use queues instead of HTTP callbacks
        });
    }
}
