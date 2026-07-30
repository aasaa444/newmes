namespace Mes.Domain.Execution;

public sealed class TestMeasurement
{
    public Guid Id { get; init; }

    public Guid TestRunId { get; init; }

    public TestRun? TestRun { get; init; }

    public required string ItemCode { get; init; }

    public required string ItemName { get; init; }

    public required string DataType { get; init; }

    public bool Required { get; init; }

    public required string RawValue { get; init; }

    public string? Unit { get; init; }

    public int? DecimalPlaces { get; init; }

    public decimal? LowerLimit { get; init; }

    public decimal? UpperLimit { get; init; }

    public string? ExpectedText { get; init; }

    public bool? ExpectedBoolean { get; init; }

    public decimal? NumericValue { get; init; }

    public bool? BooleanValue { get; init; }

    public TestMeasurementResult Result { get; init; }

    public string? DiagnosticCode { get; init; }

    public string? DiagnosticMessage { get; init; }

    public string? ReportedDiagnosticCode { get; init; }

    public string? ReportedDiagnosticMessage { get; init; }
}
