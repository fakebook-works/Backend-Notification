using System.ComponentModel.DataAnnotations;

namespace NotificationService.Security;

public sealed class InternalAuthenticationOptions
{
    public const string SectionName = "InternalAuthentication";

    [Required]
    public string GatewaySecret { get; init; } = string.Empty;

    [Required]
    public string NotificationServiceSecret { get; init; } = string.Empty;
}
