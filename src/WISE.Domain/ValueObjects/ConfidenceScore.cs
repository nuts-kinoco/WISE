using System;

namespace WISE.Domain.ValueObjects;

public record ConfidenceScore
{
    public int Value { get; init; }

    public ConfidenceScore(int value)
    {
        if (value < 0 || value > 100)
            throw new ArgumentOutOfRangeException(nameof(value), "Confidence score must be between 0 and 100.");
        Value = value;
    }

    public static ConfidenceScore Zero => new(0);
    public static ConfidenceScore Max => new(100);

    public ConfidenceScore Add(ConfidenceScore other)
    {
        int newValue = Math.Min(100, this.Value + other.Value);
        return new ConfidenceScore(newValue);
    }
}
