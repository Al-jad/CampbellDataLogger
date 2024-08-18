using System.Text.Json.Serialization;
using TestWebApi;

namespace TestWorkerService
{
    public class RequestParameters
    {
        public int Skip { get; init; }
        public int Take { get; init; } = 25;

        protected static readonly char[] separator = [' ', '+'];
        public string? Sort
        {
            get => $"{SortingKey} {SortingDirection}";
            init
            {
                var parts = value?.Split(separator) ?? ["CreatedAt", "desc"];
                (SortingKey, SortingDirection) = (parts[0], parts.ElementAtOrDefault(1) ?? "desc");
            }
        }
        [JsonIgnore] public string SortingKey = "CreatedAt";
        [JsonIgnore] public string SortingDirection = "desc";
    }
    public class SensorDataRequestParameters : RequestParameters
    {
        public long? StationId { get; init; }
        private readonly DateTime? _dateMin;
        public DateTime? DateMin { get => _dateMin; init => _dateMin = value.HasValue ? value.Value.ToUniversalTime() : null; }
        private readonly DateTime? _dateMax;
        public DateTime? DateMax { get => _dateMax; init => _dateMax = value.HasValue ? value.Value.ToUniversalTime() : null; }
        public Period? Period { get; init; }
    }

    public class StationRequestParameters : RequestParameters
    {
        public string? SourceAddress { get; init; }
        public string? Name { get; init; }
        public string? ExternalId { get; init; }

    }
}
