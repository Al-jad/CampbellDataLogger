using System.ComponentModel.DataAnnotations;

namespace TestWorkerService;

public class SensorData
{
    public long Id { get; set; }
    public string TimeStamp { get; set; }
    public int Record { get; set; }
    public string WL { get; set; }
    public double BatteryVoltage { get; set; }

    public string Station { get; set; }
}
