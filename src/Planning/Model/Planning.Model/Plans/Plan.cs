namespace Planning.Model.Plans;

/// <param name="CPPMaximum">
/// The published maximum monthly CPP retirement pension at age 65, including enhancements,
/// expressed in plan-start dollars.
/// </param>
/// <param name="CPPCombinedSurvivorMaximum">
/// The published maximum combined retirement/survivor pension at age 65 for the same period.
/// Its excess over CPPMaximum estimates the enhanced component; it is not a universal cap
/// on an individual's adjusted retirement pension plus survivor pension.
/// </param>
public record Plan(
	DateOnly StartDate,
	IEnumerable<Member> Members,
	decimal CPPMaximum,
	decimal CPPCombinedSurvivorMaximum,
	decimal OASMaximum,
	IEnumerable<Asset> Assets,
	decimal AnnualInflationPercent,
	decimal AnnualReturnPercent,
	IEnumerable<LifeInsurance> LifeInsurance,
	RetirementIncome RetirementIncome,
	IEnumerable<Contribution> Contributions,
	TaxPolicy TaxPolicy,
	Burndown Burndown,
	IEnumerable<Inheritance> Inheritance,
	IEnumerable<Expenditure> Expenditures
) {
	public static readonly Plan None = new Plan(
		DateOnly.MinValue,
		[],
		0.0m,
		0.0m,
		0.0m,
		[],
		0.0m,
		0.0m,
		[],
		RetirementIncome.None,
		[],
		TaxPolicy.None,
		Burndown.None,
		[],
		[]
	);
}
