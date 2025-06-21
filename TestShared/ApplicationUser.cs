using Microsoft.AspNetCore.Identity;

namespace TestShared;

public class ApplicationUser : IdentityUser<long>
{
    public List<string> AccessibleCities { get; set; } = [];
    public List<long> AccessibleStationIds { get; set; } = [];
    public bool IsAdmin { get; set; }
}