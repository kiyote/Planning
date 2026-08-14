using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Identifiers;
using Planning.Model.Plans;

namespace Planning.Calculator.Calculators;

/// <summary>
/// The additional amount each taxable account must give up to satisfy its RRIF minimum.
/// </summary>
internal sealed record RrifMinimumWithdrawals(
	IReadOnlyList<CalculatedWithdrawal> Withdrawals,
	decimal Total
);

/// <summary>
/// Enforces the mandatory minimum RRIF withdrawal.
///
/// Once an RRSP is converted to a RRIF the holder must withdraw a prescribed percentage of the
/// account's January 1 balance each year, whether or not the income is wanted. The requirement is
/// assessed once per calendar year, in December, against the withdrawals already taken from the
/// account during that year: only the shortfall is forced out, so a plan that already drew more
/// than its minimum is unaffected.
///
/// Because the forced amount is by definition in excess of the income the plan asked for, it is
/// not spent. It is redirected into the owning member's tax-exempt accounts while contribution
/// room lasts, and then into their capital-gains accounts, which have no cap. The money therefore
/// stays invested; only its tax shelter is lost.
/// </summary>
internal sealed class RrifMinimumPolicy {

	/// <summary>
	/// The age at which conversion to a RRIF becomes mandatory, so that the minimum applies even
	/// to a member who has not retired.
	/// </summary>
	private const int MandatoryConversionAge = 71;

	/// <summary>
	/// Calculates the top-up each taxable account still owes to meet its minimum for the year.
	/// </summary>
	/// <param name="yearStartBalances">Each taxable account's balance as at January 1.</param>
	/// <param name="withdrawnThisYear">Amount already withdrawn from each account during the year.</param>
	public RrifMinimumWithdrawals CalculateWithdrawals(
		CompiledPlan compiledPlan,
		IReadOnlyList<CalculatedAsset> currentAssets,
		DateOnly periodDate,
		IReadOnlyDictionary<AssetId, decimal> yearStartBalances,
		IReadOnlyDictionary<AssetId, decimal> withdrawnThisYear
	) {
		List<RrifMinimum> minimums = [ .. compiledPlan.TaxPolicy.RrifMinimums ?? [] ];
		if( minimums.Count == 0 ) {
			return new RrifMinimumWithdrawals( [], 0m );
		}

		IReadOnlyDictionary<MemberId, CompiledMember> membersById =
			compiledPlan.Members.ToDictionary( m => m.MemberId );

		List<CalculatedWithdrawal> withdrawals = [];
		decimal total = 0m;

		foreach( CompiledAsset asset in compiledPlan.Assets.Where( a => a.TaxStatus == AssetTaxStatus.Taxable ) ) {
			if( !membersById.TryGetValue( asset.MemberId, out CompiledMember? member ) ) {
				continue;
			}

			// The balance is measured at January 1, so the age that sets the factor is the age
			// the holder had reached at the start of the year.
			DateOnly yearStart = new DateOnly( periodDate.Year, 1, 1 );
			if( yearStart > member.DeathDate ) {
				continue;
			}

			int age = AgeAt( member.BirthDate, yearStart );

			// The RRSP is modelled as converting to a RRIF at retirement, and conversion is
			// mandatory by age 71 regardless.
			bool converted = yearStart >= member.RetirementDate || age >= MandatoryConversionAge;
			if( !converted ) {
				continue;
			}

			decimal? factor = FactorFor( minimums, age );
			if( factor is null || factor.Value <= 0m ) {
				continue;
			}

			if( !yearStartBalances.TryGetValue( asset.AssetId, out decimal openingBalance ) || openingBalance <= 0m ) {
				continue;
			}

			decimal required = openingBalance * ( factor.Value / 100m );
			withdrawnThisYear.TryGetValue( asset.AssetId, out decimal alreadyWithdrawn );

			decimal shortfall = required - alreadyWithdrawn;
			if( shortfall <= 0m ) {
				continue;
			}

			// The account cannot give up more than it currently holds.
			int index = currentAssets.ToList().FindIndex( a => a.AssetId == asset.AssetId );
			if( index < 0 ) {
				continue;
			}

			decimal available = currentAssets[index].Amount;
			decimal amount = Math.Min( shortfall, available );
			if( amount <= 0m ) {
				continue;
			}

			withdrawals.Add( new CalculatedWithdrawal( asset.AssetId, amount ) );
			total += amount;
		}

		return new RrifMinimumWithdrawals( withdrawals, total );
	}

	/// <summary>
	/// Removes the forced amounts from their accounts and redirects them into tax-exempt
	/// accounts, then capital-gains accounts. The owning member's own room is used first, but a
	/// living spouse's unused TFSA room is used before falling back to a taxable account, since
	/// sheltering the money anywhere in the household beats exposing it to tax.
	/// </summary>
	/// <returns>The total amount successfully redirected into another account.</returns>
	public decimal ApplyWithdrawals(
		CompiledPlan compiledPlan,
		List<CalculatedAsset> endingAssets,
		IReadOnlyList<CalculatedWithdrawal> withdrawals,
		DateOnly periodDate
	) {
		IReadOnlyDictionary<AssetId, CompiledAsset> assetsById = compiledPlan.Assets.ToDictionary( a => a.AssetId );

		decimal transferred = 0m;

		foreach( CalculatedWithdrawal withdrawal in withdrawals ) {
			int sourceIndex = endingAssets.FindIndex( a => a.AssetId == withdrawal.AssetId );
			if( sourceIndex < 0 ) {
				continue;
			}

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

			// The tax on this withdrawal is settled separately at year end, so the amount moved
			// is measured gross. Any part that finds no room stays uninvested.
			transferred += ShelterAllocator.Deposit(
				compiledPlan, endingAssets, periodDate, withdrawn, memberId );
		}

		return transferred;
	}

	/// <summary>
	/// Selects the factor for an age, using the highest listed age at or below it so that a
	/// schedule ending at 95+ continues to apply beyond its last entry.
	/// </summary>
	private static decimal? FactorFor(
		List<RrifMinimum> minimums,
		int age
	) {
		RrifMinimum? match = minimums
			.Where( m => m.Age <= age )
			.OrderByDescending( m => m.Age )
			.FirstOrDefault();

		return match?.Percent;
	}

	private static int AgeAt(
		DateOnly birthDate,
		DateOnly date
	) {
		int age = date.Year - birthDate.Year;
		if( date < birthDate.AddYears( age ) ) {
			age--;
		}

		return age;
	}
}
