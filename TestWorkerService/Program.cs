using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using TestWorkerService;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHostedService<Worker>();
builder.Services.AddControllers();
builder.Services.AddDbContext<SensorDataContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});
builder.Services.AddSwaggerGen();
var app = builder.Build();
app.MapControllers();

app.UseSwagger();
app.UseSwaggerUI();
app.Run();