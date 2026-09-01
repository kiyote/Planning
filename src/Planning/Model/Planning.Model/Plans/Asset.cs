namespace Planning.Model.Plans;

public record Asset(
	string Name,
	AssetTaxStatus TaxStatus,
	string Member,
	decimal Amount,
	decimal ContributionBacklog,
	decimal AnnualContributionLimit // The amount the backlog will grow annually (ie - RRSP contribution room increases each year based on income, TFSA contribution room increases each year by a fixed amount
) {
	public static readonly Asset None = new Asset( "", AssetTaxStatus.Unknown, "", 0.0m, 0.0m, 0.0m );

	public const decimal UnlimitedBacklog = -1m;
}
