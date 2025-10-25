using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using TestWorkerService;

namespace EmusatWorkerService
{
    public class Worker(ILogger<Worker> logger, IConfiguration config, IServiceProvider serviceProvider) : BackgroundService
    {
        private readonly ILogger<Worker> _logger = logger;
        private readonly EMUAppSettings? appSettings = config.Get<EMUAppSettings>();
        private readonly IServiceProvider _serviceProvider = serviceProvider;

        private readonly byte[] hgSequence = [0x20, 0x23, 0xB6, 0xB0, 0x20];
        private readonly byte[] eotSequence = [0x20, 0xBB, 0x53, 0xC6];
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (appSettings is null) throw new ArgumentNullException(nameof(appSettings));
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<SensorDataContext>();

            using HttpClient httpClient = new();
            httpClient.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br, zstd");

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
                }
                foreach (var dcpid in appSettings.DCPIDs)
                {
                    try
                    {
                        var url = $"https://service.eumetsat.int/dcswebservice/dcpAdmin.do?action=ACTION_DOWNLOAD&id={dcpid}&user={appSettings.User}&pass={appSettings.Pass}";

                        var response = await httpClient.GetAsync(url, stoppingToken);
                        if (response.IsSuccessStatusCode)
                        {
                            _logger.LogInformation("Requesting the file for Station {dcpid} at {time}", dcpid, DateTimeOffset.Now);

                            using Stream responseStream = await response.Content.ReadAsStreamAsync(stoppingToken);
                            using GZipStream decompressionStream = new(responseStream, CompressionMode.Decompress);
                            using MemoryStream memoryStream = new();

                            await decompressionStream.CopyToAsync(memoryStream, stoppingToken);
                            byte[] bytes = memoryStream.ToArray();

                            bool newHeader = bytes[0x52] == 'B';
                            int byteLength = Convert.ToInt16(newHeader
                                ? Encoding.ASCII.GetString(bytes[0x50..0x52])
                                : Encoding.ASCII.GetString(bytes[0x50..0x54])
                            , 16); //convert to hex

                            Station? station;
                            station = context.Stations.FirstOrDefault(x => x.ExternalId == dcpid);
                            if (station == null)
                            {
                                station = new Station
                                {
                                    Name = Encoding.ASCII.GetString(bytes[0x09..0x19]),
                                    SourceAddress = appSettings.SourceAddress,
                                    ExternalId = dcpid
                                };

                                context.Stations.Add(station);
                                bool success = await context.SaveChangesAsync(stoppingToken) > 0;
                                if (success) _logger.LogInformation("Added Station {dcpid} at {time}", dcpid, DateTimeOffset.Now);
                            }
                            var lastEntryDate = await context.SensorData
                                .OrderByDescending(x => x.TimeStamp)
                                .Where(x => x.StationId == station.Id)
                                .Select(x => x.TimeStamp)
                                .FirstOrDefaultAsync(stoppingToken)
                                ?? DateTime.UtcNow.AddMonths(-2);

                            int entryNum = 0;
                            int entryCount = CountSequenceOccurrences(bytes, eotSequence);
                            static int CountSequenceOccurrences(ReadOnlySpan<byte> span, ReadOnlySpan<byte> sequence)
                            {
                                int count = 0;
                                for (int i = 0; i <= span.Length - sequence.Length; i++)
                                {
                                    if (span.Slice(i, sequence.Length).SequenceEqual(sequence))
                                    {
                                        count++;
                                        i += sequence.Length - 1;
                                    }
                                }
                                return count;
                            }

                            int offset = 0;
                            List<SensorData> data = [];
                            try
                            {
                                while (offset < bytes.Length)
                                {
                                    int linewidth = FindSequence(bytes, eotSequence, offset) + 0x4 - offset;
                                    static int FindSequence(ReadOnlySpan<byte> span, ReadOnlySpan<byte> sequence, int offset = 0)
                                    {
                                        if (sequence.Length < span.Length)
                                        {
                                            for (int i = offset; i <= span.Length - sequence.Length; i++)
                                            {
                                                if (span.Slice(i, sequence.Length).SequenceEqual(sequence))
                                                {
                                                    return i;
                                                }
                                            }
                                        }
                                        throw new ArgumentException("Invalid ByteArray");
                                    }

                                    int hgIndex = FindSequenceBitError(bytes, hgSequence, offset) - offset;
                                    static int FindSequenceBitError(ReadOnlySpan<byte> span, ReadOnlySpan<byte> sequence, int offset = 0)
                                    {
                                        if (sequence.Length < span.Length)
                                        {
                                            for (int i = offset; i <= span.Length - sequence.Length; i++)
                                            {
                                                int matches = 0;
                                                // Count matching bytes at the current position
                                                for (int j = 0; j < sequence.Length; j++)
                                                {
                                                    if (span[i + j] == sequence[j])
                                                    {
                                                        matches++;
                                                    }
                                                }
                                                // Check if the number of matches meets the required threshold 3
                                                if (matches >= 3)
                                                {
                                                    return i; // Return the starting index of the match
                                                }
                                            }
                                        }
                                        throw new ArgumentException("Invalid ByteArray");
                                    }
                                    var success = AddFromBytes(bytes);
                                    bool AddFromBytes(byte[] bytes)
                                    {
                                        var row = bytes[offset..(offset + linewidth)].AsSpan();
                                        var values = ProcessSegment(row[(hgIndex - 0x5)..linewidth]).Split(' ');
                                        Console.WriteLine(string.Join(" ", values));
                                        var times = DateTime.ParseExact(ProcessSegment(row[0x37..0x48]), "dd/MM/yy HH:mm:ss", CultureInfo.InvariantCulture);
                                        var timestamp = DateTime.SpecifyKind(times, DateTimeKind.Utc);
                                        if (timestamp <= lastEntryDate) return false;

                                        if (dcpid == "1886A3C8")//HardCoded Id for IRAQ/MOWR 77
                                            data.Add(new SensorData
                                            {
                                                StationId = station.Id,
                                                WL = values[7],
                                                BatteryVoltage
                                                    = values[3] == "M" && double.TryParse(values[^4], out double batteryVoltageM) ? batteryVoltageM
                                                    : double.TryParse(values[^3], out double batteryVoltage) ? batteryVoltage
                                                    : double.TryParse(values[6], out double batteryVoltage2) ? batteryVoltage2
                                                    : double.TryParse(values[^2].Length >= 4 ? values[^2][..4] : string.Empty, out double tempBatteryVoltage3) && values[^2].Length >= 4 ? tempBatteryVoltage3
                                                    : double.TryParse(values[^4].Length >= 4 ? values[^4][..4] : string.Empty, out double tempBatteryVoltage4) && values[^4].Length >= 4 ? tempBatteryVoltage4
                                                    : double.Parse(values[15].Length >= 4 ? values[15][..4] : "0"),
                                                TimeStamp = timestamp,
                                            });
                                        else
                                            data.Add(new SensorData
                                            {
                                                StationId = station.Id,
                                                WL = values[3],
                                                BatteryVoltage
                                                    = values[3] == "M" && double.TryParse(values[^4], out double batteryVoltageM) ? batteryVoltageM
                                                    : double.TryParse(values[^3], out double batteryVoltage) ? batteryVoltage
                                                    : double.TryParse(values[6], out double batteryVoltage2) ? batteryVoltage2
                                                    : double.TryParse(values[^2].Length >= 4 ? values[^2][..4] : string.Empty, out double tempBatteryVoltage3) && values[^2].Length >= 4 ? tempBatteryVoltage3
                                                    : double.TryParse(values[^4].Length >= 4 ? values[^4][..4] : string.Empty, out double tempBatteryVoltage4) && values[^4].Length >= 4 ? tempBatteryVoltage4
                                                    : double.Parse(values[7].Length >= 4 ? values[7][..4] : "0"),
                                                TimeStamp = timestamp,
                                            });
                                        return true;
                                    }
                                    static string ProcessSegment(Span<byte> bytes)
                                    {
                                        for (int i = 0; i < bytes.Length; i++)
                                        {
                                            if (bytes[i] > 0x80) bytes[i] -= 0x80;
                                            if (bytes[i] >= 0x60) bytes[i] -= 0x40;
                                            if (bytes[i] == 0x2A) bytes[i] = 0x2E;
                                            if (bytes[i] == 0x24) bytes[i] = 0x20;
                                            if (bytes[i] == 0x00) bytes[i] = 0x20;
                                            if (bytes[i] >= 0x3C && bytes[i] < 0x40) bytes[i] -= 0x04;
                                        }
                                        return Encoding.ASCII.GetString(bytes);
                                    }
                                    if (!success)
                                    {
                                        Console.WriteLine($"Skipping Entry {entryNum += 1}:{entryCount} Date is earlier than Last saved Entry");
                                    };

                                    Console.WriteLine($"Entries: {entryNum += 1}:{entryCount}");
                                    offset += linewidth;
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex.Message);
                            }
                            await context.SensorData.AddRangeAsync(data, stoppingToken);
                            _logger.LogInformation("Added {count} Sensor Data to station {dcpid} at {time}", await context.SaveChangesAsync(stoppingToken), dcpid, DateTimeOffset.Now);
                        }
                    }
                    catch (Exception ex)
                    {
#if false
                        var innerExceptionMessage = ex.InnerException != null
                            ? $"\n*Inner Exception:*\n```\n{EscapeTelegramMarkdown(string.Join("\n", ex.InnerException.Data.Cast<DictionaryEntry>().Select(de => $"{de.Key}: {de.Value}")))}\n```"
                            : string.Empty;
                        var response = await httpClient.PostAsync($"https://api.telegram.org/bot{appSettings.Telegram.AccessToken}/sendMessage",
                            new FormUrlEncodedContent(
                            [
                                new("chat_id", appSettings.Telegram.ChatId),
                                new("message_thread_id", appSettings.Telegram.TopicId),
                                new("text", $"*Emusat Station:* `{dcpid}`\n*Error:* {EscapeTelegramMarkdown(ex.Message)}{innerExceptionMessage}"),
                                new("parse_mode", "MarkdownV2")
                            ]), stoppingToken);
                        Console.WriteLine(response);

                        Console.WriteLine(await response.Content.ReadAsStringAsync(stoppingToken));
                        throw;
#endif
                    }
                }
                _logger.LogInformation("Next run in: {count} minutes", appSettings.Delay / 1000 / 60);
                await Task.Delay(appSettings.Delay, stoppingToken);
            }
        }
        private static string EscapeTelegramMarkdown(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            return input
                .Replace("_", "\\_")
                .Replace("*", "\\*")
                .Replace("[", "\\[")
                .Replace("]", "\\]")
                .Replace("(", "\\(")
                .Replace(")", "\\)")
                .Replace("~", "\\~")
                .Replace("`", "\\`")
                .Replace(">", "\\>")
                .Replace("#", "\\#")
                .Replace("+", "\\+")
                .Replace("-", "\\-")
                .Replace("=", "\\=")
                .Replace("|", "\\|")
                .Replace("{", "\\{")
                .Replace("}", "\\}")
                .Replace(".", "\\.")
                .Replace("!", "\\!");
        }

    }
}