using Microsoft.Extensions.Options;
using NotificationService.Data;

namespace NotificationService.Tests.Data;

public sealed class SnowflakeOptionsValidatorTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(1024)]
    public void Validate_rejects_node_ids_outside_the_supported_range(int nodeId)
    {
        var result = new SnowflakeOptionsValidator().Validate(
            Options.DefaultName,
            new SnowflakeOptions { NodeId = nodeId });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("NodeId", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_rejects_an_epoch_with_a_non_utc_offset()
    {
        var result = new SnowflakeOptionsValidator().Validate(
            Options.DefaultName,
            new SnowflakeOptions
            {
                NodeId = 0,
                Epoch = new DateTimeOffset(2024, 1, 1, 7, 0, 0, TimeSpan.FromHours(7))
            });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Epoch", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_accepts_a_valid_configuration()
    {
        var result = new SnowflakeOptionsValidator().Validate(
            Options.DefaultName,
            new SnowflakeOptions
            {
                NodeId = SnowflakeIdGenerator.MaxNodeId,
                Epoch = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
            });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Generator_throws_when_constructed_with_invalid_options()
    {
        var invalidOptions = new SnowflakeOptions { NodeId = -1 };

        Assert.Throws<OptionsValidationException>(() => new SnowflakeIdGenerator(invalidOptions));
    }
}
