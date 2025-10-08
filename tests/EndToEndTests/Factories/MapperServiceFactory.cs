extern alias MapperServiceAlias;

using EndToEndTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EndToEndTests.Factories;

public sealed class MapperServiceFactory : WebApplicationFactory<MapperServiceAlias::Program>
{
    private readonly MinioFixture _minio;
    private Func<HttpMessageHandler>? _callbackHandlerFactory;

    public MapperServiceFactory(MinioFixture minio)
    {
        _minio = minio;
    }

    public void UseCallbackHandler(Func<HttpMessageHandler> handlerFactory)
    {
        _callbackHandlerFactory = handlerFactory;
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
                ["Minio:BucketName"] = _minio.BucketName
            };

            config.AddInMemoryCollection(overrides);
        });

        builder.ConfigureServices(services =>
        {
            if (_callbackHandlerFactory is not null)
            {
                services.AddHttpClient("Callback")
                    .ConfigurePrimaryHttpMessageHandler(_ => _callbackHandlerFactory!());
            }
        });
    }
}
