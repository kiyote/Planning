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
		decimal assetAmount = asset.Amount;
		assetAmount -= withdrawals.Where( w => w.AssetId == asset.AssetId ).Sum( w => w.Amount );
		decimal annualReturnPercent = ResolveAnnualReturnPercent( plan, compiledPlan, asset.AssetId, periodDate );
		decimal returnAmount = assetAmount * ( annualReturnPercent / 100 ) / 12;
		decimal newAmount = assetAmount + returnAmount;

		decimal contributedAmount = contributions.Where( c => c.AssetId == asset.AssetId ).Sum( c => c.Amount );

		assetAmount = newAmount + contributedAmount;
		return new CalculatedAsset( asset.AssetId, assetAmount );
	}

	private static decimal ResolveAnnualReturnPercent(
		Plan plan,
		CompiledPlan compiledPlan,
		AssetId assetId,
		DateOnly periodDate
	) {
		CompiledAsset compiledAsset = compiledPlan.Assets.Single( a => a.AssetId == assetId );

		// Asset-level returns override the plan-level return when present; otherwise fall back to the plan-level return.
		RangedValue? assetReturn = compiledAsset.ReturnPercentages?
			.Where( r => r.StartDate <= periodDate )
			.OrderByDescending( r => r.StartDate )
			.FirstOrDefault();

		if( assetReturn is not null ) {
			return assetReturn.Value;
		}

		return plan.AnnualReturnPercent;
	}
}
