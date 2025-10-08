using ApiService.Models;
using ApiService.Services;
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

builder.Services.AddHttpClient();
builder.Services.AddHttpClient("Mapper");
builder.Services.AddHttpClient("Reducer");

builder.Services.AddMinioClient(builder.Configuration);
builder.Services.AddSingleton<JobCoordinator>();

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
