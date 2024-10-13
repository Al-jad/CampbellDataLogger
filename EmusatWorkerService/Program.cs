using EmusatWorkerService;
using Microsoft.EntityFrameworkCore;
using TestWorkerService;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

builder.Services.AddDbContext<SensorDataContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")).EnableSensitiveDataLogging();
});

var host = builder.Build();
host.Run();
