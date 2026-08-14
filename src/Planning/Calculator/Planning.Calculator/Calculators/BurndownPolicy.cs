using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Identifiers;
using Planning.Model.Plans;

namespace Planning.Calculator.Calculators;

/// <summary>
/// The gross amount withdrawn from each taxable asset by the burndown strategy for a period.
/// </summary>
internal sealed record BurndownWithdrawals(
	IReadOnlyList<CalculatedWithdrawal> Withdrawals,
	decimal Total
);

/// <summary>
/// Draws taxable accounts down to zero over a fixed number of years and redirects the proceeds
/// into more tax-efficient accounts.
///
/// The burndown runs once per calendar year, in December, and is applied in excess of the
/// withdrawals already made to fund retirement income. Each account's window opens when its owner
/// retires and runs for the configured number of years, so members retiring at different times
/// burn down on their own schedules. Each taxable account is amortized over the number of years
/// remaining in its window using that account's own rate of return, so that the scheduled payments
/// plus growth bring the balance to zero exactly at the end of the window. The after-tax remainder
/// is contributed to the owning member's tax-exempt accounts while contribution room lasts, then to
/// their capital-gains accounts.
/// </summary>
internal sealed class BurndownPolicy {

	/// <summary>
	/// Calculates the gross burndown withdrawal from each taxable account for the given period.
	/// Returns no withdrawals when the plan has no burndown configured, outside December, or for
	/// accounts whose owner has not yet retired or has exhausted their burndown window.
	/// </summary>
	public BurndownWithdrawals CalculateWithdrawals(
		Plan plan,
		CompiledPlan compiledPlan,
		IReadOnlyList<CalculatedAsset> assets,
		DateOnly periodDate
	) {
		if( plan.Burndown is null || periodDate.Month != 12 ) {
			return new BurndownWithdrawals( [], 0m );
		}

		IReadOnlyDictionary<AssetId, CompiledAsset> assetsById = compiledPlan.Assets.ToDictionary( a => a.AssetId );
		List<CalculatedWithdrawal> withdrawals = [];
		decimal total = 0m;

		foreach( CalculatedAsset asset in assets ) {
			if( asset.TaxStatus != AssetTaxStatus.Taxable || asset.Amount <= 0m ) {
				continue;
			}

			CompiledMember owner = compiledPlan.Members
				.Single( m => m.MemberId == assetsById[asset.AssetId].MemberId );

			// The burndown window opens when the owner retires and runs for the configured
			// number of years thereafter.
			if( periodDate < owner.RetirementDate ) {
				continue;
			}

			int remainingYears = plan.Burndown.BurndownYears - ( periodDate.Year - owner.RetirementDate.Year );

			if( remainingYears <= 0 ) {
				continue;
			}

			decimal annualReturnPercent = AssetReturnResolver.ResolveAnnualReturnPercent(
				plan, compiledPlan, asset.AssetId, periodDate );

			decimal payment = Math.Min(
				AmortizedPayment( asset.Amount, annualReturnPercent / 100m, remainingYears ),
				asset.Amount );

			if( payment <= 0m ) {
				continue;
			}

			withdrawals.Add( new CalculatedWithdrawal( asset.AssetId, payment ) );
			total += payment;
		}

		return new BurndownWithdrawals( withdrawals, total );
	}

	/// <summary>
	/// The level annual payment that retires <paramref name="balance"/> over
	/// <paramref name="years"/> years while the balance continues to earn <paramref name="rate"/>.
	/// </summary>
	private static decimal AmortizedPayment(
		decimal balance,
		decimal rate,
		int years
	) {
		if( years <= 1 || rate <= 0m ) {
			return years <= 1 ? balance : balance / years;
		}

		double discountFactor = 1d - Math.Pow( 1d + (double)rate, -years );

		return balance * rate / (decimal)discountFactor;
	}

	/// <summary>
	/// Applies the burndown withdrawals to the ending assets and deposits the corresponding
	/// after-tax proceeds into destination accounts, consuming tax-exempt contribution room
	/// before overflowing into capital-gains accounts. The owning member's own room is used
	/// first, but a living spouse's unused tax-exempt room is used before falling back to a
	/// taxable account, since sheltering the proceeds anywhere in the household beats exposing
	/// them to tax. Returns the amount actually transferred.
	/// </summary>
	public decimal ApplyTransfers(
		CompiledPlan compiledPlan,
		List<CalculatedAsset> endingAssets,
		IReadOnlyList<CalculatedWithdrawal> withdrawals,
		decimal netProportion,
		DateOnly periodDate
	) {
		IReadOnlyDictionary<AssetId, CompiledAsset> assetsById = compiledPlan.Assets.ToDictionary( a => a.AssetId );

		decimal transferred = 0m;

		foreach( CalculatedWithdrawal withdrawal in withdrawals ) {
			int sourceIndex = endingAssets.FindIndex( a => a.AssetId == withdrawal.AssetId );
			CalculatedAsset source = endingAssets[sourceIndex];

			decimal withdrawn = Math.Min( source.Amount, withdrawal.Amount );
			if( withdrawn <= 0m ) {
				continue;
			}

			// Draining the source consumes its cost base in proportion to the fraction taken.
			decimal sourceCostBase = source.CostBase;
			if( source.Amount > 0m ) {
				sourceCostBase -= source.CostBase * Math.Min( 1m, withdrawn / source.Amount );
			}

			endingAssets[sourceIndex] = source with {
				Amount = source.Amount - withdrawn,
				CostBase = sourceCostBase
			};

			MemberId memberId = assetsById[withdrawal.AssetId].MemberId;

			// The proceeds are net of the tax the burndown triggered, so they are after-tax
			// capital. Any part that finds no room stays uninvested.
			transferred += ShelterAllocator.Deposit(
				compiledPlan, endingAssets, periodDate, withdrawn * netProportion, memberId );
		}

		return transferred;
	}
}
