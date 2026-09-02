using Planning.Calculator.Calculators;
using Planning.Compiler;
using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Identifiers;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Calculator.Tests;

/// <summary>
/// Covers the indexing of annual contribution room. CRA indexes both RRSP and TFSA dollar
/// limits, and additionally rounds the TFSA limit to the nearest $500, which is why the
/// published TFSA limit moves in occasional steps rather than every year.
/// </summary>
[TestFixture]
public sealed class ContributionRoomIndexingTests {

	private const string MemberTodd = "Todd";
	private const string MemberTina = "Tina";

	[Test]
	public void AllocateContributions_NoInflation_AccruesTheStatedNominalLimit() {
		// The baseline: with a neutral index the accrual must be exactly the configured limit,
		// so any indexing effect measured elsewhere is attributable to the index alone.
		CompiledPlan compiledPlan = CompilePlan();

		decimal room = AccruedRoom( compiledPlan, AssetTaxStatus.Taxable, inflationIndex: 1m );

		Assert.That( room, Is.EqualTo( 10_000m ) );
	}

	[Test]
	public void AllocateContributions_RegisteredRoom_IsIndexedWithoutRounding() {
		// RRSP room is derived from earned income and is indexed but not rounded to a step, so
		// it scales smoothly with the index.
		CompiledPlan compiledPlan = CompilePlan();

		decimal room = AccruedRoom( compiledPlan, AssetTaxStatus.Taxable, inflationIndex: 1.234m );

		Assert.That( room, Is.EqualTo( 10_000m * 1.234m ) );
	}

	[Test]
	public void AllocateContributions_TaxExemptRoom_IsRoundedToTheNearestFiveHundred() {
		// 7,000 x 1.234 = 8,638, which must present as the published-style 8,500 limit rather
		// than an unrounded amount that CRA would never actually announce.
		CompiledPlan compiledPlan = CompilePlan();

		decimal room = AccruedRoom( compiledPlan, AssetTaxStatus.TaxExempt, inflationIndex: 1.234m );

		Assert.That( room, Is.EqualTo( 8_500m ) );
	}

	[Test]
	public void AllocateContributions_TaxExemptRoom_RoundsUpAtTheMidpoint() {
		// 7,000 x 1.25 = 8,750, exactly between 8,500 and 9,000. CRA rounds to the nearest
		// increment, so the midpoint must go up rather than banker's-round down to 8,500.
		CompiledPlan compiledPlan = CompilePlan();

		decimal room = AccruedRoom( compiledPlan, AssetTaxStatus.TaxExempt, inflationIndex: 1.25m );

		Assert.That( room, Is.EqualTo( 9_000m ) );
	}

	[Test]
	public void AllocateContributions_TaxExemptRoom_HoldsTheSameLimitUntilIndexingCrossesTheNextStep() {
		// The rounding is what produces the TFSA's characteristic plateaus: modest inflation
		// leaves the published limit unchanged for several years at a time.
		CompiledPlan compiledPlan = CompilePlan();

		decimal justBelowStep = AccruedRoom( compiledPlan, AssetTaxStatus.TaxExempt, inflationIndex: 1.03m );
		decimal slightlyHigher = AccruedRoom( compiledPlan, AssetTaxStatus.TaxExempt, inflationIndex: 1.06m );

		using( Assert.EnterMultipleScope() ) {
			Assert.That( justBelowStep, Is.EqualTo( 7_000m ) );
			Assert.That( slightlyHigher, Is.EqualTo( 7_500m ) );
		}
	}

	[Test]
	public void AllocateContributions_OverALongProjection_GrantsMoreRoomThanAnUnindexedLimit() {
		// The point of the change: an unindexed limit steadily understates available shelter,
		// pushing money into taxable accounts earlier than it should.
		CompiledPlan compiledPlan = CompilePlan();

		decimal unindexed = AccruedRoom( compiledPlan, AssetTaxStatus.TaxExempt, inflationIndex: 1m );
		decimal afterDecades = AccruedRoom( compiledPlan, AssetTaxStatus.TaxExempt, inflationIndex: 2.5m );

		Assert.That( afterDecades, Is.GreaterThan( unindexed ) );
	}

	[Test]
	public void AllocateContributions_AssetStatingItsOwnIncrease_IndexesAtThatRateRatherThanInflation() {
		// The accrual year is 2027 and the plan starts in 2026, so a 10% rate compounds once:
		// 10,000 x 1.1 = 11,000. The supplied inflation index of 1.234 would instead give 12,340,
		// so the assertion fails if the plan-wide rate is used. It also pins the anchor to the
		// plan start rather than the tax policy year of 2024, which would compound three times.
		CompiledPlan compiledPlan = CompilePlan( annualContributionIncreasePercent: 10m );

		decimal room = AccruedRoom( compiledPlan, AssetTaxStatus.Taxable, inflationIndex: 1.234m );

		Assert.That( room, Is.EqualTo( 11_000m ).Within( 0.01m ) );
	}

	[Test]
	public void AllocateContributions_AssetStatingItsOwnIncrease_IsStillRoundedWhenTaxExempt() {
		// The override changes only the rate, not the CRA-style $500 step: 7,000 x 1.1 = 7,700,
		// which must present as 7,500.
		CompiledPlan compiledPlan = CompilePlan( annualContributionIncreasePercent: 10m );

		decimal room = AccruedRoom( compiledPlan, AssetTaxStatus.TaxExempt, inflationIndex: 1m );

		Assert.That( room, Is.EqualTo( 7_500m ) );
	}

	[Test]
	public void AllocateContributions_AssetWithoutAnIncreaseRate_FallsBackToPlanInflation() {
		// A null rate must reproduce the inflation-indexed figure exactly, so existing plans that
		// say nothing about the limit keep behaving as they always did.
		CompiledPlan compiledPlan = CompilePlan( annualContributionIncreasePercent: null );

		decimal room = AccruedRoom( compiledPlan, AssetTaxStatus.Taxable, inflationIndex: 1.234m );

		Assert.That( room, Is.EqualTo( 12_340m ) );
	}

	/// <summary>
	/// Accrues a January's room for the first asset of the given status and returns the increase
	/// over the backlog it started with, which isolates the accrual from the seed backlog.
	/// </summary>
	private static decimal AccruedRoom(
		CompiledPlan compiledPlan,
		AssetTaxStatus taxStatus,
		decimal inflationIndex
	) {
		CompiledAsset target = compiledPlan.Assets.First( a => a.TaxStatus == taxStatus );

		IReadOnlyList<CalculatedAsset> assets = [
			.. compiledPlan.Assets.Select( a => new CalculatedAsset(
				a.AssetId, a.Amount, a.ContributionBacklog, a.TaxStatus, a.HasUnlimitedContributionRoom, a.CostBase ) )
		];

		DateOnly periodDate = new DateOnly( 2027, 1, 1 );
		CompiledPeriod period = compiledPlan.Periods.First( p => p.PeriodDate == periodDate );

		ContributionAllocation allocation = new ContributionPolicy().AllocateContributions(
			compiledPlan,
			assets,
			periodDate,
			isFirstPeriod: false,
			compiledPlan.Contribution[period],
			new Dictionary<AssetId, decimal>(),
			compiledPlan.Periods.First().PeriodDate.Year,
			inflationIndex );

		decimal before = target.ContributionBacklog;
		decimal after = allocation.Assets.Single( a => a.AssetId == target.AssetId ).ContributionBacklog;

		return after - before;
	}

	private static CompiledPlan CompilePlan(
		decimal? annualContributionIncreasePercent = null
	) {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				new Member( MemberTodd, new DateOnly( 1975, 6, 1 ), 95, 90, 70, 80m ),
				new Member( MemberTina, new DateOnly( 1976, 6, 1 ), 95, 90, 70, 50m )
			],
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, MemberTodd, 100_000m, 50_000m, 10_000m, annualContributionIncreasePercent: annualContributionIncreasePercent ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, MemberTodd, 0m, 50_000m, 7_000m, annualContributionIncreasePercent: annualContributionIncreasePercent ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, MemberTodd, 0m, hasUnlimitedContributionRoom: true ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, MemberTina, 100_000m, 50_000m, 10_000m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, MemberTina, 0m, 50_000m, 7_000m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, MemberTina, 0m, hasUnlimitedContributionRoom: true )
			],
			annualInflationPercent: 0m,
			annualReturnPercent: 0m,
			contributions: []
		);

		return new PlanCompiler().Compile( plan );
	}
}
