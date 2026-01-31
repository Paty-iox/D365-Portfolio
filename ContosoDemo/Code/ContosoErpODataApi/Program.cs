using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ContosoErpODataApi.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Register SqlDataService
var connectionString = Environment.GetEnvironmentVariable("SqlConnectionString")
    ?? throw new InvalidOperationException("SqlConnectionString not configured");

builder.Services.AddSingleton<ISqlDataService>(new SqlDataService(connectionString));
builder.Services.AddSingleton<IComplianceDataService, ComplianceDataService>();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Build().Run();
