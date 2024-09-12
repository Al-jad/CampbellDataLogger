namespace TestWebApi.DTOs
{
    public class StationUpdateDto
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Notes { get; set; }
        public List<string>? Images {  get; set; }
        public double? Lat { get; set; }
        public double? Lng { get; set; }
        public string? City { get; set; }
    }
}
