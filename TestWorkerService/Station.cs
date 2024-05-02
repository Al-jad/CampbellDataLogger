namespace TestWorkerService;

public class Station
{
    public string Name { get; set; }
    public string DataFile { get; set; }
    public string UploadedDataFile { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public string Description { get; set; }
}


public class AppSettings
{
    public List<Station> Stations { get; set; }
    public int Delay { get; set; }
}