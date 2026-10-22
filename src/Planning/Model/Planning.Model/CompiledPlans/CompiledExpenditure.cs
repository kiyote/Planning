namespace Planning.Model.CompiledPlans;

public record CompiledExpenditure(
	CompiledPeriod Period,
	string Name,
	decimal Amount
);
