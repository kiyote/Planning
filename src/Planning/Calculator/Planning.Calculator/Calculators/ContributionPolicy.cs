using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Identifiers;
using Planning.Model.Plans;

namespace Planning.Calculator.Calculators;

/// <summary>
/// A spousal contribution that was actually applied, recorded so the attribution window can
/// later tax withdrawals back to the member who funded it.
/// </summary>
internal sealed record SpousalDeposit(
	MemberId ContributorMemberId,
	MemberId DestinationMemberId,
	AssetId AssetId,
	decimal Amount
);

/// <summary>
/// The result of allocating a period's contributions against the available contribution
/// room, carrying forward the room that remains.
/// </summary>
internal sealed record ContributionAllocation(
	IReadOnlyList<CalculatedContribution> Contributions,
	IReadOnlyList<CalculatedAsset> Assets,
	IReadOnlyList<SpousalDeposit> SpousalDeposits
);

/// <summary>
/// Applies the annual contribution room accrual and allocates each period's contributions
/// against that room. Each contribution fills the member's accounts in the configured
/// contribution priority order, overflowing to the next account as room is exhausted.
/// </summary>
internal sealed class ContributionPolicy {

	// Contribution preference: fill Taxable room first, then TaxExempt, then CapitalGains.
	// This is deliberately not the strict inverse of the withdrawal ordering.
	private static readonly AssetTaxStatus[] _contributionStatusOrder = [
		AssetTaxStatus.Taxable,
		AssetTaxStatus.TaxExempt,
		AssetTaxStatus.CapitalGains
	];

	public ContributionAllocation AllocateContributions(
		CompiledPlan compiledPlan,
		IReadOnlyList<CalculatedAsset> assets,
		DateOnly periodDate,
		bool isFirstPeriod,
		IEnumerable<CompiledContribution> contributions
	) {
		Dictionary<AssetId, decimal> backlogByAsset = assets
			.ToDictionary( a => a.AssetId, a => a.ContributionBacklog );

		// The backlog dictionary carries plain amounts, so the uncapped accounts are tracked
		// alongside it rather than being encoded into the amount itself.
		HashSet<AssetId> unlimitedRoomAssets = [ .. assets
			.Where( a => a.HasUnlimitedContributionRoom )
			.Select( a => a.AssetId ) ];

		// Contribution room accrues once per calendar year, in January. The first period is
		// skipped because the plan's seed backlog is stated as of the plan start date.
		if( periodDate.Month == 1 && !isFirstPeriod ) {
			foreach( CompiledAsset compiledAsset in compiledPlan.Assets ) {
				if( compiledAsset.HasUnlimitedContributionRoom ) {
					continue;
				}

				// Taxable room stops accruing in every year after the owner retires, since the
				// room is earned from employment income.
				if( compiledAsset.TaxStatus == AssetTaxStatus.Taxable
					&& IsAfterRetirementYear( compiledPlan, compiledAsset, periodDate )
				) {
					continue;
				}

				if( backlogByAsset.TryGetValue( compiledAsset.AssetId, out decimal backlog )
					&& !unlimitedRoomAssets.Contains( compiledAsset.AssetId )
				) {
					backlogByAsset[compiledAsset.AssetId] = backlog + compiledAsset.AnnualContributionLimit;
				}
			}
		}

		Dictionary<AssetId, decimal> appliedByAsset = [];
		List<SpousalDeposit> spousalDeposits = [];

		foreach( CompiledContribution contribution in contributions ) {
			decimal remaining = contribution.Amount;

			// The contribution fills the destination member's most tax-efficient account that
			// still has room, overflowing into progressively less efficient accounts as room
			// runs out. For a spousal contribution the funds land in the annuitant's account
			// but consume the contributor's room, so the room account is looked up separately.
			foreach( AssetTaxStatus status in _contributionStatusOrder ) {
				IEnumerable<CompiledAsset> candidates = compiledPlan.Assets
					.Where( a => a.MemberId == contribution.DestinationMemberId
						&& a.TaxStatus == status );

				foreach( CompiledAsset candidate in candidates ) {
					if( contribution.IsSpousal ) {
						// Registered room belongs to the contributor, so their matching account
						// is drawn down first.
						CompiledAsset? roomAsset = compiledPlan.Assets
							.FirstOrDefault( a => a.MemberId == contribution.MemberId
								&& a.TaxStatus == status );

						if( roomAsset is not null ) {
							decimal spousalApplied = Apply(
								backlogByAsset, unlimitedRoomAssets, appliedByAsset, roomAsset.AssetId, candidate.AssetId, remaining );
							remaining -= spousalApplied;

							if( spousalApplied > 0m ) {
								spousalDeposits.Add( new SpousalDeposit(
									contribution.MemberId,
									contribution.DestinationMemberId,
									candidate.AssetId,
									spousalApplied ) );
							}
						}

						// Once the contributor's room is exhausted the balance is treated as the
						// annuitant contributing to their own plan, drawing on their own room.
						// That portion is not a spousal contribution and carries no attribution.
					}

					decimal applied = Apply(
						backlogByAsset, unlimitedRoomAssets, appliedByAsset, candidate.AssetId, candidate.AssetId, remaining );
					remaining -= applied;

					if( remaining <= 0 ) {
						break;
					}
				}

				if( remaining <= 0 ) {
					break;
				}
			}

			// Any amount still remaining has no contribution room anywhere and is not contributed.
			if( remaining > 0m ) {
				throw new InvalidOperationException( "Contribution left over with nowhere to be placed." );
			}
		}

		// Report a contribution entry for every asset so the column set stays stable across
		// periods, even though overflow can target different accounts as room is exhausted.
		IReadOnlyList<CalculatedContribution> calculatedContributions = [
			.. compiledPlan.Assets.Select(
				a => new CalculatedContribution( a.AssetId, appliedByAsset.GetValueOrDefault( a.AssetId ) ) )
		];

		IReadOnlyList<CalculatedAsset> updatedAssets = [
			.. assets.Select( a => a with { ContributionBacklog = backlogByAsset[a.AssetId] } )
		];

		return new ContributionAllocation( calculatedContributions, updatedAssets, spousalDeposits );
	}

	/// <summary>
	/// Whether the accruing calendar year starts after the year in which the asset's owner
	/// retired. Room still accrues for the retirement year itself and stops in every year after.
	/// </summary>
	private static bool IsAfterRetirementYear(
		CompiledPlan compiledPlan,
		CompiledAsset asset,
		DateOnly periodDate
	) {
		CompiledMember member = compiledPlan.Members.Single( m => m.MemberId == asset.MemberId );

		return periodDate.Year > member.RetirementDate.Year;
	}

	/// <summary>
	/// Contributes as much of <paramref name="amount"/> as the room account's remaining room
	/// allows, reducing that room and crediting the deposit account with the amount actually
	/// applied. The two are the same account except for a spousal contribution, where the
	/// contributor supplies the room and the annuitant receives the funds. An account with
	/// unlimited contribution room absorbs the full amount and its backlog is left untouched.
	/// </summary>
	private static decimal Apply(
		Dictionary<AssetId, decimal> backlogByAsset,
		IReadOnlySet<AssetId> unlimitedRoomAssets,
		Dictionary<AssetId, decimal> appliedByAsset,
		AssetId roomAssetId,
		AssetId depositAssetId,
		decimal amount
	) {
		if( amount <= 0 || !backlogByAsset.TryGetValue( roomAssetId, out decimal available ) ) {
			return 0;
		}

		if( unlimitedRoomAssets.Contains( roomAssetId ) ) {
			appliedByAsset[depositAssetId] = appliedByAsset.GetValueOrDefault( depositAssetId ) + amount;

			return amount;
		}

		if( available <= 0 ) {
			return 0;
		}

		decimal applied = Math.Min( available, amount );
		backlogByAsset[roomAssetId] = available - applied;
		appliedByAsset[depositAssetId] = appliedByAsset.GetValueOrDefault( depositAssetId ) + applied;

		return applied;
	}
}
