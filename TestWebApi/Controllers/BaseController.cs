using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TestWebApi.DTOs;
using TestWebApi.Extensions;

namespace TestWorkerService.Controller
{
    [ApiController]
    [Route("[controller]")]
    public class BaseController : ControllerBase;
    public class FakharController(SensorDataContext dataContext) : BaseController
    {
        [HttpGet("station")]
        public async Task<IActionResult> GetStations([FromQuery] StationRequestParameters parameters)
        {
            var query = dataContext.Stations.Where(x =>
                (string.IsNullOrEmpty(parameters.Name) || x.Name.Contains(parameters.Name)) &&
                (string.IsNullOrEmpty(parameters.ExternalId) || x.ExternalId == parameters.ExternalId) &&
                x.SourceAddress == "alfakhar.co");

            var data = await query
                .OrderbyStation(parameters.SortingKey, parameters.SortingDirection)
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.Description,
                    x.Lng,
                    x.Lat,
                    x.ExternalId,
                    x.SourceAddress,
                    x.CreatedAt,
                    LastData = x.SensorData.OrderByDescending(x => x.TimeStamp)
                        .Select(x => new
                        {
                            x.TimeStamp,
                            x.WL,
                            x.BatteryVoltage,
                            x.Record,
                        }).FirstOrDefault(),
                })
                .Skip(parameters.Skip)
                .Take(parameters.Take)
                .ToListAsync();

            var count = await query.CountAsync();

            return Ok(new { count, data });
        }

        [HttpGet("data")]
        public async Task<IActionResult> GetData([FromQuery] SensorDataRequestParameters parameters)
        {
            var query = dataContext.SensorData.Where(x =>
                (!parameters.DateMax.HasValue || x.TimeStamp <= parameters.DateMax) &&
                (!parameters.DateMin.HasValue || x.TimeStamp >= parameters.DateMin) &&
                (!parameters.StationId.HasValue || x.StationId == parameters.StationId) &&
                (x.Station == null || x.Station.SourceAddress == "alfakhar.co"));

            var data = await query
                .OrderbySensorData(parameters.SortingKey, parameters.SortingDirection)
                .Select(x => new
                {
                    x.Id,
                    x.TimeStamp,
                    x.WL,
                    x.BatteryVoltage,
                    x.Record,
                    Station = x.Station != null ? new { x.Station.Name, x.Station.Id } : null,
                })
                .Skip(parameters.Skip)
                .Take(parameters.Take)
                .ToListAsync();

            var count = await query.CountAsync();

            return Ok(new { count, data });
        }
    }
    public class DataPortalController(SensorDataContext dataContext) : BaseController
    {
        [HttpGet("station")]
        public async Task<IActionResult> GetStations([FromQuery] StationRequestParameters parameters)
        {
            var query = dataContext.Stations.Where(x =>
                (string.IsNullOrEmpty(parameters.Name) || x.Name.Contains(parameters.Name)) &&
                (string.IsNullOrEmpty(parameters.ExternalId) || x.ExternalId == parameters.ExternalId) &&
                (string.IsNullOrEmpty(parameters.SourceAddress) || x.SourceAddress.Contains(parameters.SourceAddress)) &&
                (!string.IsNullOrEmpty(parameters.SourceAddress) || x.SourceAddress != "alfakhar.co"));

            var data = await query
                .OrderbyStation(parameters.SortingKey, parameters.SortingDirection)
                .Select(x => new
                {
                    x.Id,
                    x.Name,
                    x.Description,
                    x.Lng,
                    x.Lat,
                    x.ExternalId,
                    x.SourceAddress,
                    x.CreatedAt,
                    LastData = x.SensorData.OrderByDescending(x => x.TimeStamp)
                        .Select(x => new
                        {
                            x.TimeStamp,
                            x.WL,
                            x.BatteryVoltage,
                            x.Record,
                        }).FirstOrDefault(),
                })
                .Skip(parameters.Skip)
                .Take(parameters.Take)
                .ToListAsync();

            var count = await query.CountAsync();

            return Ok(new { count, data });
        }

        [HttpGet("data")]
        public async Task<IActionResult> GetData([FromQuery] SensorDataRequestParameters parameters)
        {
            var query = dataContext.SensorData.Where(x =>
                (!parameters.DateMax.HasValue || x.TimeStamp <= parameters.DateMax) &&
                (!parameters.DateMin.HasValue || x.TimeStamp >= parameters.DateMin) &&
                (!parameters.StationId.HasValue || x.StationId == parameters.StationId) &&
                x.Station != null && x.Station.SourceAddress != "alfakhar.co");

            var data = await query
                .OrderbySensorData(parameters.SortingKey, parameters.SortingDirection)
                .Select(x => new
                {
                    x.Id,
                    x.TimeStamp,
                    x.WL,
                    x.BatteryVoltage,
                    x.Record,
                    Station = x.Station != null ? new { x.Station.Name, x.Station.Id } : null,
                })
                .Skip(parameters.Skip)
                .Take(parameters.Take)
                .ToListAsync();

            var count = await query.CountAsync();

            return Ok(new { count, data });
        }

        [HttpPut("{id:long}")]
        public async Task<IActionResult> UpdateStation(long id, [FromBody] StationUpdateDto stationUpdate)
        {
            var station = await dataContext.Stations.AsTracking().FirstAsync(x => x.Id == id);
            station.Lat = stationUpdate.Lat ?? station.Lat;
            station.Lng = stationUpdate.Lng ?? station.Lng;
            station.Name = string.IsNullOrEmpty(stationUpdate.Name) ? station.Name : stationUpdate.Name;

            if (await dataContext.SaveChangesAsync() > 0) return Ok("Success");
            return BadRequest("Failed for somereason");
        }
    }
}
