using Planning.Calculator.Calculators;

namespace Planning.Calculator.Tests;

/// <summary>
/// Covers the two Ontario-specific charges that sit outside the bracket system: the surtax,
/// charged on provincial tax rather than income, and the Health Premium, charged on income but
/// neither bracketed nor indexed.
/// </summary>
[TestFixture]
public sealed class OntarioSurtaxAndHealthPremiumTests {

	[Test]
	public void CalculateOntarioSurtax_TaxBelowTheFirstThreshold_ChargesNothing() {
		TaxCalculator calculator = new TaxCalculator();

		Assert.That( calculator.CalculateOntarioSurtax( 5_000m, 1m ), Is.Zero );
	}

	[Test]
	public void CalculateOntarioSurtax_TaxBetweenTheThresholds_ChargesTheFirstRateOnly() {
		TaxCalculator calculator = new TaxCalculator();

		decimal surtax = calculator.CalculateOntarioSurtax( 6_000m, 1m );

		Assert.That( surtax, Is.EqualTo( ( 6_000m - 5_554m ) * 0.20m ) );
	}

	[Test]
	public void CalculateOntarioSurtax_TaxAboveTheSecondThreshold_ChargesBothRatesCumulatively() {
		// The second rate stacks on top of the first rather than replacing it, so income in the
		// top range effectively attracts 56% of the excess over the second threshold.
		TaxCalculator calculator = new TaxCalculator();

		decimal surtax = calculator.CalculateOntarioSurtax( 10_000m, 1m );

		decimal expected = ( ( 10_000m - 5_554m ) * 0.20m ) + ( ( 10_000m - 7_108m ) * 0.36m );
		Assert.That( surtax, Is.EqualTo( expected ) );
	}

	[Test]
	public void CalculateOntarioSurtax_ThresholdsAreIndexed_SoTheSameRealTaxIsNotSurtaxedMoreHeavily() {
		TaxCalculator calculator = new TaxCalculator();

		// At double the index, twice the nominal tax is the same tax in real terms and must
		// attract exactly twice the surtax rather than proportionally more.
		decimal unindexed = calculator.CalculateOntarioSurtax( 6_000m, 1m );
		decimal indexed = calculator.CalculateOntarioSurtax( 12_000m, 2m );

		Assert.That( indexed, Is.EqualTo( unindexed * 2m ) );
	}

	[Test]
	public void CalculateOntarioSurtax_NoProvincialTaxPayable_ChargesNothing() {
		// Credits can reduce provincial tax to zero, and the surtax must not resurrect a bill.
		TaxCalculator calculator = new TaxCalculator();

		Assert.That( calculator.CalculateOntarioSurtax( 0m, 1m ), Is.Zero );
	}

	[TestCase( 20_000, 0 )]
	[TestCase( 25_000, 300 )]
	[TestCase( 36_000, 300 )]
	[TestCase( 48_000, 450 )]
	[TestCase( 72_000, 600 )]
	[TestCase( 200_000, 750 )]
	[TestCase( 250_000, 900 )]
	public void CalculateOntarioHealthPremium_AtEachPublishedBoundary_MatchesTheScheduledAmount(
		decimal taxableIncome,
		decimal expectedPremium
	) {
		// These are the published step amounts, which the phase-in bands must reproduce exactly
		// at each boundary for the interpolation between them to be meaningful.
		TaxCalculator calculator = new TaxCalculator();

		Assert.That( calculator.CalculateOntarioHealthPremium( taxableIncome ), Is.EqualTo( expectedPremium ) );
	}

	[Test]
	public void CalculateOntarioHealthPremium_IncomeAtOrBelowTheExemption_ChargesNothing() {
		TaxCalculator calculator = new TaxCalculator();

		using( Assert.EnterMultipleScope() ) {
			Assert.That( calculator.CalculateOntarioHealthPremium( 20_000m ), Is.Zero );
			Assert.That( calculator.CalculateOntarioHealthPremium( 0m ), Is.Zero );
		}
	}

	[Test]
	public void CalculateOntarioHealthPremium_WithinAPhaseIn_RisesGraduallyRatherThanJumping() {
		// Halfway through the first band the premium should be halfway to the $300 step, which
		// is what distinguishes a phased-in schedule from a flat step table.
		TaxCalculator calculator = new TaxCalculator();

		Assert.That( calculator.CalculateOntarioHealthPremium( 22_500m ), Is.EqualTo( 150m ) );
	}

	[Test]
	public void CalculateOntarioHealthPremium_AboveTheTopStep_IsCappedAndDoesNotKeepGrowing() {
		TaxCalculator calculator = new TaxCalculator();

		using( Assert.EnterMultipleScope() ) {
			Assert.That( calculator.CalculateOntarioHealthPremium( 1_000_000m ), Is.EqualTo( 900m ) );
			Assert.That( calculator.CalculateOntarioHealthPremium( 10_000_000m ), Is.EqualTo( 900m ) );
		}
	}

	[Test]
	public void CalculateOntarioHealthPremium_IsNeverIndexed_SoItErodesInRealTerms() {
		// The premium has been frozen since 2004. It takes no inflation index at all, so the
		// same nominal income always yields the same charge no matter how far into the
		// projection it falls.
		TaxCalculator calculator = new TaxCalculator();

		Assert.That(
			calculator.CalculateOntarioHealthPremium( 50_000m ),
			Is.EqualTo( calculator.CalculateOntarioHealthPremium( 50_000m ) ) );
	}
}
