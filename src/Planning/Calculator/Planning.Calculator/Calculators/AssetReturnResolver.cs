using Planning.Model;
using Planning.Model.CompiledPlans;
using Planning.Model.Identifiers;
using Planning.Model.Plans;

namespace Planning.Calculator.Calculators;

internal static class AssetReturnResolver {

	/// <summary>
	/// Resolves the annual return percentage in effect for an asset on a given date. Asset-level
	/// returns override the plan-level return when present; otherwise the plan-level return applies.
	/// </summary>
	public static decimal ResolveAnnualReturnPercent(
		Plan plan,
		CompiledPlan compiledPlan,
		AssetId assetId,
		DateOnly periodDate
	) {
		CompiledAsset compiledAsset = compiledPlan.Assets.Single( a => a.AssetId == assetId );

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
