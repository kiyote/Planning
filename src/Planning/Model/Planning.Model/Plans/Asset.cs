namespace Planning.Model.Plans;

public record Asset(
	string Name,
	AssetTaxStatus TaxStatus,
	string Member,
	decimal Amount,
	IEnumerable<RangedValue> ReturnPercentages,
	DateOnly StartDate, // The date on which this Asset will exist (ie - inheritance)
	decimal ContributionBacklog, // The amount that can be transferred from this Asset to the next one (ie - RRSP to TFSA)
	decimal AnnualContributionLimit // The amount the backlog will grow annually (ie - RRSP contribution room increases each year based on income, TFSA contribution room increases each year by a fixed amount
);
