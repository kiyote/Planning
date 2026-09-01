using Planning.Compiler;
using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Calculator.Tests;

public class EstateSummaryTests {

	[Test]
	public void NetEstate_TerminalTaxIsCharged_DeductsTheTerminalTaxFromTheGrossEstate() {
		EstateSummary summary = new EstateSummary(
			GrossEstate: 100_000m,
			TerminalTax: 25_000m,
			FinalPeriodYear: 2026,
			PlanStartYear: 2026,
			AnnualInflationPercent: 0m
		);

		Assert.That( summary.NetEstate, Is.EqualTo( 75_000m ) );
	}

	[Test]
	public void NetEstateInPlanStartDollars_WithInflation_DiscountsBackToPlanStart() {
		// Two years at 10% compounds to 1.21, so 121,000 nominal is 100,000 in start dollars.
		EstateSummary summary = new EstateSummary(
			GrossEstate: 121_000m,
			TerminalTax: 0m,
			FinalPeriodYear: 2028,
			PlanStartYear: 2026,
			AnnualInflationPercent: 10m
		);

		Assert.That( summary.NetEstateInPlanStartDollars, Is.EqualTo( 100_000m ).Within( 0.01m ) );
	}

	[Test]
	public void NetEstateInPlanStartDollars_WithoutInflation_EqualsTheNominalNetEstate() {
		EstateSummary summary = new EstateSummary(
			GrossEstate: 50_000m,
			TerminalTax: 10_000m,
			FinalPeriodYear: 2050,
			PlanStartYear: 2026,
			AnnualInflationPercent: 0m
		);

		Assert.That( summary.NetEstateInPlanStartDollars, Is.EqualTo( 40_000m ) );
	}

	[Test]
	public void Calculate_AssetsRemainAtFinalDeath_ReportsGrossEstateMatchingTheFinalEndingAssets() {
		CalculatedPlan calculatedPlan = Calculate();

		decimal endingAssets = calculatedPlan.Periods[^1].EndingAssets.Sum( a => a.Amount );

		Assert.Multiple( () => {
			Assert.That( calculatedPlan.EstateSummary.GrossEstate, Is.EqualTo( endingAssets ) );
			Assert.That( calculatedPlan.EstateSummary.GrossEstate, Is.GreaterThan( 0m ) );
		} );
	}

	[Test]
	public void Calculate_TerminalTaxIsCharged_NetEstateIsGrossLessTheTerminalTax() {
		CalculatedPlan calculatedPlan = Calculate();
		EstateSummary estate = calculatedPlan.EstateSummary;

		// The terminal bill is assessed on the same balances the gross estate is measured from,
		// so it must be deducted exactly once to arrive at what beneficiaries receive.
		Assert.Multiple( () => {
			Assert.That( estate.TerminalTax, Is.EqualTo( calculatedPlan.TaxSummary.TerminalTax ) );
			Assert.That( estate.TerminalTax, Is.GreaterThan( 0m ) );
			Assert.That( estate.NetEstate, Is.EqualTo( estate.GrossEstate - estate.TerminalTax ) );
			Assert.That( estate.NetEstate, Is.LessThan( estate.GrossEstate ) );
		} );
	}

	private static CalculatedPlan Calculate() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				new Member( "Todd", new DateOnly( 1960, 1, 1 ), 70, 66, 70, 80m ),
				new Member( "Tina", new DateOnly( 1961, 1, 1 ), 66, 65, 70, 50m )
			],
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 300_000m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0m, 0m, 0m ),
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
