namespace TestWorkerService;

public class Station
{
    public long Id { get; set; }
    public string? ExternalId { get; set; }
    public required string SourceAddress { get; set; }
    public required string Name { get; set; }
    public string? DataFile { get; set; }
    public string? UploadedDataFile { get; set; }
    public double? Lat { get; set; }
    public double? Lng { get; set; }
    public string? Description { get; set; }
    public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;
    public List<SensorData> SensorData { get; set; } = [];
}

public class AppSettings
{
    public List<Station> Stations { get; set; } = [];
    public required string SourceAddress { get; init; }
    public int Delay { get; set; }
}

public class MSGNAppSettings : AppSettings
{
    public required string ApiBaseUrl { get; init; }
    public required string AccessToken { get; init; }
}