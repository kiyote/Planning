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

	/// <summary>
	/// The age by the end of whose year a registered plan must be wound up. No contribution can
	/// be made to a registered account after December 31 of the year its annuitant turns this age.
	/// </summary>
	private const int MaximumRegisteredContributionAge = 71;

	/// <summary>
	/// The increment the TFSA dollar limit is rounded to. CRA indexes the limit to inflation but
	/// then rounds it to the nearest $500, which is why the published limit moves in occasional
	/// steps rather than drifting upward every year.
	/// </summary>
	private const decimal TaxExemptRoomRoundingIncrement = 500m;

	// Contribution preference: fill Taxable room first, then TaxExempt, then CapitalGains.
	// This is deliberately not the strict inverse of the withdrawal ordering.
	private static readonly AssetTaxStatus[] _contributionStatusOrder = [
		AssetTaxStatus.Taxable,
		AssetTaxStatus.TaxExempt,
		AssetTaxStatus.CapitalGains
	];

	/// <param name="restoredRoomByAsset">
	/// Room given back by the prior calendar year's withdrawals, credited on January 1 alongside
	/// the annual accrual. Only tax-exempt accounts restore room this way.
	/// </param>
	/// <param name="inflationIndex">
	/// Multiplier that indexes the annual contribution limit for the year being accrued. Limits
	/// are stated in nominal start-year dollars, and CRA indexes both RRSP and TFSA room, so an
	/// unindexed limit would understate available shelter over a long projection.
	/// </param>
	public ContributionAllocation AllocateContributions(
		CompiledPlan compiledPlan,
		IReadOnlyList<CalculatedAsset> assets,
		DateOnly periodDate,
		bool isFirstPeriod,
		IEnumerable<CompiledContribution> contributions,
		IReadOnlyDictionary<AssetId, decimal> restoredRoomByAsset,
		int planStartYear,
		decimal inflationIndex
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

				// Room handed back by last year's withdrawals is credited whether or not fresh
				// room accrues this year, because it is the member's own room returning rather
				// than a new entitlement being earned.
				decimal restoredRoom = restoredRoomByAsset.GetValueOrDefault( compiledAsset.AssetId );

				// Taxable room stops accruing in every year after the owner retires, since the
				// room is earned from employment income.
				decimal accruedRoom =
					compiledAsset.TaxStatus == AssetTaxStatus.Taxable
					&& IsAfterRetirementYear( compiledPlan, compiledAsset, periodDate )
						? 0m
						: IndexedAnnualRoom( compiledAsset, periodDate, planStartYear, inflationIndex );

				decimal addedRoom = accruedRoom + restoredRoom;
				if( addedRoom <= 0m ) {
					continue;
				}

				if( backlogByAsset.TryGetValue( compiledAsset.AssetId, out decimal backlog )
					&& !unlimitedRoomAssets.Contains( compiledAsset.AssetId )
				) {
					backlogByAsset[compiledAsset.AssetId] = backlog + addedRoom;
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
						&& a.TaxStatus == status
						&& !IsRegisteredPlanClosedToContributions( compiledPlan, a, periodDate ) );

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
	/// The annual contribution room accrued for an asset in the year being calculated, indexed
	/// from the nominal limit the plan states.
	///
	/// An asset that states its own increase rate is indexed at that rate instead of the plan's
	/// inflation, for a limit expected to move on something other than the cost of living. Both
	/// are anchored on the plan's start year, since that is the year the stated limit is expressed
	/// in, so the two are interchangeable and an unstated rate reproduces the inflation-indexed
	/// figure exactly.
	///
	/// Tax-exempt (TFSA) room is additionally rounded to the nearest $500, because CRA indexes
	/// the TFSA dollar limit and then rounds it, producing the occasional step increases seen in
	/// the published limits. Registered room derived from earned income is not rounded that way,
	/// so it is only indexed.
	/// </summary>
	private static decimal IndexedAnnualRoom(
		CompiledAsset asset,
		DateOnly periodDate,
		int planStartYear,
		decimal inflationIndex
	) {
		decimal index = asset.AnnualContributionIncreasePercent.HasValue
			? (decimal)Math.Pow(
				(double)( 1m + ( asset.AnnualContributionIncreasePercent.Value / 100m ) ),
				periodDate.Year - planStartYear )
			: inflationIndex;

		decimal indexed = asset.AnnualContributionLimit * index;

		if( asset.TaxStatus != AssetTaxStatus.TaxExempt ) {
			return indexed;
		}

		return Math.Round(
			indexed / TaxExemptRoomRoundingIncrement,
			MidpointRounding.AwayFromZero ) * TaxExemptRoomRoundingIncrement;
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
	/// Whether the asset is a registered plan that can no longer accept contributions because its
	/// annuitant has passed December 31 of the year they turned 71, by which point the plan must
	/// be wound up. The test is on the annuitant rather than the contributor, which is what makes
	/// a spousal contribution to a younger spouse remain permissible after the contributor has
	/// passed that age, while a contribution to their own plan does not.
	/// </summary>
	private static bool IsRegisteredPlanClosedToContributions(
		CompiledPlan compiledPlan,
		CompiledAsset asset,
		DateOnly periodDate
	) {
		if( asset.TaxStatus != AssetTaxStatus.Taxable ) {
			return false;
		}

		CompiledMember member = compiledPlan.Members.Single( m => m.MemberId == asset.MemberId );

		return periodDate.Year > member.BirthDate.Year + MaximumRegisteredContributionAge;
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
