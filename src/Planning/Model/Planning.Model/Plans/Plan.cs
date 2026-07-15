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
	TaxPolicy TaxPolicy
);
