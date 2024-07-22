using System.Text.Json.Serialization;

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
        public DateTime? DateMin { get; init; }
        public DateTime? DateMax { get; init; }
    }

    public class StationRequestParameters : RequestParameters
    {
        public string? SourceAddress { get; init; }
        public string? Name { get; init; }
        public string? ExternalId { get; init; }

    }
}
