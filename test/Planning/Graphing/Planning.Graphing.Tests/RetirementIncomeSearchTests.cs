using Planning.Calculator;
using Planning.Compiler;
using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Graphing.Tests;

/// <summary>
/// A reusable search that, holding a fixed Go-Go / Slow-Go / No-Go ratio, finds the largest
/// retirement-income trio the plan can sustain without any shortfall. Vary the plan inputs in
/// <see cref="CreatePlan"/> (or the ratios / bounds in <see cref="FindMaxNoShortfall"/>) and the
/// search will report the appropriate incomes that keep the plan fully funded.
/// </summary>
public class RetirementIncomeSearchTests {

	// Ratios of the three retirement-income phases relative to Go-Go (Go-Go = 100%).
	private const decimal SlowGoRatio = 0.80m;
	private const decimal NoGoRatio = 0.90m;

	// Bounds and precision for the binary search over the Go-Go monthly amount.
	private const decimal MinGoGo = 0m;
	private const decimal MaxGoGo = 50_000m;
	private const decimal Tolerance = 1m;

	[Test]
	public void FindMaxNoShortfall() {
		decimal low = MinGoGo;
		decimal high = MaxGoGo;

		// Binary search for the largest Go-Go amount (at the fixed ratio) with no shortfall.
		while( high - low > Tolerance ) {
			decimal goGo = ( low + high ) / 2m;
			if( HasShortfall( goGo ) ) {
				high = goGo;
			}
			else {
				low = goGo;
			}
		}

		decimal maxGoGo = Math.Floor( low );
		decimal slowGo = Math.Floor( maxGoGo * SlowGoRatio );
		decimal noGo = Math.Floor( maxGoGo * NoGoRatio );

		CalculatedPlan result = Calculate( maxGoGo, slowGo, noGo );

		TestContext.Out.WriteLine( $"Ratios => GoGo=100% SlowGo={SlowGoRatio:P0} NoGo={NoGoRatio:P0}" );
		TestContext.Out.WriteLine( $"Max sustainable => GoGo={maxGoGo}, SlowGo={slowGo}, NoGo={noGo}" );
		TestContext.Out.WriteLine( $"HasShortfall at these values: {result.InsufficientFunds.HasShortfall}" );
		TestContext.Out.WriteLine( $"Shortfall period count: {result.InsufficientFunds.ShortfallPeriodCount}" );
		TestContext.Out.WriteLine( $"Final total assets: {result.Periods[^1].TotalAssets:F2}" );

		Assert.That( result.InsufficientFunds.HasShortfall, Is.False,
			"The reported maximum trio should not produce a shortfall." );
	}

	private static bool HasShortfall( decimal goGo ) {
		decimal slowGo = goGo * SlowGoRatio;
		decimal noGo = goGo * NoGoRatio;
		return Calculate( goGo, slowGo, noGo ).InsufficientFunds.HasShortfall;
	}

	private static CalculatedPlan Calculate( decimal goGo, decimal slowGo, decimal noGo ) {
		Plan plan = CreatePlan( goGo, slowGo, noGo );
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		return new PlanCalculator().Calculate( plan, compiledPlan );
	}

	private static Plan CreatePlan( decimal goGo, decimal slowGo, decimal noGo ) {
		return TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				new Member( "Todd", new DateOnly( 1973, 12, 31 ), 85, 60, 70, 90m ),
				new Member( "Tina", new DateOnly( 1976, 7, 22 ), 95, null, 65, 50m )
			],
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 550000m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0m ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 30000m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0m )
			],
			annualInflationPercent: 3.0m,
			annualReturnPercent: 6.0m,
			lifeInsurance: [
				new LifeInsurance( "Todd", 250000 ),
				new LifeInsurance( "Tina", 250000 )
			],
			retirementIncome: new RetirementIncome(
				GoGo: goGo,
				SlowGo: slowGo,
				SlowGoYears: 10,
				NoGo: noGo,
				NoGoYears: 10
			),
			contributions: [
				new Contribution( "Todd", 3500, 2026, Indexed: false ),
				new Contribution( "Tina", 3000, 2028, Indexed: false )
			]
		);
	}
}
