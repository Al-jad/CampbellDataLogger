using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using TestWebApi.DTOs;
using TestWebApi.Extensions;

namespace TestWorkerService.Controller
{
    [ApiController]
    [Route("[controller]")]
    public class BaseController : ControllerBase;
    public class AuthController(UserManager<IdentityUser<long>> userManager, SignInManager<IdentityUser<long>> signInManager, IConfiguration config) : BaseController
    {
        private readonly ApiAppSettings appSettings = config.Get<ApiAppSettings>() ?? throw new InvalidOperationException("Missing AppSetting.SecretKey");

        //[HttpPost("register")]
        //public async Task<ActionResult> RegisterUser(SigningDto registerRequest)
        //{
        //    if (registerRequest == null)
        //    {
        //        return BadRequest("Invalid register request");
        //    }
        //    var user = new IdentityUser<long>(userName: registerRequest.Username);

        //    var result = await userManager.CreateAsync(user, registerRequest.Password);

        //    if (result.Succeeded)
        //    {
        //        return Ok();
        //    }

        //    return BadRequest("Invalid register request");
        //}

        [HttpPost("login")]
        public async Task<ActionResult<string>> Login(SigningDto loginRequest)
        {
            var user = await userManager.FindByNameAsync(loginRequest.Username);
            if (user == null)
            {
                return BadRequest("Invalid credentials");
            }

            var result = await signInManager.CheckPasswordSignInAsync(user, loginRequest.Password, false);
            if (result.Succeeded)
            {
                var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(appSettings.SecretKey));
                var signingCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);
                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity([new(JwtRegisteredClaimNames.NameId, user.Id.ToString())]),
                    Expires = DateTime.UtcNow.AddDays(7),
                    SigningCredentials = signingCredentials
                };

                var handler = new JwtSecurityTokenHandler();
                var token = handler.CreateToken(tokenDescriptor);
                var tokenString = handler.WriteToken(token);

                return Ok(new { accessToken = tokenString });
            }

            return BadRequest("Invalid credentials");
        }
    }
    [Authorize]
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
                    x.Images,
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
    [Authorize]
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
                    x.Images,
                    x.CreatedAt,
                    LastData = x.SensorData.OrderByDescending(x => x.TimeStamp)
                        .Select(d => new
                        {
                            d.TimeStamp,
                            WL = (x.SourceAddress == "waterresourcesmng.website" && d.WL.Length > 2) ? $"{d.WL.Insert(d.WL.Length - 2, ".")}" : d.WL,
                            d.BatteryVoltage,
                            d.Record,
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
                    WL = (x.Station != null && x.Station.SourceAddress == "waterresourcesmng.website" && x.WL.Length > 2) ? $"{x.WL.Insert(x.WL.Length - 2, ".")}" : x.WL,
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
            station.Images = stationUpdate.Images ?? station.Images;
            station.Name = string.IsNullOrEmpty(stationUpdate.Name) ? station.Name : stationUpdate.Name;
            station.Description = string.IsNullOrEmpty(stationUpdate.Description) ? station.Description : stationUpdate.Description;

            if (await dataContext.SaveChangesAsync() > 0) return Ok("Success");
            return BadRequest("Failed for somereason");
        }
    }
}
