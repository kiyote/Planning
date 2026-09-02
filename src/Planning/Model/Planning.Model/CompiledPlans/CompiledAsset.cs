using Planning.Model.Identifiers;
using Planning.Model.Plans;

namespace Planning.Model.CompiledPlans;

public record CompiledAsset(
	AssetId AssetId,
	string Name,
	AssetTaxStatus TaxStatus,
	MemberId MemberId,
	decimal Amount,
	decimal ContributionBacklog,
	decimal AnnualContributionLimit,
	bool HasUnlimitedContributionRoom,
	decimal CostBase,
	decimal? AnnualContributionIncreasePercent
) {

	/// <inheritdoc cref="Asset.ContributionBacklog"/>
	public decimal ContributionBacklog { get; init; } = HasUnlimitedContributionRoom ? 0m : ContributionBacklog;

	/// <inheritdoc cref="Asset.ContributionBacklog"/>
	public decimal AnnualContributionLimit { get; init; } = HasUnlimitedContributionRoom ? 0m : AnnualContributionLimit;
}
