using Planning.Model.Identifiers;
using Planning.Model.Plans;

namespace Planning.Model.CalculatedPlans;

public record CalculatedAsset(
	AssetId AssetId,
	decimal Amount,
	decimal ContributionBacklog,
	AssetTaxStatus TaxStatus,
	decimal CostBase = 0m
) {

	/// <summary>
	/// The unrealized capital gain accrued on this asset: the amount by which its value exceeds
	/// the after-tax capital invested in it. Only this portion is subject to capital-gains tax
	/// when realized, whether by withdrawal or by deemed disposition at death.
	/// </summary>
	public decimal AccruedGain => Math.Max( 0m, Amount - CostBase );
}
