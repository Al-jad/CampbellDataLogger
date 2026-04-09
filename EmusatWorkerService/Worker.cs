using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using TestWorkerService;

namespace EmusatWorkerService
{
    public class Worker(ILogger<Worker> logger, IConfiguration config, IServiceProvider serviceProvider)
        : BackgroundService
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
                        var url =
                            $"https://service.eumetsat.int/dcswebservice/dcpAdmin.do?action=ACTION_DOWNLOAD&id={dcpid}&user={appSettings.User}&pass={appSettings.Pass}";

                        var response = await httpClient.GetAsync(url, stoppingToken);
                        if (response.IsSuccessStatusCode)
                        {
                            _logger.LogInformation("Requesting the file for Station {dcpid} at {time}", dcpid,
                                DateTimeOffset.Now);

                            using Stream responseStream = await response.Content.ReadAsStreamAsync(stoppingToken);
                            using GZipStream decompressionStream = new(responseStream, CompressionMode.Decompress);
                            using MemoryStream memoryStream = new();

                            await decompressionStream.CopyToAsync(memoryStream, stoppingToken);
                            byte[] bytes = memoryStream.ToArray();

                            if (bytes.Length < 0x55)
                            {
                                _logger.LogWarning("Station {dcpid}: response too short ({len} bytes), skipping",
                                    dcpid, bytes.Length);
                                continue;
                            }

                            bool newHeader = bytes[0x52] == 'B';
                            int byteLength = Convert.ToInt16(newHeader
                                    ? Encoding.ASCII.GetString(bytes[0x50..0x52])
                                    : Encoding.ASCII.GetString(bytes[0x50..0x54])
                                , 16);

                            Station? station;
                            station = context.Stations.FirstOrDefault(x => x.ExternalId == dcpid);
                            if (station == null)
                            {
                                station = new Station
                                {
                                    Name = Encoding.ASCII.GetString(bytes[0x09..0x19]),
                                    SourceAddress = appSettings.SourceAddress,
                                    ExternalId = dcpid,
                                    City = "بغداد"
                                };

                                context.Stations.Add(station);
                                bool success = await context.SaveChangesAsync(stoppingToken) > 0;
                                if (success)
                                    _logger.LogInformation("Added Station {dcpid} at {time}", dcpid,
                                        DateTimeOffset.Now);
                            }

                            var lastEntryDate = await context.SensorData
                                                    .OrderByDescending(x => x.TimeStamp)
                                                    .Where(x => x.StationId == station.Id)
                                                    .Select(x => x.TimeStamp)
                                                    .FirstOrDefaultAsync(stoppingToken)
                                                ?? DateTime.UtcNow.AddMonths(-2);

                            int entryNum = 0;
                            int skipped = 0;
                            int failed = 0;
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

                            while (offset < bytes.Length)
                            {
                                int linewidth;
                                try
                                {
                                    linewidth = FindSequence(bytes, eotSequence, offset) + 0x4 - offset;
                                }
                                catch
                                {
                                    break;
                                }

                                try
                                {
                                    int hgIndex = FindSequenceBitError(bytes, hgSequence, offset) - offset;

                                    var row = bytes[offset..(offset + linewidth)].AsSpan();

                                    if (row.Length < 0x48)
                                    {
                                        _logger.LogWarning(
                                            "Station {dcpid} entry {num}/{total}: too short ({len} bytes), skipping",
                                            dcpid, entryNum + 1, entryCount, row.Length);
                                        failed++;
                                        offset += linewidth;
                                        entryNum++;
                                        continue;
                                    }

                                    int dataStart = Math.Max(0, hgIndex - 0x5);
                                    var processedStr = ProcessSegment(row[dataStart..linewidth]);
                                    var values = processedStr
                                        .Split(' ', StringSplitOptions.RemoveEmptyEntries);

                                    Console.WriteLine(string.Join(" ", values));

                                    var timeStr = ProcessSegment(row[0x37..0x48]);
                                    if (!DateTime.TryParseExact(timeStr, "dd/MM/yy HH:mm:ss",
                                            CultureInfo.InvariantCulture, DateTimeStyles.None, out var times))
                                    {
                                        _logger.LogWarning(
                                            "Station {dcpid} entry {num}/{total}: bad timestamp '{ts}', skipping",
                                            dcpid, entryNum + 1, entryCount, timeStr.Trim());
                                        failed++;
                                        offset += linewidth;
                                        entryNum++;
                                        continue;
                                    }

                                    var timestamp = DateTime.SpecifyKind(times, DateTimeKind.Utc);
                                    if (timestamp <= lastEntryDate)
                                    {
                                        skipped++;
                                        offset += linewidth;
                                        entryNum++;
                                        continue;
                                    }

                                    string wl = dcpid == "1886A3C8"
                                        ? (values.Length > 7 ? values[7] : "0")
                                        : (values.Length > 3 ? values[3] : "0");

                                    double battery = ParseBatteryVoltage(values, dcpid == "1886A3C8");
                                    double? salt = ParseSalt(processedStr);
                                    double? tds = ParseTds(processedStr);

                                    data.Add(new SensorData
                                    {
                                        StationId = station.Id,
                                        WL = wl,
                                        BatteryVoltage = battery,
                                        Salt = salt,
                                        TDS = tds,
                                        TimeStamp = timestamp,
                                    });
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex,
                                        "Station {dcpid} entry {num}/{total}: parse error, skipping",
                                        dcpid, entryNum + 1, entryCount);
                                    failed++;
                                }

                                offset += linewidth;
                                entryNum++;
                            }

                            if (failed > 0)
                                _logger.LogWarning(
                                    "Station {dcpid}: {failed}/{total} entries failed to parse",
                                    dcpid, failed, entryCount);
                            if (skipped > 0)
                                _logger.LogInformation(
                                    "Station {dcpid}: {skipped}/{total} entries skipped (already saved)",
                                    dcpid, skipped, entryCount);

                            await context.SensorData.AddRangeAsync(data, stoppingToken);
                            _logger.LogInformation("Added {count} Sensor Data to station {dcpid} at {time}",
                                await context.SaveChangesAsync(stoppingToken), dcpid, DateTimeOffset.Now);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process station {dcpid}", dcpid);
                    }
                }

                _logger.LogInformation("Next run in: {count} minutes", appSettings.Delay / 1000 / 60);
                await Task.Delay(appSettings.Delay, stoppingToken);
            }
        }

        private static double ParseBatteryVoltage(string[] values, bool isMowr77)
        {
            if (values.Length < 4) return 0;

            if (values[3] == "M" && values.Length >= 5 &&
                double.TryParse(values[^4], out double bvM))
                return bvM;

            if (values.Length >= 4 && double.TryParse(values[^3], out double bv1))
                return bv1;

            if (values.Length > 6 && double.TryParse(values[6], out double bv2))
                return bv2;

            if (values.Length >= 3 && values[^2].Length >= 4 &&
                double.TryParse(values[^2][..4], out double bv3))
                return bv3;

            if (values.Length >= 5 && values[^4].Length >= 4 &&
                double.TryParse(values[^4][..4], out double bv4))
                return bv4;

            int fallbackIdx = isMowr77 ? 15 : 7;
            if (values.Length > fallbackIdx)
            {
                var raw = values[fallbackIdx];
                var candidate = raw.Length >= 4 ? raw[..4] : raw;
                if (double.TryParse(candidate, out double bvLast))
                    return bvLast;
            }

            return 0;
        }

        private static double? ParseSalt(string processedSegment)
        {
            var match = Regex.Match(processedSegment, @"SAL[^#]*#\s*\d{2}\s*(\d+\.\d{2})", RegexOptions.IgnoreCase);
            return match.Success &&
                   double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double salt)
                ? salt
                : null;
        }

        private static double? ParseTds(string processedSegment)
        {
            var match = Regex.Match(processedSegment, @"TDS[^#]*#\s*\d{2}\s*(\d+\.\d{2})", RegexOptions.IgnoreCase);
            return match.Success &&
                   double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double tds)
                ? tds
                : null;
        }

        private static int FindSequence(ReadOnlySpan<byte> span, ReadOnlySpan<byte> sequence, int offset = 0)
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

            throw new ArgumentException("Sequence not found in byte array");
        }

        private static int FindSequenceBitError(ReadOnlySpan<byte> span, ReadOnlySpan<byte> sequence,
            int offset = 0)
        {
            if (sequence.Length < span.Length)
            {
                for (int i = offset; i <= span.Length - sequence.Length; i++)
                {
                    int matches = 0;
                    for (int j = 0; j < sequence.Length; j++)
                    {
                        if (span[i + j] == sequence[j])
                        {
                            matches++;
                        }
                    }

                    if (matches >= 3)
                    {
                        return i;
                    }
                }
            }

            throw new ArgumentException("Sequence not found in byte array (fuzzy)");
        }

        private static string ProcessSegment(Span<byte> bytes)
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
