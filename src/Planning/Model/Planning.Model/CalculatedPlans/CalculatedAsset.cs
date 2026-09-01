using Planning.Model.CompiledPlans;
using Planning.Model.Identifiers;
using Planning.Model.Plans;

namespace Planning.Model.CalculatedPlans;

public record CalculatedAsset(
	AssetId AssetId,
	decimal Amount,
	decimal ContributionBacklog,
	AssetTaxStatus TaxStatus,
	bool HasUnlimitedContributionRoom,
	decimal CostBase = 0m
) {

	/// <inheritdoc cref="Asset.ContributionBacklog"/>
	public decimal ContributionBacklog { get; init; } = HasUnlimitedContributionRoom ? 0m : ContributionBacklog;

	/// <summary>
	/// The unrealized capital gain accrued on this asset: the amount by which its value exceeds
	/// the after-tax capital invested in it. Only this portion is subject to capital-gains tax
	/// when realized, whether by withdrawal or by deemed disposition at death.
	/// </summary>
	/// <remarks>
	/// The cost base is carried proportionally through every contribution and withdrawal, which
	/// leaves sub-cent residue on an account that has no real gain. Resolving at cent precision
	/// keeps that residue from being taxed as a gain.
	/// </remarks>
	public decimal AccruedGain => Math.Max( 0m, Math.Round( Amount - CostBase, 2, MidpointRounding.AwayFromZero ) );
}
