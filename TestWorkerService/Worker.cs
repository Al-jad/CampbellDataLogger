using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace TestWorkerService;

public class Worker(ILogger<Worker> logger, IServiceProvider serviceProvider) : BackgroundService
{
    private readonly AppSettings? appSettings = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText("appsettings.json"));
    private readonly ILogger<Worker> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (appSettings is null) throw new ArgumentNullException(nameof(appSettings));
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            }

            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SensorDataContext>();

            if (!context.Stations.Any(x => x.SourceAddress.Equals(appSettings.SourceAddress, StringComparison.OrdinalIgnoreCase)))
            {
                await context.Stations.AddRangeAsync(appSettings.Stations, stoppingToken);
                var station = await context.Stations.FirstAsync(x => x.SourceAddress == appSettings.SourceAddress, stoppingToken);
                await context.SensorData.Where(x => x.Station == null).ExecuteUpdateAsync(x => x.SetProperty(x => x.StationId, station.Id), stoppingToken);
            }

            var stations = await context.Stations.Where(x => x.SourceAddress.Equals(appSettings.SourceAddress, StringComparison.OrdinalIgnoreCase)).ToListAsync(stoppingToken);

            foreach (var station in stations)
            {
                if (!File.Exists(station.DataFile))
                    continue;

                var sensorData = SensorDataMap.ParseCsvFile(station.DataFile);

                foreach (var data in sensorData)
                {
                    data.StationId = station.Id;
                }
                
                context.SensorData.AddRange(sensorData);
                var isSaved = await context.SaveChangesAsync(stoppingToken) > 0;

                if (isSaved && station.UploadedDataFile != null)
                    File.Move(station.DataFile, station.UploadedDataFile, true);
            }

            await Task.Delay(appSettings.Delay, stoppingToken);
        }
    }
}