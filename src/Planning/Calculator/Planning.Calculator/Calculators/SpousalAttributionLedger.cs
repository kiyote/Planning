using Planning.Model.Identifiers;

namespace Planning.Calculator.Calculators;

/// <summary>
/// The split of a withdrawal's taxable income between the annuitant who owns the account and
/// the spouse who contributed the funds being withdrawn.
/// </summary>
internal sealed record AttributedIncome(
	MemberId MemberId,
	decimal Amount
);

/// <summary>
/// Tracks spousal contributions so that withdrawals falling inside the attribution window are
/// taxed back to the contributing spouse rather than the annuitant.
///
/// Under the CRA rule, a withdrawal from a spousal plan is included in the contributor's income
/// up to the total the contributor put into any spousal plan in the year of withdrawal and the
/// two preceding calendar years. Anything beyond that is taxed to the annuitant as usual.
/// </summary>
internal sealed class SpousalAttributionLedger {

	/// <summary>
	/// The withdrawal year plus the two preceding calendar years are attributable.
	/// </summary>
	private const int AttributionYears = 3;

	private readonly Dictionary<(MemberId Annuitant, MemberId Contributor, int Year), decimal> _contributions = [];

	/// <summary>
	/// Records a spousal contribution that was actually applied, making it attributable for the
	/// year it was made and the two years that follow.
	/// </summary>
	public void RecordContribution(
		MemberId annuitantMemberId,
		MemberId contributorMemberId,
		int year,
		decimal amount
	) {
		if( amount <= 0m ) {
			return;
		}

		(MemberId, MemberId, int) key = (annuitantMemberId, contributorMemberId, year);
		_contributions[key] = _contributions.GetValueOrDefault( key ) + amount;
	}

	/// <summary>
	/// Splits <paramref name="taxableAmount"/> withdrawn from <paramref name="annuitantMemberId"/>'s
	/// registered account between the contributors it is attributed back to and the annuitant.
	///
	/// Attributed amounts are consumed from the window as they are used, so a later withdrawal in
	/// the same year cannot be attributed against contributions already accounted for. The most
	/// recent contributions are consumed first: attribution is a pooled three-year calculation, and
	/// drawing down the newest contributions leaves the oldest to age out of the window as the rule
	/// intends. Consuming the oldest first would instead always retain the contributions with the
	/// longest remaining life, perpetuating attribution well beyond three years.
	/// </summary>
	public IReadOnlyList<AttributedIncome> Attribute(
		MemberId annuitantMemberId,
		int withdrawalYear,
		decimal taxableAmount
	) {
		if( taxableAmount <= 0m ) {
			return [];
		}

		int earliestYear = withdrawalYear - ( AttributionYears - 1 );

		List<(MemberId Annuitant, MemberId Contributor, int Year)> openWindow = [
			.. _contributions.Keys
				.Where( k => k.Annuitant == annuitantMemberId
					&& k.Year >= earliestYear
					&& k.Year <= withdrawalYear
					&& _contributions[k] > 0m )
				.OrderByDescending( k => k.Year )
		];

		if( openWindow.Count == 0 ) {
			return [new AttributedIncome( annuitantMemberId, taxableAmount )];
		}

		Dictionary<MemberId, decimal> attributed = [];
		decimal remaining = taxableAmount;

		foreach( (MemberId Annuitant, MemberId Contributor, int Year) key in openWindow ) {
			if( remaining <= 0m ) {
				break;
			}

			decimal available = _contributions[key];
			decimal used = Math.Min( available, remaining );

			_contributions[key] = available - used;
			attributed[key.Contributor] = attributed.GetValueOrDefault( key.Contributor ) + used;
			remaining -= used;
		}

		List<AttributedIncome> result = [
			.. attributed.Select( kvp => new AttributedIncome( kvp.Key, kvp.Value ) )
		];

		// Whatever the window could not absorb stays with the annuitant.
		if( remaining > 0m ) {
			result.Add( new AttributedIncome( annuitantMemberId, remaining ) );
		}

		return result;
	}

	/// <summary>
	/// Discards contributions that have aged out of the window, keeping the ledger bounded over a
	/// projection that can run for many decades.
	/// </summary>
	public void Prune( int currentYear ) {
		int earliestYear = currentYear - ( AttributionYears - 1 );

		List<(MemberId, MemberId, int)> expired = [
			.. _contributions.Keys.Where( k => k.Year < earliestYear )
		];

		foreach( (MemberId, MemberId, int) key in expired ) {
			_contributions.Remove( key );
		}
	}
}
