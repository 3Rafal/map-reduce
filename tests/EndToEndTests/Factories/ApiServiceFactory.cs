extern alias ApiServiceAlias;
extern alias MapperServiceAlias;
extern alias ReducerServiceAlias;

using EndToEndTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace EndToEndTests.Factories;

public sealed class ApiServiceFactory : WebApplicationFactory<ApiServiceAlias::Program>
{
    private readonly MinioFixture _minio;
    private readonly RabbitMqFixture _rabbitMq;
    private readonly MapperServiceFactory _mapperFactory;
    private readonly ReducerServiceFactory _reducerFactory;

    private Uri MapperServerBaseAddress => _mapperFactory.Server.BaseAddress;

    private Uri ReducerServerBaseAddress => _reducerFactory.Server.BaseAddress;

    public ApiServiceFactory(MinioFixture minio, RabbitMqFixture rabbitMq, MapperServiceFactory mapperFactory, ReducerServiceFactory reducerFactory)
    {
        _minio = minio;
        _rabbitMq = rabbitMq;
        _mapperFactory = mapperFactory;
        _reducerFactory = reducerFactory;
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
                ["Coordinator:MapperBaseUrl"] = MapperServerBaseAddress.ToString(),
                ["Coordinator:ReducerBaseUrl"] = ReducerServerBaseAddress.ToString(),
                ["Coordinator:CallbackBaseUrl"] = "http://localhost/",
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
