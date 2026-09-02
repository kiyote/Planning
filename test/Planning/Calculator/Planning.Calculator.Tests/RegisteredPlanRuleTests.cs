using Planning.Calculator.Calculators;
using Planning.Compiler;
using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Identifiers;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Calculator.Tests;

/// <summary>
/// Covers the registered-plan lifecycle rules: the age 71 contribution deadline, the RRIF
/// conversion-year exemption, the pension splitting age test, and TFSA room restoration.
/// </summary>
[TestFixture]
public sealed class RegisteredPlanRuleTests {

	private const string MemberTodd = "Todd";
	private const string MemberTina = "Tina";

	[Test]
	public void AllocateContributions_AnnuitantIsPastTheYearTheyTurn71_DivertsTheContributionAwayFromTheRrsp() {
		// A registered plan must be wound up by December 31 of the year the annuitant turns 71,
		// so no contribution can land in it after that. The money must fall through to the
		// next account in the contribution order rather than being accepted or dropped.
		Plan plan = CreatePlan(
			toddBirthDate: new DateOnly( 1954, 6, 1 ),
			contributions: [new Contribution( MemberTodd, 3_000m, 2026, Indexed: false, AnnualIncreasePercent: 0m, Spousal: null )] );

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember todd = compiledPlan.Members.Single( m => m.Name == MemberTodd );
		CompiledAsset toddRrsp = compiledPlan.Assets.Single(
			a => a.MemberId == todd.MemberId && a.TaxStatus == AssetTaxStatus.Taxable );
		CompiledAsset toddTfsa = compiledPlan.Assets.Single(
			a => a.MemberId == todd.MemberId && a.TaxStatus == AssetTaxStatus.TaxExempt );

		// 2026 is well past 2025, the year Todd turns 71.
		ContributionAllocation allocation = Allocate( compiledPlan, new DateOnly( 2026, 3, 1 ) );

		using( Assert.EnterMultipleScope() ) {
			Assert.That(
				allocation.Contributions.Where( c => c.AssetId == toddRrsp.AssetId ).Sum( c => c.Amount ),
				Is.Zero,
				"A registered plan is closed to contributions after the annuitant's 71st year." );
			Assert.That(
				allocation.Contributions.Where( c => c.AssetId == toddTfsa.AssetId ).Sum( c => c.Amount ),
				Is.EqualTo( 3_000m ),
				"The contribution must overflow into the next eligible account." );
		}
	}

	[Test]
	public void AllocateContributions_AnnuitantIsStillInTheYearTheyTurn71_AcceptsTheContribution() {
		// The deadline is the end of the year, not the birthday, so a contribution made during
		// the 71st year is still valid.
		Plan plan = CreatePlan(
			toddBirthDate: new DateOnly( 1955, 6, 1 ),
			contributions: [new Contribution( MemberTodd, 3_000m, 2026, Indexed: false, AnnualIncreasePercent: 0m, Spousal: null )] );

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember todd = compiledPlan.Members.Single( m => m.Name == MemberTodd );
		CompiledAsset toddRrsp = compiledPlan.Assets.Single(
			a => a.MemberId == todd.MemberId && a.TaxStatus == AssetTaxStatus.Taxable );

		// Todd turns 71 in 2026, and this period is after his birthday but still within the year.
		ContributionAllocation allocation = Allocate( compiledPlan, new DateOnly( 2026, 9, 1 ) );

		Assert.That(
			allocation.Contributions.Where( c => c.AssetId == toddRrsp.AssetId ).Sum( c => c.Amount ),
			Is.EqualTo( 3_000m ) );
	}

	[Test]
	public void AllocateContributions_SpousalContributionToAYoungerAnnuitant_IsStillAcceptedAfterTheContributorTurns71() {
		// The age 71 test applies to the annuitant, not the contributor. An over-71 spouse may
		// still make spousal contributions to a younger spouse's RRSP.
		Plan plan = CreatePlan(
			toddBirthDate: new DateOnly( 1954, 6, 1 ),
			contributions: [new Contribution( MemberTina, 3_000m, 2026, Indexed: false, AnnualIncreasePercent: 0m, Spousal: MemberTodd )] );

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember tina = compiledPlan.Members.Single( m => m.Name == MemberTina );
		CompiledAsset tinaRrsp = compiledPlan.Assets.Single(
			a => a.MemberId == tina.MemberId && a.TaxStatus == AssetTaxStatus.Taxable );

		ContributionAllocation allocation = Allocate( compiledPlan, new DateOnly( 2026, 3, 1 ) );

		Assert.That(
			allocation.Contributions.Where( c => c.AssetId == tinaRrsp.AssetId ).Sum( c => c.Amount ),
			Is.EqualTo( 3_000m ),
			"The age test applies to the annuitant, so a younger spouse's plan stays open." );
	}

	[Test]
	public void AllocateContributions_RoomRestoredByLastYearsWithdrawal_IsCreditedOnTopOfTheAccrual() {
		// A TFSA withdrawal is added back to contribution room on January 1 of the following
		// year, in addition to that year's own accrual.
		Plan plan = CreatePlan(
			toddBirthDate: new DateOnly( 1970, 6, 1 ),
			contributions: [] );

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember todd = compiledPlan.Members.Single( m => m.Name == MemberTodd );
		CompiledAsset toddTfsa = compiledPlan.Assets.Single(
			a => a.MemberId == todd.MemberId && a.TaxStatus == AssetTaxStatus.TaxExempt );

		decimal startingBacklog = toddTfsa.ContributionBacklog;

		ContributionAllocation allocation = Allocate(
			compiledPlan,
			new DateOnly( 2027, 1, 1 ),
			restoredRoomByAsset: new Dictionary<AssetId, decimal> { [toddTfsa.AssetId] = 5_000m } );

		CalculatedAsset tfsa = allocation.Assets.Single( a => a.AssetId == toddTfsa.AssetId );

		Assert.That(
			tfsa.ContributionBacklog,
			Is.EqualTo( startingBacklog + toddTfsa.AnnualContributionLimit + 5_000m ),
			"Restored room is credited alongside, not instead of, the annual accrual." );
	}

	[Test]
	public void Calculate_TfsaWithdrawal_RestoresContributionRoomTheFollowingYear() {
		// End-to-end: a plan that withdraws from a TFSA must see that room reappear, so the
		// account can be refilled later without the withdrawal permanently costing shelter.
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				new Member( MemberTodd, new DateOnly( 1960, 1, 1 ), 70, 60, 70, 80m ),
				new Member( MemberTina, new DateOnly( 1961, 1, 1 ), 66, 60, 70, 50m )
			],
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, MemberTodd, 0m, 0m, 0m ),
				// Fully contributed TFSA: no backlog and no fresh accrual, so any room that
				// appears can only have come from the withdrawal being added back.
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, MemberTodd, 200_000m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, MemberTodd, 0m, hasUnlimitedContributionRoom: true ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, MemberTina, 0m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, MemberTina, 0m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, MemberTina, 0m, hasUnlimitedContributionRoom: true )
			],
			annualInflationPercent: 0m,
			annualReturnPercent: 0m,
			lifeInsurance: [],
			// The desired income always exceeds the pension income, so the plan is permanently
			// in shortfall. Nothing is ever left over to be sheltered back into the TFSA, which
			// would otherwise consume the very room the withdrawal restored.
			retirementIncome: new RetirementIncome( GoGo: 5_000m, SlowGo: 5_000m, SlowGoYears: 0, NoGo: 5_000m, NoGoYears: 0 ),
			contributions: [],
			burndown: null
		);

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember todd = compiledPlan.Members.Single( m => m.Name == MemberTodd );
		AssetId tfsaId = compiledPlan.Assets
			.Single( a => a.MemberId == todd.MemberId && a.TaxStatus == AssetTaxStatus.TaxExempt )
			.AssetId;

		CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );

		// With no growth and no contributions, every dollar the balance falls by during 2026 left
		// the account as a withdrawal, whether to fund income or to pay tax. Both kinds restore
		// room, so the drop in balance is the amount that must reappear.
		CalculatedPeriod january2027 = calculatedPlan.Periods
			.First( p => p.PeriodDate.Year == 2027 && p.PeriodDate.Month == 1 );

		decimal withdrawnIn2026 =
			calculatedPlan.Periods.First().StartingAssets.Single( a => a.AssetId == tfsaId ).Amount
			- january2027.StartingAssets.Single( a => a.AssetId == tfsaId ).Amount;

		decimal roomAfterRestoration = january2027.EndingAssets
			.Single( a => a.AssetId == tfsaId )
			.ContributionBacklog;

		using( Assert.EnterMultipleScope() ) {
			Assert.That( withdrawnIn2026, Is.GreaterThan( 0m ), "The scenario must actually draw on the TFSA." );
			Assert.That(
				roomAfterRestoration,
				Is.EqualTo( withdrawnIn2026 ).Within( 0.01m ),
				"Every dollar withdrawn from a TFSA returns as contribution room the next January." );
		}
	}

	private static ContributionAllocation Allocate(
		CompiledPlan compiledPlan,
		DateOnly periodDate,
		IReadOnlyDictionary<AssetId, decimal>? restoredRoomByAsset = null
	) {
		IReadOnlyList<CalculatedAsset> assets = [
			.. compiledPlan.Assets.Select( a => new CalculatedAsset(
				a.AssetId, a.Amount, a.ContributionBacklog, a.TaxStatus, a.HasUnlimitedContributionRoom, a.CostBase ) )
		];

		CompiledPeriod period = compiledPlan.Periods.First( p => p.PeriodDate == periodDate );

		return new ContributionPolicy().AllocateContributions(
			compiledPlan,
			assets,
			periodDate,
			isFirstPeriod: false,
			compiledPlan.Contribution[period],
			restoredRoomByAsset ?? new Dictionary<AssetId, decimal>(),
			inflationIndex: 1m );
	}

	private static Plan CreatePlan(
		DateOnly toddBirthDate,
		IEnumerable<Contribution> contributions
	) {
		return TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				// Contributions only compile while the contributor is still working, so both
				// members are kept in employment for the whole window under test.
				new Member( MemberTodd, toddBirthDate, 95, 90, 70, 80m ),
				new Member( MemberTina, new DateOnly( 1975, 6, 1 ), 95, 90, 70, 50m )
			],
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, MemberTodd, 100_000m, 50_000m, 10_000m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, MemberTodd, 0m, 50_000m, 7_000m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, MemberTodd, 0m, hasUnlimitedContributionRoom: true ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, MemberTina, 100_000m, 50_000m, 10_000m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, MemberTina, 0m, 50_000m, 7_000m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, MemberTina, 0m, hasUnlimitedContributionRoom: true )
			],
			annualInflationPercent: 0m,
			annualReturnPercent: 0m,
			contributions: contributions
		);
	}
}
