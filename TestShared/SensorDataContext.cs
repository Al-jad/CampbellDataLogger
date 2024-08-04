using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace TestWorkerService;

public class SensorDataContext(DbContextOptions<SensorDataContext> options) : IdentityDbContext<IdentityUser<long>, IdentityRole<long>, long>(options)
{
    public DbSet<SensorData> SensorData { get; set; }
    public DbSet<Station> Stations { get; set; }
}