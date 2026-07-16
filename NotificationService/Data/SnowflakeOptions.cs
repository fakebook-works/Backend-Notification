using Microsoft.Extensions.Options;

namespace NotificationService.Data;

/// <summary>
/// Configuration for the 64-bit, application-generated notification identifier.
/// </summary>
public sealed class SnowflakeOptions
{
    public const string SectionName = "Snowflake";

    /// <summary>
    /// Unique worker/node identifier in the inclusive range 0..1023.
    /// This is intentionally unset by default so deployment configuration is required.
    /// </summary>
    public int NodeId { get; set; } = -1;

    /// <summary>
    /// UTC custom epoch used by the 41-bit timestamp component.
    /// </summary>
    public DateTimeOffset Epoch { get; set; } = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
}

public sealed class SnowflakeOptionsValidator : IValidateOptions<SnowflakeOptions>
{
    public ValidateOptionsResult Validate(string? name, SnowflakeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.NodeId is < 0 or > SnowflakeIdGenerator.MaxNodeId)
        {
            failures.Add($"{SnowflakeOptions.SectionName}:NodeId must be between 0 and {SnowflakeIdGenerator.MaxNodeId}.");
        }

        if (options.Epoch.Offset != TimeSpan.Zero)
        {
            failures.Add($"{SnowflakeOptions.SectionName}:Epoch must be expressed in UTC (offset +00:00).");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
