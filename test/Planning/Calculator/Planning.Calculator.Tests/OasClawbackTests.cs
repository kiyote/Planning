using Planning.Calculator.Calculators;
using Planning.Compiler;
using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Calculator.Tests;

/// <summary>
/// Covers the OAS recovery tax (clawback).
///
/// OAS is clawed back at a fixed rate on net income above a threshold, and the amount
/// recovered is capped at the OAS actually received in the year, because a member can never
/// repay more OAS than they were paid. Unlike the Age Amount and Pension Income Amount, the
/// clawback is a tax rather than a credit, so it is added to federal tax rather than
/// reducing it, and it applies even when credits have already reduced federal tax to zero.
///
/// The threshold is expressed in nominal start-year dollars and is indexed by inflation for
/// the year being calculated, matching how bracket thresholds and the Age Amount are handled.
/// </summary>
public class OasClawbackTests {

	private static readonly TaxPolicy Policy = TestPlanFactory.CreateTaxPolicy() with {
		OasClawbackThreshold = 90_000m,
		OasClawbackRate = 15m
	};

	/// <summary>
	/// Below the threshold there is no recovery at all, no matter how much OAS was received.
	/// </summary>
	[Test]
	public void CalculateOasClawback_NetIncomeBelowThreshold_RecoversNothing() {
		decimal clawback = new TaxCalculator().CalculateOasClawback(
			Policy,
			netIncome: 89_999m,
			oasReceived: 9_000m,
			inflationIndex: 1m );

		Assert.That( clawback, Is.EqualTo( 0m ),
			"No OAS may be recovered from a member whose net income is under the threshold." );
	}

	/// <summary>
	/// Above the threshold the recovery is the policy rate applied to the excess only, not to
	/// the whole of net income.
	/// </summary>
	[Test]
	public void CalculateOasClawback_NetIncomeAboveThreshold_RecoversTheRateOnTheExcessOnly() {
		decimal clawback = new TaxCalculator().CalculateOasClawback(
			Policy,
			netIncome: 100_000m,
			oasReceived: 9_000m,
			inflationIndex: 1m );

		// ( 100,000 - 90,000 ) * 15% = 1,500
		Assert.That( clawback, Is.EqualTo( 1_500m ).Within( 0.01m ),
			"The clawback must apply the rate to income above the threshold, not to all income." );
	}

	/// <summary>
	/// This is the rule that makes the clawback different from an ordinary surtax: however far
	/// income runs above the threshold, the member cannot repay more than they were paid.
	/// </summary>
	[Test]
	public void CalculateOasClawback_IncomeFarAboveThreshold_RecoversNoMoreThanTheOasReceived() {
		decimal clawback = new TaxCalculator().CalculateOasClawback(
			Policy,
			netIncome: 500_000m,
			oasReceived: 9_000m,
			inflationIndex: 1m );

		// The uncapped recovery would be ( 500,000 - 90,000 ) * 15% = 61,500.
		Assert.That( clawback, Is.EqualTo( 9_000m ),
			"The clawback is capped at the OAS received; a member cannot repay more than they were paid." );
	}

	/// <summary>
	/// A member who received no OAS has nothing to recover, regardless of their income.
	/// </summary>
	[Test]
	public void CalculateOasClawback_NoOasReceived_RecoversNothing() {
		decimal clawback = new TaxCalculator().CalculateOasClawback(
			Policy,
			netIncome: 500_000m,
			oasReceived: 0m,
			inflationIndex: 1m );

		Assert.That( clawback, Is.EqualTo( 0m ),
			"A member who received no OAS has no OAS to repay." );
	}

	/// <summary>
	/// The threshold is stated in start-year dollars, so it must rise with inflation. Without
	/// indexing, a fixed threshold would claw back progressively more each year purely because
	/// nominal incomes inflate, which would overstate tax in later years.
	/// </summary>
	[Test]
	public void CalculateOasClawback_ThresholdIsIndexed_SoInflatedIncomeIsNotClawedBackMoreHeavily() {
		TaxCalculator calculator = new();

		decimal unindexed = calculator.CalculateOasClawback(
			Policy, netIncome: 100_000m, oasReceived: 9_000m, inflationIndex: 1m );

		// Income and the threshold both inflated by the same 10%: the real position is
		// unchanged, so the real clawback should be unchanged too.
		decimal indexed = calculator.CalculateOasClawback(
			Policy, netIncome: 110_000m, oasReceived: 9_900m, inflationIndex: 1.1m );

		Assert.That( indexed / 1.1m, Is.EqualTo( unindexed ).Within( 0.01m ),
			"Indexing the threshold must leave the clawback unchanged in real terms." );
	}

	/// <summary>
	/// Disabling the clawback must be possible so that its effect can be isolated, and so that
	/// a plan modelling a jurisdiction without a recovery tax can opt out.
	/// </summary>
	[Test]
	public void CalculateOasClawback_ThresholdOfZero_DisablesTheClawback() {
		decimal clawback = new TaxCalculator().CalculateOasClawback(
			Policy with { OasClawbackThreshold = 0m },
			netIncome: 500_000m,
			oasReceived: 9_000m,
			inflationIndex: 1m );

		Assert.That( clawback, Is.EqualTo( 0m ),
			"A threshold of zero must disable the clawback rather than clawing back everything." );
	}

	/// <summary>
	/// End-to-end check that the clawback is actually wired into the annual settlement: a plan
	/// whose retiree draws a large RRIF income must pay more total tax with the clawback
	/// enabled than with it disabled. This pins the integration, not just the arithmetic.
	/// </summary>
	[Test]
	public void Calculate_HighIncomeRetiree_PaysMoreTaxWithTheClawbackEnabled() {
		CalculatedPlan withClawback = Calculate( clawbackThreshold: 90_000m );
		CalculatedPlan withoutClawback = Calculate( clawbackThreshold: 0m );

		Assert.That(
			withClawback.TaxSummary.TotalTaxIncludingTerminal,
			Is.GreaterThan( withoutClawback.TaxSummary.TotalTaxIncludingTerminal ),
			"A retiree with income above the threshold must bear additional tax from the OAS clawback." );
	}

	/// <summary>
	/// The complement of the test above: a household whose income never approaches the
	/// threshold must be completely unaffected. This guards against the clawback leaking into
	/// low-income plans through a mis-tracked OAS amount.
	/// </summary>
	[Test]
	public void Calculate_LowIncomeRetiree_IsUnaffectedByTheClawback() {
		CalculatedPlan withClawback = Calculate( clawbackThreshold: 90_000m, retirementIncome: 1_500m, rrsp: 40_000m );
		CalculatedPlan withoutClawback = Calculate( clawbackThreshold: 0m, retirementIncome: 1_500m, rrsp: 40_000m );

		Assert.That(
			withClawback.TaxSummary.TotalTaxIncludingTerminal,
			Is.EqualTo( withoutClawback.TaxSummary.TotalTaxIncludingTerminal ).Within( 0.01m ),
			"A household that never exceeds the threshold must pay exactly the same tax either way." );
	}

	private static CalculatedPlan Calculate(
		decimal clawbackThreshold,
		decimal retirementIncome = 12_000m,
		decimal rrsp = 1_500_000m
	) {
		TaxPolicy policy = TestPlanFactory.CreateTaxPolicy() with {
			OasClawbackThreshold = clawbackThreshold,
			OasClawbackRate = 15m
		};

		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				new Member( "Todd", new DateOnly( 1960, 1, 1 ), 80, 66, 65, 100m ),
				new Member( "Tina", new DateOnly( 1961, 1, 1 ), 80, 66, 65, 100m )
			],
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", rrsp, 0m, 0m, 5m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0m, 0m, 0m, 5m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m, -1m, -1m, 5m ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 0m, 0m, 0m, 5m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0m, 0m, 0m, 5m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m, -1m, -1m, 5m )
			],
			annualInflationPercent: 0m,
			annualReturnPercent: 5m,
			lifeInsurance: [],
			retirementIncome: new RetirementIncome(
				GoGo: retirementIncome,
				SlowGo: retirementIncome,
				SlowGoYears: 0,
				NoGo: retirementIncome,
				NoGoYears: 0
			),
			contributions: [],
			taxPolicy: policy,
			burndown: null
		);

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		return new PlanCalculator().Calculate( plan, compiledPlan );
	}
}
