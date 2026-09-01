using Planning.Model.Plans;

namespace Planning.Model.CompiledPlans;

public record CompiledPlan(
	IEnumerable<CompiledPeriod> Periods,
	IEnumerable<CompiledMember> Members,
	IEnumerable<CompiledAsset> Assets,

	/// <summary>
	/// Income arriving from outside the plan's own assets - government benefits, life insurance
	/// and inheritances - itemized per member for every period. These inflows happen regardless
	/// of any decision the projection makes.
	/// </summary>
	IDictionary<CompiledPeriod, IEnumerable<CompiledIncome>> ScheduledIncome,

	/// <summary>
	/// The after-tax income the plan is trying to deliver in each period, inflated forward from
	/// the go-go/slow-go/no-go amounts. Any part of this not met by <see cref="ScheduledIncome"/>
	/// has to be funded by drawing on assets.
	/// </summary>
	IDictionary<CompiledPeriod, decimal> DesiredIncome,

	IDictionary<CompiledPeriod, IEnumerable<CompiledContribution>> Contribution,
	TaxPolicy TaxPolicy,
	RetirementPhaseSchedule RetirementPhaseSchedule,
	CompiledBurndown Burndown
);
