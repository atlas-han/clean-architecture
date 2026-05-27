using System.Linq;
using System.Text.Json;
using CleanArchitecture.Api.Filters;
using CleanArchitecture.Application;
using CleanArchitecture.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// "application" liveness check; the "database" check is registered in AddInfrastructure.
builder.Services.AddHealthChecks()
    .AddCheck("application", () => HealthCheckResult.Healthy("Application is running"));

builder.Services.AddControllers(options =>
{
    options.Filters.Add<ApiExceptionFilter>();
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CleanArchitecture API",
        Version = "v1",
        Description = "ASP.NET Core 9 (C# 9 LangVersion) Clean Architecture sample."
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Local"))
{
    app.UseDeveloperExceptionPage();
}

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "CleanArchitecture v1");
    c.RoutePrefix = string.Empty;
});

app.UseHttpsRedirection();
app.MapControllers();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description
            })
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
});

app.Run();

public partial class Program { }
