using Newtonsoft.Json;

namespace TestWorkerService;

public class Worker(ILogger<Worker> logger, IServiceProvider serviceProvider) : BackgroundService
{
    private readonly AppSettings appSetting = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText("appsettings.json"));
    private readonly ILogger<Worker> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;

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

            await Task.Delay(appSetting.Delay, stoppingToken);
        }
    }

    private List<Station> GetAllStations()
    {
        return appSetting.Stations;
    }
}