using CounterpartyRisk.ServiceDefaults;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

// TODO(#19): Quartz.NET scheduler, ingestion jobs and engine runs dispatched through IDispatcher.

var host = builder.Build();
await host.RunAsync().ConfigureAwait(false);
