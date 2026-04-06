using System.ComponentModel.DataAnnotations;

namespace TestWorkerService;

public class SensorData
{
    public long Id { get; set; }
    public DateTime? TimeStamp { get; set; }
    public int Record { get; set; }
    [StringLength(16)] public required string WL { get; set; }
    public double? BatteryVoltage { get; set; }
    public double? Salt { get; set; }
    public long? StationId { get; set; }
    public Station? Station { get; set; }
}