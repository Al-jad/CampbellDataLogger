using Microsoft.EntityFrameworkCore;

namespace TestWorkerService;

public class SensorDataContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<SensorData> SensorData { get; set; }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
    }
}