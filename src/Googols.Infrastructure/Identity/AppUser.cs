using Microsoft.AspNetCore.Identity;

namespace Googols.Infrastructure.Identity;

public class AppUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = "";
    public AccountStatus Status { get; set; } = AccountStatus.Active;
    public DateTime CreatedAtUtc { get; set; }
    public string PreferredLanguage { get; set; } = "en";
}

public enum AccountStatus
{
    Active = 0,
    PendingConsent = 1,
    Suspended = 2,
    Deleted = 3,
}
