using System.Text.Json.Serialization;
using PerfumeShop.Api;
using PerfumeShop.Shared.Infrastructure.Auth;
using PerfumeShop.Shared.Infrastructure.Http;
using PerfumeShop.Shared.Infrastructure.Jobs;
using PerfumeShop.Shared.Infrastructure.Modules;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options => options.AddFlagsEnumNames());
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DatabaseConflictExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    // Numbers are numbers: "12" is rejected, and OpenAPI (→ the TypeScript client) types them as number only.
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
builder.Services.AddPlatformAuth(builder.Environment);
builder.Services.AddPlatformBackgroundJobs(builder.Configuration);
builder.Services.AddModules(ModuleRegistry.All, builder.Configuration, builder.Environment);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // /openapi/v1.json, also the source for web/packages/api-client
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "PerfumeShop API v1"));
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapPlatformJobDashboard();

foreach (var module in ModuleRegistry.All)
{
    module.MapEndpoints(app);
}

app.Run();

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
