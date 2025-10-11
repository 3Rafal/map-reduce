using ApiService.Models;
using ApiService.Services;
using MassTransit;
using Shared.Utils;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ConfigureDefaultLogging();

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

builder.Services.AddOptions<CoordinatorOptions>()
    .Bind(builder.Configuration.GetSection("Coordinator"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var rabbitSection = builder.Configuration.GetSection("RabbitMq");

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<MapResultConsumer>();
    x.AddConsumer<ReduceResultConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        var hostName = rabbitSection["HostName"] ?? "localhost";
        var port = rabbitSection.GetValue<int>("Port", 5672);
        var virtualHost = rabbitSection["VirtualHost"] ?? "/";
        var userName = rabbitSection["UserName"] ?? "guest";
        var password = rabbitSection["Password"] ?? "guest";
        var useSsl = rabbitSection.GetValue("UseSsl", false);

        var hostUri = new UriBuilder("rabbitmq", hostName, port, virtualHost.TrimStart('/')).Uri;

        cfg.Host(hostUri, h =>
        {
            h.Username(userName);
            h.Password(password);
            if (useSsl)
            {
                h.UseSsl();
            }
        });

        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddMinioClient(builder.Configuration);
builder.Services.AddSingleton<JobCoordinator>();
builder.Services.AddSingleton<IQueuePublisher, QueuePublisher>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
