using Microsoft.EntityFrameworkCore;

namespace TestWorkerService;

public class SensorDataContext : DbContext
{
    public SensorDataContext(DbContextOptions options) : base(options)
    {
        
    }

    public DbSet<SensorData> SensorData { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
       
    }
}