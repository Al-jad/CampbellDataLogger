using System.ComponentModel.DataAnnotations;

namespace TestWorkerService;

public class SensorData
{
    public long Id { get; set; }
    public DateTime TimeStamp { get; set; }
    public int Record { get; set; }
    public required string WL { get; set; }
    public double BatteryVoltage { get; set; }
    public required string Station { get; set; }
}
