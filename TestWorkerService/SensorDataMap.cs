using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace TestWorkerService;

public class SensorDataMap : ClassMap<SensorData>
{
    public SensorDataMap()
    {
        Map(m => m.TimeStamp).Convert(row => DateTime.Parse(row.Row.GetField(0)).AddHours(-3).ToUniversalTime());
        Map(m => m.Record).Name("RECNBR");
        Map(m => m.WL).Name("WL");
        Map(m => m.BatteryVoltage).Name("BattV_Min");
    }
    
    public static List<SensorData> ParseCsvFile(string filePath)
    {
        using var reader = new StreamReader(filePath);

        const int linesToSkip = 1;
        for (var i = 0; i < linesToSkip; i++)
        {
            reader.ReadLine();
        }
        
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        
        csv.Context.RegisterClassMap<SensorDataMap>();
        return csv.GetRecords<SensorData>().ToList();
    }
}