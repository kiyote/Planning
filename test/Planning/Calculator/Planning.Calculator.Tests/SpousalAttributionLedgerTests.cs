using Planning.Calculator.Calculators;
using Planning.Model.Identifiers;

namespace Planning.Calculator.Tests;

public class SpousalAttributionLedgerTests {

	private static readonly MemberId Contributor = new MemberId( 1 );
	private static readonly MemberId Annuitant = new MemberId( 2 );

	[Test]
	public void Attribute_NoSpousalContributions_TaxesTheAnnuitant() {
		SpousalAttributionLedger ledger = new SpousalAttributionLedger();

		IReadOnlyList<AttributedIncome> attributed = ledger.Attribute( Annuitant, 2030, 5_000m );

		Assert.Multiple( () => {
			Assert.That( attributed, Has.Count.EqualTo( 1 ) );
			Assert.That( attributed[0].MemberId, Is.EqualTo( Annuitant ) );
			Assert.That( attributed[0].Amount, Is.EqualTo( 5_000m ) );
		} );
	}

	[Test]
	public void Attribute_WithdrawalInTheContributionYear_TaxesTheContributor() {
		SpousalAttributionLedger ledger = new SpousalAttributionLedger();
		ledger.RecordContribution( Annuitant, Contributor, 2030, 10_000m );

		IReadOnlyList<AttributedIncome> attributed = ledger.Attribute( Annuitant, 2030, 4_000m );

		Assert.Multiple( () => {
			Assert.That( attributed, Has.Count.EqualTo( 1 ) );
			Assert.That( attributed[0].MemberId, Is.EqualTo( Contributor ) );
			Assert.That( attributed[0].Amount, Is.EqualTo( 4_000m ) );
		} );
	}

	[TestCase( 2030 )]
	[TestCase( 2031 )]
	[TestCase( 2032 )]
	public void Attribute_WithdrawalInsideTheThreeYearWindow_TaxesTheContributor( int withdrawalYear ) {
		SpousalAttributionLedger ledger = new SpousalAttributionLedger();
		ledger.RecordContribution( Annuitant, Contributor, 2030, 10_000m );

		IReadOnlyList<AttributedIncome> attributed = ledger.Attribute( Annuitant, withdrawalYear, 4_000m );

		Assert.Multiple( () => {
			Assert.That( attributed, Has.Count.EqualTo( 1 ) );
			Assert.That( attributed[0].MemberId, Is.EqualTo( Contributor ) );
		} );
	}

	[Test]
	public void Attribute_WithdrawalAfterTheWindowHasElapsed_TaxesTheAnnuitant() {
		SpousalAttributionLedger ledger = new SpousalAttributionLedger();
		ledger.RecordContribution( Annuitant, Contributor, 2030, 10_000m );

		// The window covers 2030 through 2032, so a 2033 withdrawal is the annuitant's income.
		IReadOnlyList<AttributedIncome> attributed = ledger.Attribute( Annuitant, 2033, 4_000m );

		Assert.Multiple( () => {
			Assert.That( attributed, Has.Count.EqualTo( 1 ) );
			Assert.That( attributed[0].MemberId, Is.EqualTo( Annuitant ) );
		} );
	}

	[Test]
	public void Attribute_WithdrawalExceedingTheWindow_SplitsBetweenContributorAndAnnuitant() {
		SpousalAttributionLedger ledger = new SpousalAttributionLedger();
		ledger.RecordContribution( Annuitant, Contributor, 2030, 10_000m );

		IReadOnlyList<AttributedIncome> attributed = ledger.Attribute( Annuitant, 2030, 25_000m );

		Assert.Multiple( () => {
			Assert.That( attributed.Single( a => a.MemberId == Contributor ).Amount, Is.EqualTo( 10_000m ) );
			Assert.That( attributed.Single( a => a.MemberId == Annuitant ).Amount, Is.EqualTo( 15_000m ) );
		} );
	}

	[Test]
	public void Attribute_SuccessiveWithdrawals_DoNotReattributeTheSameContributions() {
		SpousalAttributionLedger ledger = new SpousalAttributionLedger();
		ledger.RecordContribution( Annuitant, Contributor, 2030, 10_000m );

		IReadOnlyList<AttributedIncome> first = ledger.Attribute( Annuitant, 2030, 6_000m );
		IReadOnlyList<AttributedIncome> second = ledger.Attribute( Annuitant, 2030, 6_000m );

		Assert.Multiple( () => {
			// The first withdrawal consumes 6,000 of the pool, leaving only 4,000 attributable.
			Assert.That( first.Single().MemberId, Is.EqualTo( Contributor ) );
			Assert.That( second.Single( a => a.MemberId == Contributor ).Amount, Is.EqualTo( 4_000m ) );
			Assert.That( second.Single( a => a.MemberId == Annuitant ).Amount, Is.EqualTo( 2_000m ) );
		} );
	}

	[Test]
	public void Attribute_ContributionsAcrossYears_ConsumesTheMostRecentFirst() {
		SpousalAttributionLedger ledger = new SpousalAttributionLedger();
		ledger.RecordContribution( Annuitant, Contributor, 2030, 5_000m );
		ledger.RecordContribution( Annuitant, Contributor, 2032, 5_000m );

		// A 2032 withdrawal draws against the 2032 contributions, leaving the 2030 contributions
		// to age out of the window rather than being preserved for future attribution.
		ledger.Attribute( Annuitant, 2032, 5_000m );
		IReadOnlyList<AttributedIncome> later = ledger.Attribute( Annuitant, 2034, 5_000m );

		Assert.Multiple( () => {
			Assert.That( later.Single().MemberId, Is.EqualTo( Annuitant ) );
			Assert.That( later.Single().Amount, Is.EqualTo( 5_000m ) );
		} );
	}

	[Test]
	public void Attribute_WithdrawalExceedingTheNewestYear_FallsBackToOlderContributions() {
		SpousalAttributionLedger ledger = new SpousalAttributionLedger();
		ledger.RecordContribution( Annuitant, Contributor, 2030, 5_000m );
		ledger.RecordContribution( Annuitant, Contributor, 2032, 5_000m );

		// Both years are inside the window, so a large enough withdrawal still reaches the whole
		// pool regardless of the order it is consumed in.
		IReadOnlyList<AttributedIncome> attributed = ledger.Attribute( Annuitant, 2032, 12_000m );

		Assert.Multiple( () => {
			Assert.That( attributed.Single( a => a.MemberId == Contributor ).Amount, Is.EqualTo( 10_000m ) );
			Assert.That( attributed.Single( a => a.MemberId == Annuitant ).Amount, Is.EqualTo( 2_000m ) );
		} );
	}

	[Test]
	public void Attribute_OldestContributionsAgeOut_LeavingNothingAttributable() {
		SpousalAttributionLedger ledger = new SpousalAttributionLedger();
		ledger.RecordContribution( Annuitant, Contributor, 2030, 5_000m );
		ledger.RecordContribution( Annuitant, Contributor, 2032, 5_000m );

		ledger.Attribute( Annuitant, 2032, 5_000m );

		// By 2035 the window spans 2033 onward, so both contributions have aged out entirely.
		IReadOnlyList<AttributedIncome> later = ledger.Attribute( Annuitant, 2035, 5_000m );

		Assert.That( later.Single().MemberId, Is.EqualTo( Annuitant ) );
	}

	[Test]
	public void Attribute_WithdrawalFromTheContributorsOwnPlan_TaxesTheContributor() {
		SpousalAttributionLedger ledger = new SpousalAttributionLedger();
		ledger.RecordContribution( Annuitant, Contributor, 2030, 10_000m );

		// The attribution pool belongs to the annuitant's plan, so the contributor's own plan is
		// unaffected by it.
		IReadOnlyList<AttributedIncome> attributed = ledger.Attribute( Contributor, 2030, 4_000m );

		Assert.That( attributed.Single().MemberId, Is.EqualTo( Contributor ) );
	}

	[Test]
	public void Prune_AfterTheWindowElapses_LeavesTheAnnuitantTaxable() {
		SpousalAttributionLedger ledger = new SpousalAttributionLedger();
		ledger.RecordContribution( Annuitant, Contributor, 2030, 10_000m );

		ledger.Prune( 2034 );
		IReadOnlyList<AttributedIncome> attributed = ledger.Attribute( Annuitant, 2034, 4_000m );

		Assert.That( attributed.Single().MemberId, Is.EqualTo( Annuitant ) );
	}

	[Test]
	public void Prune_InsideTheWindow_RetainsAttribution() {
		SpousalAttributionLedger ledger = new SpousalAttributionLedger();
		ledger.RecordContribution( Annuitant, Contributor, 2030, 10_000m );

		ledger.Prune( 2032 );
		IReadOnlyList<AttributedIncome> attributed = ledger.Attribute( Annuitant, 2032, 4_000m );

		Assert.That( attributed.Single().MemberId, Is.EqualTo( Contributor ) );
	}

	[Test]
	public void RecordContribution_NonPositiveAmount_IsIgnored() {
		SpousalAttributionLedger ledger = new SpousalAttributionLedger();
		ledger.RecordContribution( Annuitant, Contributor, 2030, 0m );

		IReadOnlyList<AttributedIncome> attributed = ledger.Attribute( Annuitant, 2030, 4_000m );

		Assert.That( attributed.Single().MemberId, Is.EqualTo( Annuitant ) );
	}
}
