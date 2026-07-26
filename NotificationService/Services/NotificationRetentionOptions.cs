namespace NotificationService.Services;

/// <summary>
/// Bounds how long delivered notifications are kept.
/// </summary>
/// <remarks>
/// The table gains a row for every like, comment, follow and mention in the entire system
/// and nothing ever removed one, so it and its four indexes grew without limit on a
/// database shared by every service. Deleting in bounded batches keeps each statement
/// short rather than taking one long lock over a large range.
/// </remarks>
public sealed class NotificationRetentionOptions
{
    public const string SectionName = "NotificationRetention";

    public bool Enabled { get; set; } = true;

    /// <summary>How long a delivered notification is kept before it becomes eligible for deletion.</summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>Rows removed per statement. Several batches run per sweep until nothing is left.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>Upper bound on batches per sweep, so one pass cannot run unbounded.</summary>
    public int MaxBatchesPerSweep { get; set; } = 40;

    public int SweepIntervalMinutes { get; set; } = 60;
}
