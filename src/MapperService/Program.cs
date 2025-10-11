using MassTransit;
using MapperService.Services;
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

var rabbitSection = builder.Configuration.GetSection("RabbitMq");

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<MapConsumer>();

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
builder.Services.AddHttpClient("Callback");
builder.Services.AddScoped<WordCountMapper>();

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
