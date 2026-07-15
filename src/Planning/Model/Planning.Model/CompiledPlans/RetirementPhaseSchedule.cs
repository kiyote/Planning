namespace Planning.Model.CompiledPlans;

/// <summary>
/// The start dates of each retirement-income lifestyle phase, in chronological order.
/// Go-Go begins at the earliest retirement date, followed by Slow-Go and then No-Go.
/// </summary>
public record RetirementPhaseSchedule(
	DateOnly GoGoStart,
	DateOnly SlowGoStart,
	DateOnly NoGoStart
);
