using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TestWorkerService;

namespace MNGWorkerService
{
    public class Worker(ILogger<Worker> logger, IConfiguration config, IServiceProvider serviceProvider) : BackgroundService
    {
        private readonly MSGNAppSettings? appSettings = config.Get<MSGNAppSettings>();
        private readonly ILogger<Worker> _logger = logger;
        private readonly IServiceProvider _serviceProvider = serviceProvider;
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (appSettings is null) throw new ArgumentNullException(nameof(appSettings));

            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SensorDataContext>();

            HttpClient httpClient = new() { BaseAddress = new Uri(appSettings.ApiBaseUrl) };
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {appSettings.AccessToken}");

            JsonSerializerOptions serializerOptions = new() { WriteIndented = true };

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_logger.IsEnabled(LogLevel.Information)) _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                //find the last page number
                var t = await httpClient.GetFromJsonAsync<LastPageResponse>("api/stations/maindata", stoppingToken);
                if (t is not null)
                {
                    List<Station> currentStations = await context.Stations
                        .Where(x => x.SourceAddress == appSettings.SourceAddress)
                        .AsTracking().ToListAsync(cancellationToken: stoppingToken);

                    if (currentStations.Count == 0)
                    {
                        //use file to populate the database faster
                        Station[]? jsonStations = JsonSerializer.Deserialize<Station[]>(File.ReadAllText("dbStations.json"));
                        if (jsonStations != null && jsonStations.Length != 0)
                        {
                            foreach (var station in jsonStations)
                            {
                                station.Name ??= $"UnNamed Station ID: {station.ExternalId}";
                            }
                            _logger.LogInformation("Stations Count: {count}", jsonStations.Length);
                            await context.Stations.AddRangeAsync(jsonStations, stoppingToken);

                            _logger.LogInformation("Changes Count: {count}", await context.SaveChangesAsync(stoppingToken));
                        }
                    }

                    DateTime? LastEntryDate = await context.SensorData
                        .Where(x => x.Station != null && x.Station.SourceAddress == appSettings.SourceAddress && x.TimeStamp != null)
                        .OrderByDescending(x => x.TimeStamp)
                        .Select(x => x.TimeStamp)
                        .FirstOrDefaultAsync(cancellationToken: stoppingToken);

                    HashSet<MainDataStation> stations = [];
                    List<MainDataSensor> sensorData = [];
                    for (var i = t.last_page; i > 1; i--)
                    {
                        _logger.LogInformation("Request made at Page: {page} running at: {time}", i, DateTimeOffset.Now);
                        var response = await GetData(httpClient, $"api/stations/maindata?page={i}", stoppingToken);
                        if (response is not null)
                        {
                            foreach (var station in response.data.Select(x => x.station).Where(x => !currentStations.Select(x => x.ExternalId).Contains(x.id.ToString())))
                            {
                                stations.Add(station);
                            }
                            sensorData.AddRange(response.data.Where(x => x.created_at > LastEntryDate));
                            if (response.data.First().created_at <= LastEntryDate) break;
                        }
                    }

                    Station[] dbStations = stations.Select(x => new Station
                    {
                        Name = x.Name ?? $"UnNamed Station ID: {x.id}",
                        SourceAddress = appSettings.SourceAddress,
                        Lat = x.latitude,
                        Lng = x.longitude,
                        ExternalId = x.id.ToString(),
                        SensorData = sensorData.Where(s => s.stations_id == x.id).Select(x => new SensorData
                        {
                            TimeStamp = DateTime.TryParse($"{x.DATE} {x.TIME}", out DateTime parsedDate)
                                && parsedDate <= x.created_at//to invalidate wrong or null dates
                                    ? parsedDate
                                    : x.created_at,
                            Record = (int)x.id,
                            WL = x.LEVEl.ToString(),
                        }).ToList()
                    }).ToArray();
                    await context.Stations.AddRangeAsync(dbStations, stoppingToken);

                    foreach (var station in currentStations)
                    {
                        var sensordata = sensorData.Where(s => s.stations_id.ToString() == station.ExternalId).Select(x => new SensorData
                        {
                            TimeStamp = DateTime.TryParse($"{x.DATE} {x.TIME}", out DateTime parsedDate)
                                ? parsedDate.ToUniversalTime()
                                : null,
                            Record = (int)x.id,
                            WL = x.LEVEl.ToString(),
                            StationId = station.Id
                        });
                        await context.SensorData.AddRangeAsync(sensordata, stoppingToken);
                    }

                    _logger.LogInformation("Changes Count: {count}", await context.SaveChangesAsync(stoppingToken));

                    _logger.LogInformation("Next run in: {count} minutes", appSettings.Delay / 1000 / 60);
                }
                else _logger.LogError("Failed to Retrieve Data at: {time}", DateTimeOffset.Now);

                await Task.Delay(appSettings.Delay, stoppingToken);
            }
        }

        public async Task<MainDataResponse> GetData(HttpClient httpClient, string path, CancellationToken stoppingToken, int delay = 5000)
        {
            try
            {
                return await httpClient.GetFromJsonAsync<MainDataResponse>(path, stoppingToken)
                 ?? throw new HttpRequestException("No Data returned");
            }
            catch (HttpRequestException exception)
            {
                if (exception.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _logger.LogError(exception.Message);
                    _logger.LogInformation("Retrying in {delay} seconds", delay / 1000);//ms
                    await Task.Delay(delay, stoppingToken);
                    return await GetData(httpClient, path, stoppingToken, delay);
                }
                else
                {
                    throw;
                }
            }
        }
    }
    public record LastPageResponse(int last_page);
    public record MainDataResponse(int current_page, int last_page, List<MainDataSensor> data);
    public record MainDataSensor(long id, long stations_id, float LEVEl, string DATE, string TIME, DateTime created_at, MainDataStation station);
    public record MainDataStation(long id, int Station, string? Name, double? latitude, double? longitude);
}
