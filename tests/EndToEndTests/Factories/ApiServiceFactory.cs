extern alias ApiServiceAlias;
extern alias MapperServiceAlias;
extern alias ReducerServiceAlias;

using EndToEndTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EndToEndTests.Factories;

public sealed class ApiServiceFactory : WebApplicationFactory<ApiServiceAlias::Program>
{
    private readonly MinioFixture _minio;
    private readonly MapperServiceFactory _mapperFactory;
    private readonly ReducerServiceFactory _reducerFactory;

    private Uri MapperServerBaseAddress => _mapperFactory.Server.BaseAddress;

    private Uri ReducerServerBaseAddress => _reducerFactory.Server.BaseAddress;

    public ApiServiceFactory(MinioFixture minio, MapperServiceFactory mapperFactory, ReducerServiceFactory reducerFactory)
    {
        _minio = minio;
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
                ["Minio:Endpoint"] = _minio.Endpoint,
                ["Minio:Port"] = _minio.Port.ToString(),
                ["Minio:UseSsl"] = "false",
                ["Minio:AccessKey"] = _minio.AccessKey,
                ["Minio:SecretKey"] = _minio.SecretKey,
                ["Minio:BucketName"] = _minio.BucketName,
                ["Coordinator:MapperBaseUrl"] = MapperServerBaseAddress.ToString(),
                ["Coordinator:ReducerBaseUrl"] = ReducerServerBaseAddress.ToString(),
                ["Coordinator:CallbackBaseUrl"] = "http://localhost/"
            };

            config.AddInMemoryCollection(overrides);
        });

        builder.ConfigureServices(services =>
        {
            services.AddHttpClient("Mapper")
                .ConfigureHttpClient(client => client.BaseAddress = MapperServerBaseAddress)
                .ConfigurePrimaryHttpMessageHandler(() => _mapperFactory.Server.CreateHandler());

            services.AddHttpClient("Reducer")
                .ConfigureHttpClient(client => client.BaseAddress = ReducerServerBaseAddress)
                .ConfigurePrimaryHttpMessageHandler(() => _reducerFactory.Server.CreateHandler());
        });
    }
}
