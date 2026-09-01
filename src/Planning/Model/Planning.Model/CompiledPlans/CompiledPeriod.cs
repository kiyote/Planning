using Planning.Model.Identifiers;

namespace Planning.Model.CompiledPlans;

public record CompiledPeriod(
	PeriodNumber PeriodNumber,
	DateOnly PeriodDate
);
