using Planning.Model;
using Planning.Model.Plans;
using Planning.Model.CompiledPlans;
using Planning.Model.CalculatedPlans;
using Planning.Model.Identifiers;

namespace Planning.Calculator.Calculators;

internal sealed class AssetGrowthCalculator {

	public CalculatedAsset GrowAsset(
		Plan plan,
		CompiledPlan compiledPlan,
		CalculatedAsset asset,
		DateOnly periodDate,
		IEnumerable<CalculatedWithdrawal> withdrawals,
		IEnumerable<CalculatedContribution> contributions
	) {
		decimal withdrawnAmount = withdrawals.Where( w => w.AssetId == asset.AssetId ).Sum( w => w.Amount );

		// A withdrawal consumes cost base in proportion to the fraction of the balance taken,
		// leaving the remaining balance and remaining cost base in the same ratio as before.
		decimal costBase = asset.CostBase;
		if( withdrawnAmount > 0m && asset.Amount > 0m ) {
			decimal fraction = Math.Min( 1m, withdrawnAmount / asset.Amount );
			costBase -= asset.CostBase * fraction;
		}

		decimal assetAmount = asset.Amount - withdrawnAmount;
		decimal returnAmount = assetAmount * ( plan.AnnualReturnPercent / 100 ) / 12;
		decimal newAmount = assetAmount + returnAmount;

		decimal contributedAmount = contributions.Where( c => c.AssetId == asset.AssetId ).Sum( c => c.Amount );

		// Growth is an accrued gain and does not affect cost base, but a contribution is made
		// with already-taxed money and so adds to it dollar for dollar.
		costBase += contributedAmount;

		assetAmount = newAmount + contributedAmount;
		return new CalculatedAsset( asset.AssetId, assetAmount, asset.ContributionBacklog, asset.TaxStatus, costBase );
	}
}
