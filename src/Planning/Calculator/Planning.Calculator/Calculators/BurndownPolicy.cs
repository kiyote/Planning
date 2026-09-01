using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Identifiers;

namespace Planning.Calculator.Calculators;

/// <summary>
/// The gross amount withdrawn from a single taxable asset by the burndown strategy, together with
/// the member who owns it and is therefore taxed on it.
/// </summary>
internal sealed record BurndownWithdrawal(
	AssetId AssetId,
	MemberId MemberId,
	decimal Amount
);

/// <summary>
/// The gross amount withdrawn from each taxable asset by the burndown strategy for a period.
/// </summary>
internal sealed record BurndownWithdrawals(
	IReadOnlyList<BurndownWithdrawal> Withdrawals,
	decimal Total
) {
	public static readonly BurndownWithdrawals None = new BurndownWithdrawals( [], 0m );

	/// <summary>
	/// Projects the withdrawals into the general shape used by the tax accrual, which does not
	/// care which member owns the account.
	/// </summary>
	public IReadOnlyList<CalculatedWithdrawal> AsCalculatedWithdrawals()
		=> [.. Withdrawals.Select( w => new CalculatedWithdrawal( w.AssetId, w.Amount ) )];
}

/// <summary>
/// Draws taxable accounts down to zero over a fixed number of years and redirects the proceeds
/// into more tax-efficient accounts.
///
/// The burndown runs once per calendar year, in December, and is applied in excess of the
/// withdrawals already made to fund retirement income. Each account's window opens when its owner
/// retires and runs for the configured number of years, so members retiring at different times
/// burn down on their own schedules. Each taxable account is amortized over the number of years
/// remaining in its window using the plan-wide rate of return, so that the scheduled payments plus
/// growth bring the balance to zero exactly at the end of the window. The after-tax remainder is
/// contributed to the owning member's tax-exempt accounts while contribution room lasts, then to
/// their capital-gains accounts.
///
/// The eligibility rules and the amortization factors depend only on the plan, so they are
/// resolved once by the compiler into <see cref="CompiledBurndown"/>. All that remains here is
/// applying those factors to balances that are only known as the projection runs.
/// </summary>
internal sealed class BurndownPolicy {

	/// <summary>
	/// Calculates the gross burndown withdrawal from each taxable account for the given period by
	/// applying the compiled amortization factors to the current balances. Returns no withdrawals
	/// for periods that the compiled schedule does not cover.
	/// </summary>
	public BurndownWithdrawals CalculateWithdrawals(
		CompiledPlan compiledPlan,
		CompiledPeriod period,
		IReadOnlyList<CalculatedAsset> assets
	) {
		if( !compiledPlan.Burndown.Schedule.TryGetValue( period, out IEnumerable<CompiledBurndownWithdrawal>? scheduled ) ) {
			return BurndownWithdrawals.None;
		}

		List<BurndownWithdrawal> withdrawals = [];
		decimal total = 0m;

		foreach( CompiledBurndownWithdrawal entry in scheduled ) {
			CalculatedAsset? asset = assets.FirstOrDefault( a => a.AssetId == entry.AssetId );
			if( asset is null || asset.Amount <= 0m ) {
				continue;
			}

			// The schedule fixes the fraction of the balance to take; the balance itself is only
			// known now, and the payment can never exceed what the account actually holds.
			decimal payment = Math.Min( asset.Amount * entry.AmortizationFactor, asset.Amount );

			if( payment <= 0m ) {
				continue;
			}

			withdrawals.Add( new BurndownWithdrawal( entry.AssetId, entry.MemberId, payment ) );
			total += payment;
		}

		return new BurndownWithdrawals( withdrawals, total );
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
		IReadOnlyList<BurndownWithdrawal> withdrawals,
		decimal netProportion,
		DateOnly periodDate
	) {
		decimal transferred = 0m;

		foreach( BurndownWithdrawal withdrawal in withdrawals ) {
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

			MemberId memberId = withdrawal.MemberId;

			// The proceeds are net of the tax the burndown triggered, so they are after-tax
			// capital. Any part that finds no room stays uninvested.
			transferred += ShelterAllocator.Deposit(
				compiledPlan, endingAssets, periodDate, withdrawn * netProportion, memberId );
		}

		return transferred;
	}
}
