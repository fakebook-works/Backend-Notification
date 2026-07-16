using Microsoft.Extensions.Options;

namespace NotificationService.Data;

/// <summary>
/// Thread-safe 64-bit Snowflake generator:
/// 41 timestamp bits, 10 node bits, and 12 sequence bits.
/// </summary>
public sealed class SnowflakeIdGenerator : ISnowflakeIdGenerator
{
    private const int NodeIdBits = 10;
    private const int SequenceBits = 12;
    private const int NodeIdShift = SequenceBits;
    private const int TimestampShift = NodeIdBits + SequenceBits;
    private const long MaxSequence = (1L << SequenceBits) - 1;
    private const long MaxTimestamp = (1L << 41) - 1;

    /// <summary>
    /// Maximum configured worker/node identifier.
    /// </summary>
    public const int MaxNodeId = (1 << NodeIdBits) - 1;

    private readonly object _gate = new();
    private readonly long _epochMilliseconds;
    private readonly int _nodeId;
    private readonly TimeProvider _timeProvider;

    private long _lastTimestamp = -1;
    private long _sequence;

    public SnowflakeIdGenerator(IOptions<SnowflakeOptions> options)
        : this(options?.Value ?? throw new ArgumentNullException(nameof(options)))
    {
    }

    public SnowflakeIdGenerator(SnowflakeOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var validationResult = new SnowflakeOptionsValidator().Validate(Options.DefaultName, options);
        if (validationResult.Failed)
        {
            throw new OptionsValidationException(Options.DefaultName, typeof(SnowflakeOptions), validationResult.Failures);
        }

        _epochMilliseconds = options.Epoch.ToUnixTimeMilliseconds();
        _nodeId = options.NodeId;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public long NextId()
    {
        lock (_gate)
        {
            var timestamp = Math.Max(_timeProvider.GetUtcNow().ToUnixTimeMilliseconds(), _epochMilliseconds);

            // Never reuse a timestamp after a wall-clock rollback. A sequence overflow advances
            // a logical millisecond instead of blocking indefinitely while the clock catches up.
            if (timestamp < _lastTimestamp)
            {
                timestamp = _lastTimestamp;
            }

            if (timestamp == _lastTimestamp)
            {
                if (_sequence == MaxSequence)
                {
                    timestamp = checked(_lastTimestamp + 1);
                    _sequence = 0;
                }
                else
                {
                    _sequence++;
                }
            }
            else
            {
                _sequence = 0;
            }

            var relativeTimestamp = timestamp - _epochMilliseconds;
            if (relativeTimestamp > MaxTimestamp)
            {
                throw new InvalidOperationException("The Snowflake timestamp has exceeded its 41-bit lifetime.");
            }

            _lastTimestamp = timestamp;

            return (relativeTimestamp << TimestampShift) |
                   ((long)_nodeId << NodeIdShift) |
                   _sequence;
        }
    }
}
