using Newtonsoft.Json;

namespace TestWorkerService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceProvider _serviceProvider;

    public Worker(ILogger<Worker> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }

            var stations = GetAllStations();

            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SensorDataContext>();

            foreach (var station in stations)
            {
                if (!File.Exists(station.DataFile))
                    continue;

                var sensorData = SensorDataMap.ParseCsvFile(station.DataFile);

                foreach (var data in sensorData)
                {
                    data.Station = station.Name;
                }
                
                context.SensorData.AddRange(sensorData);
                var isSaved = await context.SaveChangesAsync(stoppingToken) > 0;

                if (isSaved)
                    File.Move(station.DataFile, station.UploadedDataFile, true);
            }

            await Task.Delay(10000, stoppingToken);
        }
    }

    private List<Station> GetAllStations()
    {
        
        var jsonText = File.ReadAllText("appsettings.json");
        
        var appSettings = JsonConvert.DeserializeObject<AppSettings>(jsonText);
        return appSettings.Stations;
    }
}