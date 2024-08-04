using Microsoft.AspNetCore.Identity;

namespace TestWebApi.DTOs
{
    public class SigningDto
    {
        public required string Username { get; set; }
        public required string Password { get; set; }
    }
}
