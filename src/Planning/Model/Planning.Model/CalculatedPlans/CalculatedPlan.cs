namespace Planning.Model.CalculatedPlans;

using Planning.Model.Plans;

public record CalculatedPlan(
	IReadOnlyList<CalculatedPeriod> Periods,
	InsufficientFundsSummary InsufficientFunds,
	TaxSummary TaxSummary,
	EstateSummary EstateSummary,
	IReadOnlyList<PlanEvent> Events,
	RetirementIncome RetirementIncome
);
