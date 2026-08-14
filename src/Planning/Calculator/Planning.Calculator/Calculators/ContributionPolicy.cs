using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Identifiers;
using Planning.Model.Plans;

namespace Planning.Calculator.Calculators;

/// <summary>
/// The result of allocating a period's contributions against the available contribution
/// room, carrying forward the room that remains.
/// </summary>
internal sealed record ContributionAllocation(
	IReadOnlyList<CalculatedContribution> Contributions,
	IReadOnlyList<CalculatedAsset> Assets
);

/// <summary>
/// Applies the annual contribution room accrual and allocates each period's contributions
/// against that room. Each contribution fills the member's accounts in the configured
/// contribution priority order, overflowing to the next account as room is exhausted.
/// </summary>
internal sealed class ContributionPolicy {

	/// <summary>
	/// Sentinel indicating an account has no contribution cap. An unlimited backlog absorbs
	/// any contribution without being consumed, and an unlimited annual limit accrues nothing.
	/// </summary>
	private const decimal Unlimited = -1m;

	// Contribution preference: fill Taxable room first, then TaxExempt, then CapitalGains.
	// This is deliberately not the strict inverse of the withdrawal ordering.
	private static readonly AssetTaxStatus[] ContributionStatusOrder = [
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

		// Contribution room accrues once per calendar year, in January. The first period is
		// skipped because the plan's seed backlog is stated as of the plan start date.
		if( periodDate.Month == 1 && !isFirstPeriod ) {
			foreach( CompiledAsset compiledAsset in compiledPlan.Assets ) {
				if( compiledAsset.AnnualContributionLimit == Unlimited ) {
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
					&& backlog != Unlimited
				) {
					backlogByAsset[compiledAsset.AssetId] = backlog + compiledAsset.AnnualContributionLimit;
				}
			}
		}

		Dictionary<AssetId, decimal> appliedByAsset = [];

		foreach( CompiledContribution contribution in contributions ) {
			decimal remaining = contribution.Amount;

			// The contribution fills the member's most tax-efficient account that still has
			// room, overflowing into progressively less efficient accounts as room runs out.
			foreach( AssetTaxStatus status in ContributionStatusOrder ) {
				IEnumerable<CompiledAsset> candidates = compiledPlan.Assets
					.Where( a => a.MemberId == contribution.MemberId
						&& a.TaxStatus == status );

				foreach( CompiledAsset candidate in candidates ) {
					remaining -= Apply( backlogByAsset, appliedByAsset, candidate.AssetId, remaining );

					if( remaining <= 0 ) {
						break;
					}
				}

				if( remaining <= 0 ) {
					break;
				}
			}

			// Any amount still remaining has no contribution room anywhere and is not contributed.
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

		return new ContributionAllocation( calculatedContributions, updatedAssets );
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
	/// Contributes as much of <paramref name="amount"/> as the asset's remaining room allows,
	/// reducing that room and returning the amount actually applied. An account with an
	/// <see cref="Unlimited"/> backlog absorbs the full amount and its backlog is left untouched.
	/// </summary>
	private static decimal Apply(
		Dictionary<AssetId, decimal> backlogByAsset,
		Dictionary<AssetId, decimal> appliedByAsset,
		AssetId assetId,
		decimal amount
	) {
		if( amount <= 0 || !backlogByAsset.TryGetValue( assetId, out decimal available ) ) {
			return 0;
		}

		if( available == Unlimited ) {
			appliedByAsset[assetId] = appliedByAsset.GetValueOrDefault( assetId ) + amount;

			return amount;
		}

		if( available <= 0 ) {
			return 0;
		}

		decimal applied = Math.Min( available, amount );
		backlogByAsset[assetId] = available - applied;
		appliedByAsset[assetId] = appliedByAsset.GetValueOrDefault( assetId ) + applied;

		return applied;
	}
}
