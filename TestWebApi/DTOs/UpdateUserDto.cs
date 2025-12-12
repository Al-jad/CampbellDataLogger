using System;
using TestShared;

namespace TestWebApi.DTOs;

public class UpdateUserDto
{
        public string? UserName { get; set; }
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string? PhoneNumber { get; set; }
        public bool? IsAdmin { get; set; }

        public void ProjectTo(ApplicationUser user)
        {
                if (UserName != null) user.UserName = UserName;
                if (Email != null) user.Email = Email;
                if (PhoneNumber != null) user.PhoneNumber = PhoneNumber;
                if (IsAdmin != null) user.IsAdmin = IsAdmin.Value;
        }
}
