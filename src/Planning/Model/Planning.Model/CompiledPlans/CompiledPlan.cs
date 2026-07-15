using Planning.Model.Plans;

namespace Planning.Model.CompiledPlans;

public record CompiledPlan(
	IEnumerable<CompiledPeriod> Periods,
	IEnumerable<CompiledMember> Members,
	IEnumerable<CompiledAsset> Assets,
	IDictionary<CompiledPeriod, IEnumerable<CompiledIncome>> Income,
	IDictionary<CompiledPeriod, decimal> RetirementIncome,
	IDictionary<CompiledPeriod, IEnumerable<CompiledContribution>> Contribution,
	TaxPolicy TaxPolicy,
	RetirementPhaseSchedule RetirementPhaseSchedule

);
