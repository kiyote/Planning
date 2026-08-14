using Planning.Model.CompiledPlans;
using Planning.Model.CalculatedPlans;
using Planning.Model.Plans;

namespace Planning.Calculator.Calculators;

internal sealed class WithdrawalPolicy {

	public IEnumerable<CalculatedWithdrawal> CalculateWithdrawals(
		CompiledPeriod period,
		CompiledPlan plan,
		List<CalculatedAsset> assets,
		decimal shortfall
	) {
		List<CalculatedWithdrawal> result = [];

		foreach( CompiledMember member in plan.Members ) {
			decimal memberShare = shortfall / plan.Members.Count();

			// Withdraw in tax-preference order: FullTax first, then CapitalGains, then TaxExempt.
			// Own accounts are drained before the spouse's accounts within each tax tier.
			AssetTaxStatus[] statusOrder = [ AssetTaxStatus.Taxable, AssetTaxStatus.CapitalGains, AssetTaxStatus.TaxExempt ];

			foreach( AssetTaxStatus status in statusOrder ) {
				// Own accounts of this tax status.
				WithdrawFrom( result, assets, memberShare, out memberShare,
					plan.Assets.Where( a => a.TaxStatus == status && a.MemberId == member.MemberId ) );
			}

			foreach( AssetTaxStatus status in statusOrder ) {
				// Spouse's accounts of this tax status.
				WithdrawFrom( result, assets, memberShare, out memberShare,
					plan.Assets.Where( a => a.TaxStatus == status && a.MemberId != member.MemberId ) );
			}

			if( memberShare != 0 ) {
				//throw new InvalidOperationException( "You ran out of money. Game over." );
			}
		}

		// Now, re-pack the withdrawals so that all withdrawals from the same asset are combined into one withdrawal for that asset.
		// Every asset keeps an entry even when nothing was drawn from it, so that the per-asset
		// vector is stable across periods; reporting derives its columns from this shape.
		List<CalculatedWithdrawal> repackedResult = [.. result
			.GroupBy( w => w.AssetId )
			.Select( g => new CalculatedWithdrawal( g.Key, g.Sum( w => w.Amount ) ) )];

		return repackedResult;
	}

	private static void WithdrawFrom(
		List<CalculatedWithdrawal> result,
		List<CalculatedAsset> assets,
		decimal memberShare,
		out decimal remainingShare,
		IEnumerable<CompiledAsset> candidateAssets
	) {
		foreach( CompiledAsset asset in candidateAssets ) {
			CalculatedAsset currentAsset = assets.Single( a => a.AssetId == asset.AssetId );
			decimal currentAssetAmount = currentAsset.Amount - result.Where( r => r.AssetId == currentAsset.AssetId ).Sum( a => a.Amount );
			decimal amountToWithdraw = Math.Min( currentAssetAmount, memberShare );
			CalculatedWithdrawal withdrawal = new CalculatedWithdrawal( currentAsset.AssetId, amountToWithdraw );
			result.Add( withdrawal );
			memberShare -= amountToWithdraw;
		}
		remainingShare = memberShare;
	}
}
