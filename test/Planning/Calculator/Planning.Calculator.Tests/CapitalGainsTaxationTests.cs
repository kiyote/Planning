using Planning.Calculator.Calculators;
using Planning.Compiler;
using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Identifiers;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Calculator.Tests;

/// <summary>
/// Covers how non-registered (CapitalGains) accounts are taxed.
///
/// Only the accrued gain on such an account is taxable, at the 50% capital-gains inclusion
/// rate. The gain is the balance less its adjusted cost base (ACB), so returning capital that
/// was originally contributed must not attract tax. Contributions raise the ACB dollar for
/// dollar, growth does not change it, and a withdrawal consumes the ACB in proportion to the
/// fraction of the balance withdrawn.
/// </summary>
public class CapitalGainsTaxationTests {

	/// <summary>
	/// Contributions into a non-registered account are made with money that has already been
	/// taxed, so they establish cost base. Withdrawing them again with no growth in between is
	/// a pure return of capital and must produce no tax at all.
	///
	/// The plan still bears ordinary income tax on CPP and OAS, so the capital-gains component
	/// is isolated by differencing against an otherwise identical plan that has no
	/// non-registered activity whatsoever.
	/// </summary>
	[Test]
	public void Calculate_ContributedCapitalWithdrawnWithoutGrowth_AddsNoTaxOverAPlanWithNoSuchAccount() {
		CalculatedPlan withCapital = Calculate(
			openingNonRegistered: 0m,
			annualReturnPercent: 0m,
			monthlyContribution: 2_000m,
			retirementIncome: 3_000m );

		CalculatedPlan withoutCapital = Calculate(
			openingNonRegistered: 0m,
			annualReturnPercent: 0m,
			monthlyContribution: 0m,
			retirementIncome: 3_000m );

		Assert.That(
			withCapital.TaxSummary.TotalTaxIncludingTerminal,
			Is.EqualTo( withoutCapital.TaxSummary.TotalTaxIncludingTerminal ).Within( 0.01m ),
			"Contributing after-tax capital and later withdrawing it must not create any tax." );
	}

	/// <summary>
	/// With no cost base at all, the entire balance is accrued gain, so half of every dollar
	/// realized is taxable. This is the boundary case where the old and new models agree.
	/// </summary>
	[Test]
	public void Calculate_AccountWithNoCostBase_TaxesHalfOfEveryDollarRealized() {
		CalculatedPlan calculatedPlan = Calculate(
			openingNonRegistered: 500_000m,
			annualReturnPercent: 0m,
			monthlyContribution: 0m,
			retirementIncome: 3_000m );

		Assert.That( calculatedPlan.TaxSummary.TotalTaxIncludingTerminal, Is.GreaterThan( 0m ) );
	}

	/// <summary>
	/// A non-registered account whose cost base equals its balance has no accrued gain, so the
	/// deemed disposition at the final death must not produce a terminal tax bill.
	/// </summary>
	[Test]
	public void Calculate_NoAccruedGainAtFinalDeath_ChargesNoTerminalTax() {
		CalculatedPlan calculatedPlan = Calculate(
			openingNonRegistered: 0m,
			annualReturnPercent: 0m,
			monthlyContribution: 2_000m,
			retirementIncome: 0m );

		Assert.Multiple( () => {
			Assert.That(
				calculatedPlan.Periods[^1].EndingAssets.Sum( a => a.Amount ),
				Is.GreaterThan( 0m ),
				"The scenario must actually leave assets behind for this to be meaningful." );
			Assert.That( calculatedPlan.TaxSummary.TerminalTax, Is.Zero );
		} );
	}

	/// <summary>
	/// Growth on a non-registered account is an accrued gain, and is realized at death. Half
	/// of that gain is taxable even though none of it was ever withdrawn during life.
	/// </summary>
	[Test]
	public void Calculate_GrowthRemainsAtFinalDeath_ChargesTerminalTaxOnTheGain() {
		CalculatedPlan calculatedPlan = Calculate(
			openingNonRegistered: 0m,
			annualReturnPercent: 6m,
			monthlyContribution: 2_000m,
			retirementIncome: 0m );

		Assert.That( calculatedPlan.TaxSummary.TerminalTax, Is.GreaterThan( 0m ) );
	}

	/// <summary>
	/// Pins the exact mechanics of cost base through a contribution, growth and a partial
	/// withdrawal, so that a regression in any one of the three is caught precisely rather
	/// than only showing up as a shifted total somewhere downstream.
	/// </summary>
	[Test]
	public void GrowAsset_ContributionThenGrowthThenWithdrawal_TracksCostBaseProportionally() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			assets: [
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m, -1m, -1m, 0m )
			],
			annualInflationPercent: 0m,
			annualReturnPercent: 0m,
			lifeInsurance: [],
			contributions: [] );

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		AssetId assetId = compiledPlan.Assets.First( a => a.TaxStatus == AssetTaxStatus.CapitalGains ).AssetId;

		// Contribute 1,000 of after-tax capital into an empty account.
		CalculatedAsset asset = new CalculatedAsset( assetId, 0m, -1m, AssetTaxStatus.CapitalGains, 0m );
		asset = Grow( plan, compiledPlan, asset, contribution: 1_000m, withdrawal: 0m );

		Assert.Multiple( () => {
			Assert.That( asset.Amount, Is.EqualTo( 1_000m ), "Balance after contribution." );
			Assert.That( asset.CostBase, Is.EqualTo( 1_000m ), "A contribution establishes cost base." );
			Assert.That( asset.AccruedGain, Is.Zero, "Contributed capital is not a gain." );
		} );

		// Simulate the account doubling in value; growth must not change cost base.
		asset = asset with { Amount = 2_000m };

		Assert.That( asset.AccruedGain, Is.EqualTo( 1_000m ), "Growth is entirely accrued gain." );

		// Withdraw half the balance: half the cost base is consumed and half the gain realized.
		asset = Grow( plan, compiledPlan, asset, contribution: 0m, withdrawal: 1_000m );

		Assert.Multiple( () => {
			Assert.That( asset.Amount, Is.EqualTo( 1_000m ), "Balance after withdrawing half." );
			Assert.That( asset.CostBase, Is.EqualTo( 500m ), "Half the cost base is consumed." );
			Assert.That( asset.AccruedGain, Is.EqualTo( 500m ), "Half the gain remains unrealized." );
		} );
	}

	private static CalculatedAsset Grow(
		Plan plan,
		CompiledPlan compiledPlan,
		CalculatedAsset asset,
		decimal contribution,
		decimal withdrawal
	) {
		return new AssetGrowthCalculator().GrowAsset(
			plan,
			compiledPlan,
			asset,
			new DateOnly( 2026, 1, 1 ),
			withdrawal > 0m ? [ new CalculatedWithdrawal( asset.AssetId, withdrawal ) ] : [],
			contribution > 0m ? [ new CalculatedContribution( asset.AssetId, contribution ) ] : []
		);
	}

	private static CalculatedPlan Calculate(
		decimal openingNonRegistered,
		decimal annualReturnPercent,
		decimal monthlyContribution,
		decimal retirementIncome
	) {
		List<Contribution> contributions = monthlyContribution > 0m
			? [ new Contribution( "Todd", monthlyContribution, 2026, Indexed: false ) ]
			: [];

		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				// Todd is born late enough that the contribution years (from 2026 until his
				// retirement at 66) actually fall inside the projection.
				new Member( "Todd", new DateOnly( 1985, 1, 1 ), 70, 66, 70, 80m ),
				// Tina predeceases Todd holding nothing, so all tax falls on Todd alone.
				new Member( "Tina", new DateOnly( 1986, 1, 1 ), 66, 65, 70, 50m )
			],
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 0m, 0m, 0m, annualReturnPercent ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0m, 0m, 0m, annualReturnPercent ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", openingNonRegistered, -1m, -1m, annualReturnPercent ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 0m, 0m, 0m, annualReturnPercent ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0m, 0m, 0m, annualReturnPercent ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m, -1m, -1m, annualReturnPercent )
			],
			annualInflationPercent: 0m,
			annualReturnPercent: annualReturnPercent,
			lifeInsurance: [],
			retirementIncome: new RetirementIncome(
				GoGo: retirementIncome,
				SlowGo: retirementIncome,
				SlowGoYears: 0,
				NoGo: retirementIncome,
				NoGoYears: 0
			),
			contributions: contributions,
			burndown: null
		);

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		return new PlanCalculator().Calculate( plan, compiledPlan );
	}
}
