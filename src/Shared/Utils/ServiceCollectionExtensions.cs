using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Models;

namespace Shared.Utils;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMqMassTransit(this IServiceCollection services, Action<IBusRegistrationConfigurator> registerConsumers)
    
    {
        services.AddOptions<RabbitMqOptions>()
            .BindConfiguration(RabbitMqOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddMassTransit(config =>
        {
            registerConsumers(config);

            config.UsingRabbitMq((context, cfg) =>
            {
                var options = context.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
                var hostUri = new UriBuilder("rabbitmq", options.HostName, options.Port, options.VirtualHost.TrimStart('/')).Uri;

                cfg.Host(hostUri, h =>
                {
                    h.Username(options.UserName);
                    h.Password(options.Password);
                    if (options.UseSsl)
                    {
                        h.UseSsl();
                    }
                });

                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }
}
