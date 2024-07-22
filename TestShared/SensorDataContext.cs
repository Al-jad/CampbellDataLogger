using Microsoft.EntityFrameworkCore;

namespace TestWorkerService;

public class SensorDataContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<SensorData> SensorData { get; set; }
    public DbSet<Station> Stations { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
    }
}