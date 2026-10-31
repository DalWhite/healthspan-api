#nullable enable

namespace Controllers.ApiContracts.Results;

// Deterministic contract from docs/results-health-kpis-t_358e09e9.md §3.
// Rules: every KPI always carries an explicit `state`; nulls are explicit and
// never replaced by 0; numerics are double rounded to 2 decimals (decimal→double
// PR#25 lesson); `byType` ordered by typeId asc; month arrays ascending.

public sealed class MonthlyResultsResource
{
    public string TraineeId { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
    public string Month { get; set; } = string.Empty;
    public ConsistencyKpiResource Consistency { get; set; } = new();
    public WeightKpiResource Weight { get; set; } = new();
    public StrengthKpiResource Strength { get; set; } = new();
}

public sealed class ResultsSeriesResource
{
    public string TraineeId { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
    /// <summary>Ascending calendar months "YYYY-MM" (UTC).</summary>
    public List<string> Months { get; set; } = [];
    public List<ConsistencyKpiResource> Consistency { get; set; } = [];
    public List<WeightKpiResource> Weight { get; set; } = [];
    public StrengthSeriesResource Strength { get; set; } = new();
}

public sealed class ConsistencyKpiResource
{
    /// <summary>ok | not_started (null is NOT 0 — spec §2 K1).</summary>
    public string State { get; set; } = ResultsStates.Ok;
    /// <summary>0–1, 2 decimals. Null only when state = not_started.</summary>
    public double? Score { get; set; }
    /// <summary>score(M) − score(M−1) in percentage points. Null when either side is not_started.</summary>
    public double? DeltaPp { get; set; }
    public int ActiveSessions { get; set; }
}

public sealed class WeightKpiResource
{
    /// <summary>ok | current_only | insufficient_data.</summary>
    public string State { get; set; } = ResultsStates.InsufficientData;
    public double? LastKg { get; set; }
    /// <summary>Signed, neutral. 0 is a legitimate plateau; null means "no previous observation".</summary>
    public double? DeltaKg { get; set; }
}

public sealed class StrengthKpiResource
{
    /// <summary>ok | insufficient_data.</summary>
    public string State { get; set; } = ResultsStates.InsufficientData;
    /// <summary>Mean of the relative deltas of the qualifying basic types. Null when 0 types qualify.</summary>
    public double? IndexPct { get; set; }
    public List<StrengthTypeKpiResource> ByType { get; set; } = [];
}

public sealed class StrengthTypeKpiResource
{
    public int TypeId { get; set; }
    /// <summary>Lowercased exercise type name (stable label for the FE).</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Copied from ExerciseType.WeightType; never mixed across types.</summary>
    public string Unit { get; set; } = string.Empty;
    /// <summary>ok | insufficient_data.</summary>
    public string State { get; set; } = ResultsStates.InsufficientData;
    /// <summary>Signed delta vs the last previous observation. 0 = plateau ≠ null.</summary>
    public double? Delta { get; set; }
    public double? DeltaPct { get; set; }
}

public sealed class StrengthSeriesResource
{
    public List<StrengthIndexPoint> IndexPct { get; set; } = [];
    /// <summary>Ordered by typeId asc; each type's points align with the series months.</summary>
    public List<StrengthTypeSeriesResource> ByType { get; set; } = [];
}

public sealed class StrengthIndexPoint
{
    public string Month { get; set; } = string.Empty;
    public string State { get; set; } = ResultsStates.InsufficientData;
    public double? Value { get; set; }
}

public sealed class StrengthTypeSeriesResource
{
    public int TypeId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public List<StrengthTypePoint> Points { get; set; } = [];
}

public sealed class StrengthTypePoint
{
    public string Month { get; set; } = string.Empty;
    public string State { get; set; } = ResultsStates.InsufficientData;
    public double? Delta { get; set; }
    public double? DeltaPct { get; set; }
}

public static class ResultsStates
{
    public const string Ok = "ok";
    public const string NotStarted = "not_started";
    public const string CurrentOnly = "current_only";
    public const string InsufficientData = "insufficient_data";
}
