using Microsoft.AspNetCore.Identity;

namespace TestShared;

public class ApplicationUser : IdentityUser<long>
{
    public List<string> AccessibleCities { get; set; } = new List<string>();
    public bool IsAdmin { get; set; }
}