using Planning.Compiler;
using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Calculator.Tests;

public class TerminalTaxTests {

	[Test]
	public void Calculate_AssetsRemainAtFinalDeath_ChargesTerminalTaxOnTheRemainingTaxableBalance() {
		CalculatedPlan calculatedPlan = Calculate( taxableAmount: 300_000m, taxExemptAmount: 0m );

		Assert.Multiple( () => {
			Assert.That( calculatedPlan.Periods[^1].EndingAssets.Sum( a => a.Amount ), Is.GreaterThan( 0m ) );
			Assert.That( calculatedPlan.TaxSummary.TerminalTax, Is.GreaterThan( 0m ) );
			Assert.That(
				calculatedPlan.TaxSummary.TerminalTax,
				Is.EqualTo( calculatedPlan.TaxSummary.TerminalFederalTax + calculatedPlan.TaxSummary.TerminalProvincialTax ) );
		} );
	}

	[Test]
	public void Calculate_OnlyTaxExemptAssetsRemainAtFinalDeath_ChargesNoTerminalTax() {
		// A TFSA passes to the estate tax-free, so nothing should fall due on the final return.
		CalculatedPlan calculatedPlan = Calculate( taxableAmount: 0m, taxExemptAmount: 300_000m );

		Assert.Multiple( () => {
			Assert.That( calculatedPlan.Periods[^1].EndingAssets.Sum( a => a.Amount ), Is.GreaterThan( 0m ) );
			Assert.That( calculatedPlan.TaxSummary.TerminalTax, Is.Zero );
		} );
	}

	[Test]
	public void Calculate_TerminalTaxIsCharged_IsExcludedFromTotalTaxButIncludedInTheLifetimeTotal() {
		CalculatedPlan calculatedPlan = Calculate( taxableAmount: 300_000m, taxExemptAmount: 0m );
		TaxSummary summary = calculatedPlan.TaxSummary;

		decimal periodTax = calculatedPlan.Periods
			.SelectMany( p => p.Taxes )
			.Sum( t => t.FederalTax + t.ProvincialTax );

		Assert.Multiple( () => {
			// TotalTax stays a pure roll-up of tax actually paid during the projection.
			Assert.That( summary.TotalTax, Is.EqualTo( periodTax ) );
			Assert.That( summary.TotalTaxIncludingTerminal, Is.EqualTo( summary.TotalTax + summary.TerminalTax ) );
		} );
	}

	[Test]
	public void Calculate_InheritedTaxableBalanceRemainsAtDeath_IsTaxedOnItsFullValueNotItsGain() {
		// An inherited RRSP/RRIF stays fully taxable: unlike a capital-gains account, no cost
		// base shelters it, so the whole remaining balance is deemed income on the final return.
		// The spousal rollover carries cost base across for the capital-gains case, and this
		// guards against that cost base ever being applied to a taxable account.
		CalculatedPlan calculatedPlan = Calculate( taxableAmount: 300_000m, taxExemptAmount: 0m );

		CalculatedPeriod finalPeriod = calculatedPlan.Periods[^1];
		decimal remainingTaxable = finalPeriod.EndingAssets
			.Where( a => a.TaxStatus == AssetTaxStatus.Taxable )
			.Sum( a => a.Amount );

		decimal impliedTaxableIncome = finalPeriod.EndingAssets
			.Where( a => a.TaxStatus == AssetTaxStatus.Taxable )
			.Sum( a => a.Amount - a.CostBase );

		Assert.Multiple( () => {
			Assert.That( remainingTaxable, Is.GreaterThan( 0m ) );
			Assert.That( calculatedPlan.TaxSummary.TerminalTax, Is.GreaterThan( 0m ) );

			// If cost base were (incorrectly) sheltering the balance, the deemed income would be
			// smaller than the balance and the terminal bill would be understated.
			Assert.That(
				impliedTaxableIncome,
				Is.EqualTo( remainingTaxable ).Within( 0.01m ),
				"A taxable account must not accumulate cost base that could shelter it at death." );
		} );
	}

	private static CalculatedPlan Calculate(
		decimal taxableAmount,
		decimal taxExemptAmount
	) {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				new Member( "Todd", new DateOnly( 1960, 1, 1 ), 70, 66, 70, 80m ),
				// Tina predeceases Todd and holds nothing, so the terminal bill is Todd's alone.
				new Member( "Tina", new DateOnly( 1961, 1, 1 ), 66, 65, 70, 50m )
			],
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", taxableAmount, 0m, 0m ),
				// Room to absorb surplus income; without it the surplus spills into the taxable
				// non-registered account and this stops being a purely tax-exempt estate.
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", taxExemptAmount, 1_000_000m, 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m, hasUnlimitedContributionRoom: true ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 0m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m, hasUnlimitedContributionRoom: true )
			],
			annualInflationPercent: 0m,
			annualReturnPercent: 0m,
			lifeInsurance: [],
			retirementIncome: new RetirementIncome(
				GoGo: 500m,
				SlowGo: 500m,
				SlowGoYears: 0,
				NoGo: 500m,
				NoGoYears: 0
			),
			contributions: [],
			burndown: null
		);

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		return new PlanCalculator().Calculate( plan, compiledPlan );
	}
}
