namespace Planning.Model.Plans;

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
	IEnumerable<Inheritance> Inheritance
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
		[]
	);
}
