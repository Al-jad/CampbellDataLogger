using Microsoft.EntityFrameworkCore;
using TestWorkerService;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAnyOrigin",
        builder => builder.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader());
});
builder.Services.AddDbContext<SensorDataContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});
builder.Services.AddSwaggerGen();
var app = builder.Build();
app.MapControllers();
app.UseCors("AllowAnyOrigin");
app.UseSwagger();
app.UseSwaggerUI();
app.Run();