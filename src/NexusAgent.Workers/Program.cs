using NexusAgent.Infrastructure;
using NexusAgent.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<DocumentIndexWorker>();

var host = builder.Build();
await host.RunAsync();