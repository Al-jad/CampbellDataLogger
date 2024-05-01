using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace TestWorkerService.Controller
{
    [ApiController]
    [Route("/api/")]
    public class BaseController(SensorDataContext dataContext) : ControllerBase
    {
        private readonly AppSettings appSetting = JsonConvert.DeserializeObject<AppSettings>(System.IO.File.ReadAllText("appsettings.json"));

        [HttpGet]
        public IActionResult GetStations()
        {
            return Ok(appSetting.Stations);
        }


        [HttpGet("station")]
        public async Task<IActionResult> GetStations([FromQuery] SeonsorDataRequestParameters query)
        {
           var stations = await dataContext.SensorData.Where(x =>
                (!query.DateMax.HasValue || x.TimeStamp <= query.DateMax) &&
                (!query.DateMin.HasValue || x.TimeStamp >= query.DateMin) &&
                string.IsNullOrEmpty(query.Station) || x.Station == query.Station)
                .ToListAsync();
            return Ok(stations);
        }
    }
}
