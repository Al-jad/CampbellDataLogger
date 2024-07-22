using Microsoft.EntityFrameworkCore;
using MNGWorkerService;
using TestWorkerService;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

builder.Services.AddDbContext<SensorDataContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

var host = builder.Build();
host.Run();
