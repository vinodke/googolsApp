using Googols.Domain.Common;

namespace Googols.Domain.Site;

/// <summary>A message sent from the public Contact page. Read and answered by admins.</summary>
public class ContactMessage : Entity
{
    public required string Name { get; set; }
    public required string Email { get; set; }
    public string? Phone { get; set; }
    public required string Topic { get; set; }      // general | school | parent | support | partnership
    public required string Message { get; set; }
    public bool Handled { get; set; }
}
