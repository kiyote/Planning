using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Identifiers;
using Planning.Model.Plans;

namespace Planning.Calculator.Calculators;

/// <summary>
/// Places money into the household's sheltered accounts, respecting contribution room.
///
/// Several policies need to park money that is not being spent: the burndown, the mandatory RRIF
/// minimum, and surplus income. They all follow the same rules, so the ordering lives here rather
/// than being repeated. Tax-exempt room is filled first because it shelters growth permanently,
/// then capital-gains accounts, which have no cap but expose future growth to tax.
///
/// Two constraints matter and are easy to get wrong. Contribution room is a hard legal cap, so an
/// account can never absorb more than its remaining backlog. And a deceased member's accounts can
/// no longer receive contributions, so only living members are eligible destinations.
/// </summary>
internal static class ShelterAllocator {

	// Money fills tax-exempt room first, then spills into capital-gains accounts.
	private static readonly AssetTaxStatus[] DestinationStatusOrder = [
		AssetTaxStatus.TaxExempt,
		AssetTaxStatus.CapitalGains
	];

	/// <summary>
	/// Deposits <paramref name="amount"/> into the household's sheltered accounts and returns the
	/// portion successfully placed. Any remainder had no destination with room and is left to the
	/// caller to handle.
	///
	/// Accounts belonging to <paramref name="preferredMemberId"/> are filled before a living
	/// spouse's, so money stays with its owner when there is room, but reaches across the
	/// household rather than falling back to a taxable account while a spouse's room sits unused.
	/// Pass <see langword="null"/> to express no preference.
	/// </summary>
	public static decimal Deposit(
		CompiledPlan compiledPlan,
		List<CalculatedAsset> endingAssets,
		DateOnly periodDate,
		decimal amount,
		MemberId? preferredMemberId
	) {
		if( amount <= 0m ) {
			return 0m;
		}

		HashSet<MemberId> livingMembers = [ .. compiledPlan.Members
			.Where( m => periodDate <= m.DeathDate )
			.Select( m => m.MemberId ) ];

		decimal remaining = amount;

		foreach( AssetTaxStatus status in DestinationStatusOrder ) {
			if( remaining <= 0m ) {
				break;
			}

			IEnumerable<CompiledAsset> candidates = compiledPlan.Assets
				.Where( a => a.TaxStatus == status && livingMembers.Contains( a.MemberId ) )
				.OrderByDescending( a => a.MemberId == preferredMemberId );

			foreach( CompiledAsset candidate in candidates ) {
				if( remaining <= 0m ) {
					break;
				}

				int index = endingAssets.FindIndex( a => a.AssetId == candidate.AssetId );
				if( index < 0 ) {
					continue;
				}

				CalculatedAsset destination = endingAssets[index];
				bool unlimited = destination.ContributionBacklog == CalculatedAsset.UnlimitedBacklog;
				decimal applied = unlimited
					? remaining
					: Math.Min( destination.ContributionBacklog, remaining );

				if( applied <= 0m ) {
					continue;
				}

				// The deposited money has already been taxed, so it establishes cost base in the
				// destination and is not taxed a second time as a capital gain.
				endingAssets[index] = destination with {
					Amount = destination.Amount + applied,
					CostBase = destination.CostBase + applied,
					ContributionBacklog = unlimited
						? destination.ContributionBacklog
						: destination.ContributionBacklog - applied
				};

				remaining -= applied;
			}
		}

		return amount - remaining;
	}
}
