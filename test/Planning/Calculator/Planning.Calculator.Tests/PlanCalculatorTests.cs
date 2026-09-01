using Planning.Calculator.Calculators;
using Planning.Compiler;
using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Identifiers;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Calculator.Tests;

[TestFixture]
public sealed class PlanCalculatorTests {
	public const string MemberTodd = "Todd";
	public const string MemberTina = "Tina";

	public const string AssetRRSP = "RRSP";
	public const string AssetTFSA = "TFSA";
	public const string AssetNonReg = "Non-Reg";


	[Test]
	public void Calculate_SpousalContributionExceedingTheContributorsRoom_FallsBackToTheAnnuitantsOwnRoom() {
		// Todd has only 1,000 of RRSP room, so a 3,000 spousal contribution into Tina's RRSP
		// draws 1,000 from Todd and the remaining 2,000 from Tina's own room.
		Plan plan = TestPlanFactory.Create(
			assets: [
				TestPlanFactory.CreateAsset( AssetRRSP, AssetTaxStatus.Taxable, MemberTodd, 100_000m, contributionBacklog: 1_000m ),
				TestPlanFactory.CreateAsset( AssetTFSA, AssetTaxStatus.TaxExempt, MemberTodd, 0m ),
				TestPlanFactory.CreateAsset( AssetRRSP, AssetTaxStatus.Taxable, MemberTina, 100_000m, contributionBacklog: 50_000m ),
				TestPlanFactory.CreateAsset( AssetTFSA, AssetTaxStatus.TaxExempt, MemberTina, 0m ),
				TestPlanFactory.CreateAsset( AssetNonReg, AssetTaxStatus.CapitalGains, MemberTodd, 0m ),
				TestPlanFactory.CreateAsset( AssetNonReg, AssetTaxStatus.CapitalGains, MemberTina, 0m ),
			],
			contributions: [
				new Contribution( MemberTina, 3000m, 2026, Indexed: false, Spousal: MemberTodd )
			]
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember todd = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledMember tina = compiledPlan.Members.First( m => m.Name == MemberTina );

		CalculatedPeriod period = new PlanCalculator().Calculate( plan, compiledPlan ).Periods.First();
		CompiledAsset toddRrsp = compiledPlan.Assets.Single( a => a.MemberId == todd.MemberId && a.TaxStatus == AssetTaxStatus.Taxable );
		CompiledAsset tinaRrsp = compiledPlan.Assets.Single( a => a.MemberId == tina.MemberId && a.TaxStatus == AssetTaxStatus.Taxable );

		using( Assert.EnterMultipleScope() ) {
			// The whole 3,000 still lands in Tina's RRSP.
			Assert.That( period.Contribution.Single( c => c.AssetId == tinaRrsp.AssetId ).Amount, Is.EqualTo( 3_000m ) );

			// Todd's room is fully consumed, and Tina's own room covers the shortfall rather than
			// the overflow spilling into a TFSA.
			Assert.That( period.EndingAssets.Single( a => a.AssetId == toddRrsp.AssetId ).ContributionBacklog, Is.Zero );
			Assert.That( period.EndingAssets.Single( a => a.AssetId == tinaRrsp.AssetId ).ContributionBacklog, Is.EqualTo( 48_000m ) );
		}
	}

	[Test]
	public void AllocateContributions_SpousalContributionFallingBackToTheAnnuitantsRoom_RecordsOnlyTheContributorFundedPortionAsSpousal() {
		// Todd has only 1,000 of RRSP room, so of a 3,000 contribution into Tina's RRSP only
		// 1,000 is genuinely spousal. The 2,000 funded from Tina's own room is her own
		// contribution and must not be attributed back to Todd on withdrawal.
		Plan plan = TestPlanFactory.Create(
			assets: [
				TestPlanFactory.CreateAsset( AssetRRSP, AssetTaxStatus.Taxable, MemberTodd, 100_000m, contributionBacklog: 1_000m ),
				TestPlanFactory.CreateAsset( AssetTFSA, AssetTaxStatus.TaxExempt, MemberTodd, 0m ),
				TestPlanFactory.CreateAsset( AssetRRSP, AssetTaxStatus.Taxable, MemberTina, 100_000m, contributionBacklog: 50_000m ),
				TestPlanFactory.CreateAsset( AssetTFSA, AssetTaxStatus.TaxExempt, MemberTina, 0m ),
				TestPlanFactory.CreateAsset( AssetNonReg, AssetTaxStatus.CapitalGains, MemberTodd, 0m, hasUnlimitedContributionRoom: true ),
				TestPlanFactory.CreateAsset( AssetNonReg, AssetTaxStatus.CapitalGains, MemberTina, 0m )
			],
			contributions: [
				new Contribution( MemberTina, 3000m, 2026, Indexed: false, Spousal: MemberTodd )
			]
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledPeriod firstPeriod = compiledPlan.Periods.First();

		IReadOnlyList<CalculatedAsset> assets = [
			.. compiledPlan.Assets.Select( a => new CalculatedAsset(
				a.AssetId, a.Amount, a.ContributionBacklog, a.TaxStatus, a.HasUnlimitedContributionRoom, a.CostBase ) )
		];

		ContributionAllocation allocation = new ContributionPolicy().AllocateContributions(
			compiledPlan, assets, firstPeriod.PeriodDate, isFirstPeriod: true, compiledPlan.Contribution[firstPeriod] );

		using( Assert.EnterMultipleScope() ) {
			// Only Todd's 1,000 is recorded as spousal, so only that much can ever be attributed.
			Assert.That( allocation.SpousalDeposits.Sum( d => d.Amount ), Is.EqualTo( 1_000m ) );
			Assert.That( allocation.SpousalDeposits.Single().ContributorMemberId, Is.EqualTo( new MemberId( 1 ) ) );

			// The full 3,000 still reaches Tina's RRSP.
			Assert.That( allocation.Contributions.Sum( c => c.Amount ), Is.EqualTo( 3_000m ) );
		}
	}

	[Test]
	public void Calculate_SpousalContribution_DepositsIntoTheAnnuitantAndConsumesTheContributorsRoom() {
		Plan plan = TestPlanFactory.Create(
			contributions: [
				new Contribution(
					Member: MemberTina,
					Amount: 3000.0m,
					StartYear: 2026,
					Indexed: false,
					Spousal: MemberTodd
				)
			]
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledAsset toddRRSPCompiled = compiledPlan.Assets.First( a => a.Name == AssetRRSP && a.MemberId == toddCompiled.MemberId );
		CompiledMember tinaCompiled = compiledPlan.Members.First( m => m.Name == MemberTina );
		CompiledAsset tinaRRSPCompiled = compiledPlan.Assets.First( a => a.Name == AssetRRSP && a.MemberId == tinaCompiled.MemberId );

		CalculatedPeriod period = new PlanCalculator().Calculate( plan, compiledPlan ).Periods[0];
		CalculatedAsset toddRRSPCalculated = period.EndingAssets.Single( a => a.AssetId == toddRRSPCompiled.AssetId );
		CalculatedAsset tinaRRSPCalculated = period.EndingAssets.Single( a => a.AssetId == tinaRRSPCompiled.AssetId );

		using( Assert.EnterMultipleScope() ) {
			// The funds land in Tina's RRSP, not Todd's, even though Todd funded them.
			Assert.That( period.Contribution.Single( c => c.AssetId == tinaRRSPCalculated.AssetId ).Amount, Is.EqualTo( 3000m ) );
			Assert.That( period.Contribution.Single( c => c.AssetId == toddRRSPCalculated.AssetId ).Amount, Is.Zero );

			// Todd's room is what gets consumed, so his backlog falls while his balance does not
			// receive the contribution.
			Assert.That( toddRRSPCalculated.ContributionBacklog, Is.LessThan(
				period.StartingAssets.Single( a => a.AssetId == toddRRSPCompiled.AssetId ).ContributionBacklog ) );
		}
	}

	[Test]
	public void Calculate_Inheritance_AddsLifecycleEventOnTheReceiptDate() {
		Plan plan = TestPlanFactory.Create(
			inheritance: [
				new Inheritance( MemberTodd, 500_000m, AgeReceived: 65 )
			]
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );
		PlanEvent inheritanceEvent = calculatedPlan.Events.Single( e => e.Name == "Todd inheritance" );

		using( Assert.EnterMultipleScope() ) {
			// Todd is born 1973-12-25, so age 65 falls on 2038-12-25.
			Assert.That( inheritanceEvent.Date, Is.EqualTo( new DateOnly( 2038, 12, 25 ) ) );
			Assert.That( inheritanceEvent.Kind, Is.EqualTo( PlanEventKind.Lifecycle ) );
			Assert.That( calculatedPlan.Events, Is.Ordered.By( nameof( PlanEvent.Date ) ) );
		}
	}

	[Test]
	public void Calculate_ZeroAmountInheritance_AddsNoInheritanceEvent() {
		Plan plan = TestPlanFactory.Create(
			inheritance: [
				new Inheritance( MemberTodd, 0m, AgeReceived: 65 )
			]
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );

		Assert.That( calculatedPlan.Events.Any( e => e.Name.Contains( "inheritance" ) ), Is.False );
	}

	[Test]
	public void Calculate_NoInheritance_AddsNoInheritanceEvent() {
		Plan plan = TestPlanFactory.Create();
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );

		Assert.That( calculatedPlan.Events.Any( e => e.Name.Contains( "inheritance" ) ), Is.False );
	}

	[Test]
	public void Calculate_FirstPeriod_AppliesReturnAndContributionToBalances() {
		Plan plan = TestPlanFactory.Create();
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledMember tinaCompiled = compiledPlan.Members.First( m => m.Name == MemberTina );
		CompiledAsset toddRRSPCompiled = compiledPlan.Assets.First( a => a.Name == AssetRRSP && a.MemberId == toddCompiled.MemberId );
		CompiledAsset tinaRRSPCompiled = compiledPlan.Assets.First( a => a.Name == AssetRRSP && a.MemberId == tinaCompiled.MemberId );

		CalculatedPeriod period = new PlanCalculator().Calculate( plan, compiledPlan ).Periods[0];
		CalculatedAsset toddRRSPCalculated = period.EndingAssets.Single( a => a.AssetId == toddRRSPCompiled.AssetId );
		CalculatedAsset tinaRRSPCalculated = period.EndingAssets.Single( a => a.AssetId == tinaRRSPCompiled.AssetId );

		using( Assert.EnterMultipleScope() ) {
			Assert.That( period.StartingAssets.Single( a => a.AssetId == toddRRSPCompiled.AssetId ).Amount, Is.EqualTo( 520_000m ) );
			Assert.That( period.Contribution.Single( c => c.AssetId == toddRRSPCompiled.AssetId ).Amount, Is.EqualTo( 3200m ) );
			Assert.That( period.Withdrawals.Sum( w => w.Amount ), Is.Zero );
			Assert.That( toddRRSPCalculated.Amount, Is.EqualTo( 525_366.66m ).Within( 0.01m ) );
			Assert.That( tinaRRSPCalculated.Amount, Is.EqualTo( 30_125m ) );
			Assert.That( period.TotalAssets, Is.EqualTo( 555_491.66m ).Within( 0.01m ) );
		}
		;
	}

	[Test]
	public void Calculate_RetirementShortfall_UsesCharacterizedWithdrawalOrder() {
		Plan plan = CreateWithdrawalPlan(
			assets: [
				TestPlanFactory.CreateAsset( "Taxable", AssetTaxStatus.Taxable, "Todd", 25m ),
				TestPlanFactory.CreateAsset( "NonTaxable", AssetTaxStatus.TaxExempt, "Todd", 25m ),
				TestPlanFactory.CreateAsset( "Taxable", AssetTaxStatus.Taxable, "Tina", 25m ),
				TestPlanFactory.CreateAsset( "NonTaxable", AssetTaxStatus.TaxExempt, "Tina", 125m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m ),
			]
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		CalculatedPeriod period = new PlanCalculator().Calculate( plan, compiledPlan ).Periods.First();
		// Accounts that were not drawn from still carry a zero entry; only the funded ones
		// characterize the order.
		CalculatedWithdrawal[] withdrawals = [.. period.Withdrawals
			.Where( w => w.Amount != 0m )
			.OrderBy( w => w.AssetId )];

		Assert.Multiple( () => {
			Assert.That( period.DesiredRetirementIncome, Is.EqualTo( 200m ) );
			Assert.That( period.RetirementIncomeShortfall, Is.EqualTo( 200m ) );
			Assert.That( withdrawals.Select( w => w.AssetId.Value ), Is.EqualTo( new[] { 1, 2, 3, 4 } ) );
			Assert.That( withdrawals.Select( w => w.Amount ), Is.EqualTo( new[] { 25m, 25m, 25m, 125m } ) );
			Assert.That( period.EndingAssets.All( a => a.Amount == 0m ), Is.True );
			Assert.That( period.TotalAssets, Is.Zero );
		} );
	}

	[Test]
	public void Calculate_InsufficientAssets_CharacterizesUnfundedShortfall() {
		Plan plan = CreateWithdrawalPlan(
			assets: [
				TestPlanFactory.CreateAsset( "Taxable", AssetTaxStatus.Taxable, "Todd", 20m ),
				TestPlanFactory.CreateAsset( "NonTaxable", AssetTaxStatus.TaxExempt, "Tina", 30m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0m ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m )
			]
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		CalculatedPeriod period = new PlanCalculator().Calculate( plan, compiledPlan ).Periods.First();

		Assert.Multiple( () => {
			Assert.That( period.RetirementIncomeShortfall, Is.EqualTo( 200m ) );
			Assert.That( period.Withdrawals.Sum( w => w.Amount ), Is.EqualTo( 50m ) );
			Assert.That( period.TotalAssets, Is.Zero );
		} );
	}

	[Test]
	public void Calculate_Income_ClassifiesTaxableAndNonTaxable() {
		Plan plan = TestPlanFactory.Create();
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		CalculatedPeriod period = new PlanCalculator().Calculate( plan, compiledPlan ).Periods[0];

		Assert.Multiple( () => {
			Assert.That(
				period.TaxableIncome.Select( i => i.Name ).Distinct(),
				Is.EquivalentTo( new[] { "CPP", "OAS", "CPP Survivor" } )
			);
			Assert.That(
				period.NonTaxableIncome.Select( i => i.Name ).Distinct(),
				Is.EquivalentTo( new[] { "Todd Life Insurance", "Tina Life Insurance" } )
			);
		} );
	}

	[Test]
	public void Calculate_Income_ComputesTotalsDesiredIncomeAndShortfall() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2025, 2, 1 ),
			members: [
				new Member( "Todd", new DateOnly( 1955, 1, 1 ), 90, 65, 65, 100m ),
				new Member( "Tina", new DateOnly( 1955, 1, 1 ), 90, 65, 65, 100m )
			],
			annualInflationPercent: 0m,
			annualReturnPercent: 0m,
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 100m ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 100m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m )
			],
			lifeInsurance: [],
			retirementIncome: new RetirementIncome(
				GoGo: 5000m,
				SlowGo: 0m,
				SlowGoYears: 0,
				NoGo: 0m,
				NoGoYears: 0
			),
			contributions: []
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		CalculatedPeriod period = new PlanCalculator().Calculate( plan, compiledPlan ).Periods.First();

		Assert.Multiple( () => {
			Assert.That( period.TotalTaxableIncome, Is.EqualTo( 4501.40m ) );
			Assert.That( period.TotalNonTaxableIncome, Is.Zero );
			Assert.That( period.TotalIncome, Is.EqualTo( 4501.40m ) );
			Assert.That( period.DesiredRetirementIncome, Is.EqualTo( 5000m ) );
			Assert.That( period.RetirementIncomeShortfall, Is.EqualTo( 498.60m ) );
		} );
	}

	[Test]
	public void Calculate_FirstPeriod_AppliesContributionsToEndingBalances() {
		Plan plan = TestPlanFactory.Create( annualReturnPercent: 0m );
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledMember tinaCompiled = compiledPlan.Members.First( m => m.Name == MemberTina );
		CompiledAsset toddRRSPCompiled = compiledPlan.Assets.First( a => a.Name == AssetRRSP && a.MemberId == toddCompiled.MemberId );
		CompiledAsset tinaRRSPCompiled = compiledPlan.Assets.First( a => a.Name == AssetRRSP && a.MemberId == tinaCompiled.MemberId );

		CalculatedPeriod period = new PlanCalculator().Calculate( plan, compiledPlan ).Periods[0];

		Assert.Multiple( () => {
			// Todd contributes 3200 from 2026; Tina starts in 2028 so contributes nothing yet.
			// Contributions follow the Taxable, TaxExempt, CapitalGains priority order, so Todd's
			// RRSP (asset 1) receives the amount while it still has room.
			// The plan return is 0%, so balances change only by the contribution.
			Assert.That( period.Contribution.Single( c => c.AssetId == toddRRSPCompiled.AssetId ).Amount, Is.EqualTo( 3200m ) );
			Assert.That( period.Contribution.Single( c => c.AssetId == tinaRRSPCompiled.AssetId ).Amount, Is.Zero );
			Assert.That( period.EndingAssets.Single( a => a.AssetId == toddRRSPCompiled.AssetId ).Amount, Is.EqualTo( 523_200m ) );
			Assert.That( period.EndingAssets.Single( a => a.AssetId == tinaRRSPCompiled.AssetId ).Amount, Is.EqualTo( 30_000m ) );
			Assert.That( period.TotalAssets, Is.EqualTo( 553_200m ) );
		} );
	}

	[Test]
	public void Calculate_UnlimitedContributionRoom_AbsorbsOverflowWithoutConsumingBacklog() {
		Plan plan = TestPlanFactory.Create(
			annualReturnPercent: 0m,
			assets: [
				// Taxable room is capped and exhausted by the first contribution, so the
				// remainder overflows into the unlimited CapitalGains account.
				TestPlanFactory.CreateAsset( AssetRRSP, AssetTaxStatus.Taxable, MemberTodd, 0m, 1_000m, 0m ),
				TestPlanFactory.CreateAsset( AssetNonReg, AssetTaxStatus.CapitalGains, MemberTodd, 0m, hasUnlimitedContributionRoom: true ),
				TestPlanFactory.CreateAsset( AssetTFSA, AssetTaxStatus.TaxExempt, MemberTodd, 0m, 0m, 0m ),
				TestPlanFactory.CreateAsset( AssetRRSP, AssetTaxStatus.Taxable, MemberTina, 0m, 0m, 0m ),
				TestPlanFactory.CreateAsset( AssetNonReg, AssetTaxStatus.CapitalGains, MemberTina, 0m, hasUnlimitedContributionRoom: true ),
				TestPlanFactory.CreateAsset( AssetTFSA, AssetTaxStatus.TaxExempt, MemberTina, 0m, 0m, 0m ),
			],
			contributions: [
				new Contribution( MemberTodd, 5_000m, 2026, Indexed: false )
			]
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledAsset toddRRSPCompiled = compiledPlan.Assets.First( a => a.Name == AssetRRSP && a.MemberId == toddCompiled.MemberId );
		CompiledAsset toddNonRegCompiled = compiledPlan.Assets.First( a => a.Name == AssetNonReg && a.MemberId == toddCompiled.MemberId );

		CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );
		CalculatedPeriod first = calculatedPlan.Periods.First();

		using( Assert.EnterMultipleScope() ) {
			Assert.That( first.Contribution.Single( c => c.AssetId == toddRRSPCompiled.AssetId ).Amount, Is.EqualTo( 1_000m ) );
			Assert.That( first.Contribution.Single( c => c.AssetId == toddNonRegCompiled.AssetId ).Amount, Is.EqualTo( 4_000m ) );
			Assert.That( first.EndingAssets.Single( a => a.AssetId == toddRRSPCompiled.AssetId ).ContributionBacklog, Is.Zero );

			// The unlimited room is never consumed and never accrues.
			Assert.That(
				calculatedPlan.Periods.All( p => {
					CalculatedAsset a = p.EndingAssets.Single( a => a.AssetId == toddNonRegCompiled.AssetId );
					return a.HasUnlimitedContributionRoom && a.ContributionBacklog == 0m;
				} ),
				Is.True
			);

			// With no room left in the capped account, later periods route everything to the
			// unlimited account.
			CalculatedPeriod second = calculatedPlan.Periods.ElementAt( 1 );
			Assert.That( second.Contribution.Single( c => c.AssetId == toddRRSPCompiled.AssetId ).Amount, Is.Zero );
			Assert.That( second.Contribution.Single( c => c.AssetId == toddNonRegCompiled.AssetId ).Amount, Is.EqualTo( 5_000m ) );
		}
	}

	[Test]
	public void Calculate_TaxableRoom_StopsAccruingAfterRetirementYear() {
		Plan plan = TestPlanFactory.Create(
			annualReturnPercent: 0m,
			assets: [
				TestPlanFactory.CreateAsset( AssetRRSP, AssetTaxStatus.Taxable, MemberTodd, 0m, 0m, 1_000m ),
				TestPlanFactory.CreateAsset( AssetTFSA, AssetTaxStatus.TaxExempt, MemberTodd, 0m, 0m, 500m ),
				TestPlanFactory.CreateAsset( AssetRRSP, AssetTaxStatus.Taxable, MemberTina, 0m ),
				TestPlanFactory.CreateAsset( AssetTFSA, AssetTaxStatus.TaxExempt, MemberTina, 0m ),
				TestPlanFactory.CreateAsset( AssetNonReg, AssetTaxStatus.CapitalGains, MemberTodd, 0m ),
				TestPlanFactory.CreateAsset( AssetNonReg, AssetTaxStatus.CapitalGains, MemberTina, 0m )
			],
			contributions: []
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledAsset toddRRSPCompiled = compiledPlan.Assets.First( a => a.Name == AssetRRSP && a.MemberId == toddCompiled.MemberId );
		CompiledAsset toddTFSACompiled = compiledPlan.Assets.First( a => a.Name == AssetTFSA && a.MemberId == toddCompiled.MemberId );

		CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );

		decimal TaxableBacklogAt( int year ) => calculatedPlan.Periods
			.First( p => p.PeriodDate.Year == year && p.PeriodDate.Month == 1 )
			.EndingAssets.Single( a => a.AssetId == toddRRSPCompiled.AssetId ).ContributionBacklog;

		decimal TaxExemptBacklogAt( int year ) => calculatedPlan.Periods
			.First( p => p.PeriodDate.Year == year && p.PeriodDate.Month == 1 )
			.EndingAssets.Single( a => a.AssetId == toddTFSACompiled.AssetId ).ContributionBacklog;

		using( Assert.EnterMultipleScope() ) {
			// Todd retires 2034-01-01, so room accrues each January through the retirement year.
			Assert.That( TaxableBacklogAt( 2033 ), Is.EqualTo( 7_000m ) );
			Assert.That( TaxableBacklogAt( 2034 ), Is.EqualTo( 8_000m ) );

			// Every January after the retirement year adds nothing.
			Assert.That( TaxableBacklogAt( 2035 ), Is.EqualTo( 8_000m ) );
			Assert.That( TaxableBacklogAt( 2040 ), Is.EqualTo( 8_000m ) );

			// Non-taxable accounts keep accruing after retirement.
			Assert.That( TaxExemptBacklogAt( 2035 ), Is.EqualTo( TaxExemptBacklogAt( 2034 ) + 500m ) );
		}
	}

	[Test]
	public void Calculate_ExhaustedAssets_NeverProducesNegativeBalances() {
		Plan plan = CreateWithdrawalPlan(
			assets: [
				TestPlanFactory.CreateAsset( "Taxable", AssetTaxStatus.Taxable, "Todd", 20m ),
				TestPlanFactory.CreateAsset( "NonTaxable", AssetTaxStatus.TaxExempt, "Tina", 30m ),
				TestPlanFactory.CreateAsset( "Taxable", AssetTaxStatus.Taxable, "Tina", 0m ),
				TestPlanFactory.CreateAsset( "NonTaxable", AssetTaxStatus.TaxExempt, "Todd", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m )
			]
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );

		Assert.That(
			calculatedPlan.Periods.SelectMany( p => p.EndingAssets ).All( a => a.Amount >= 0m ),
			Is.True
		);
	}

	[Test]
	public void Calculate_MultipleWithdrawalsFromOneAsset_AreCombined() {
		Plan plan = CreateWithdrawalPlan(
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 1000m ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m )
			]
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledAsset toddRRSPCompiled = compiledPlan.Assets.First( a => a.Name == AssetRRSP && a.MemberId == toddCompiled.MemberId );

		CalculatedPeriod period = new PlanCalculator().Calculate( plan, compiledPlan ).Periods.First();

		using( Assert.EnterMultipleScope() ) {
			// Todd draws his 100 share from his own asset; Tina draws her 100 share from the
			// same asset as another member's taxable account. The two are repacked into one.
			Assert.That( period.Withdrawals.Count( w => w.Amount != 0m ), Is.EqualTo( 1 ) );
			Assert.That( period.Withdrawals.Single( w => w.AssetId == toddRRSPCompiled.AssetId ).Amount, Is.EqualTo( 200m ) );
		}
	}

	[Test]
	public void Calculate_SingleAssetCoversShortfall_WithdrawsFullShortfall() {
		Plan plan = CreateWithdrawalPlan(
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 1000m ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m )
			]
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledAsset toddRRSPCompiled = compiledPlan.Assets.First( a => a.Name == AssetRRSP && a.MemberId == toddCompiled.MemberId );

		CalculatedPeriod period = new PlanCalculator().Calculate( plan, compiledPlan ).Periods.First();

		using( Assert.EnterMultipleScope() ) {
			// The plan return is 0%, so the remaining balance does not grow.
			Assert.That( period.Withdrawals.Sum( w => w.Amount ), Is.EqualTo( 200m ) );
			Assert.That( period.EndingAssets.Single( a => a.AssetId == toddRRSPCompiled.AssetId ).Amount, Is.EqualTo( 800m ) );
			Assert.That( period.TotalAssets, Is.EqualTo( 800m ) );
		}
	}

	[Test]
	public void Calculate_SingleMember_WithdrawsFromThatMembersAsset() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				new Member( "Todd", new DateOnly( 1970, 1, 1 ), 60, 50, 70, 80m ),
				new Member( "Tina", new DateOnly( 1970, 1, 1 ), 60, 50, 70, 80m )
			],
			annualInflationPercent: 0m,
			annualReturnPercent: 0m,
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 1000m ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m ),
			],
			lifeInsurance: [],
			retirementIncome: new RetirementIncome(
				GoGo: 200m,
				SlowGo: 200m,
				SlowGoYears: 0,
				NoGo: 200m,
				NoGoYears: 0
			),
			contributions: []
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledMember tinaCompiled = compiledPlan.Members.First( m => m.Name == MemberTina );
		CompiledAsset toddRRSPCompiled = compiledPlan.Assets.First( a => a.Name == AssetRRSP && a.MemberId == toddCompiled.MemberId );
		CompiledAsset tinaRRSPCompiled = compiledPlan.Assets.First( a => a.Name == AssetRRSP && a.MemberId == tinaCompiled.MemberId );

		CalculatedPeriod period = new PlanCalculator().Calculate( plan, compiledPlan ).Periods.First();

		using( Assert.EnterMultipleScope() ) {
			// A single member draws the entire shortfall (no split across members).
			// The plan return is 0%, so the remaining balance does not grow.
			Assert.That( period.Withdrawals.Single( w => w.AssetId == toddRRSPCompiled.AssetId ).Amount, Is.EqualTo( 200m ) );
			Assert.That( period.EndingAssets.Single( a => a.AssetId == toddRRSPCompiled.AssetId ).Amount, Is.EqualTo( 800m ) );
		}
	}

	[Test]
	public void Calculate_MoreThanTwoMembers_ThrowsBecauseSurvivorAssumesTwo() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				new Member( "Todd", new DateOnly( 1960, 1, 1 ), 70, 60, 65, 80m ),
				new Member( "Tina", new DateOnly( 1960, 1, 1 ), 90, 60, 65, 50m ),
				new Member( "Theo", new DateOnly( 1960, 1, 1 ), 90, 60, 65, 50m )
			],
			annualInflationPercent: 0m,
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 100m ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 100m ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Theo", 100m )
			],
			lifeInsurance: [],
			contributions: []
		);

		// More than two members now fails plan validation (household must be exactly
		// two per decision D001) before calculation begins.
		Assert.That(
			() => new PlanCalculator().Calculate( plan, new PlanCompiler().Compile( plan ) ),
			Throws.TypeOf<PlanValidationException>()
		);
	}

	[Test]
	public void Calculate_AfterMemberDeath_ContinuesWithSurvivorIncome() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2025, 1, 1 ),
			members: [
				new Member( "Todd", new DateOnly( 1955, 1, 1 ), 70, 65, 65, 100m ),
				new Member( "Tina", new DateOnly( 1955, 1, 1 ), 90, 65, 65, 100m )
			],
			annualInflationPercent: 0m,
			annualReturnPercent: 0m,
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 100m ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 100m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m )
			],
			lifeInsurance: [],
			retirementIncome: new RetirementIncome(
				GoGo: 0m,
				SlowGo: 0m,
				SlowGoYears: 0,
				NoGo: 0m,
				NoGoYears: 0
			),
			contributions: []
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledMember tinaCompiled = compiledPlan.Members.First( m => m.Name == MemberTina );

		CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );
		// Todd (target age 70) dies end of January 2025; February is the first widowed month.
		CalculatedPeriod afterDeath = calculatedPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2025, 2, 1 ) );

		using( Assert.EnterMultipleScope() ) {
			Assert.That( afterDeath.TaxableIncome.Single( i => i.MemberId == toddCompiled.MemberId && i.Name == "CPP" ).Amount, Is.Zero );
			Assert.That( afterDeath.TaxableIncome.Single( i => i.MemberId == tinaCompiled.MemberId && i.Name == "CPP Survivor" ).Amount, Is.GreaterThan( 0m ) );
		}
	}

	[Test]
	public void Calculate_LifeInsurancePayout_BumpsTotalAssets() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2025, 1, 1 ),
			members: [
				new Member( MemberTodd, new DateOnly( 1955, 1, 1 ), 70, 65, 65, 100m ),
				new Member( MemberTina, new DateOnly( 1955, 1, 1 ), 90, 65, 65, 100m )
			],
			annualInflationPercent: 0m,
			annualReturnPercent: 0m,
			assets: [
				TestPlanFactory.CreateAsset( AssetRRSP, AssetTaxStatus.Taxable, MemberTodd, 100m ),
				// The TFSA needs contribution room for the payout to be sheltered there; without
				// it the deposit would legitimately have to spill elsewhere.
				TestPlanFactory.CreateAsset( AssetTFSA, AssetTaxStatus.TaxExempt, MemberTina, 100m, 250_000m ),

				TestPlanFactory.CreateAsset( AssetTFSA, AssetTaxStatus.TaxExempt, MemberTodd, 0m ),
				TestPlanFactory.CreateAsset( AssetRRSP, AssetTaxStatus.Taxable, MemberTina, 0m ),
				TestPlanFactory.CreateAsset( AssetNonReg, AssetTaxStatus.CapitalGains, MemberTodd, 0m ),
				TestPlanFactory.CreateAsset( AssetNonReg, AssetTaxStatus.CapitalGains, MemberTina, 0m )
			],
			lifeInsurance: [
				new LifeInsurance( MemberTodd, 250_000m )
			],
			retirementIncome: new RetirementIncome(
				GoGo: 0m,
				SlowGo: 0m,
				SlowGoYears: 0,
				NoGo: 0m,
				NoGoYears: 0
			),
			contributions: []
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );
		// Todd (target age 70) dies end of January 2025, so the payout lands that month.
		CalculatedPeriod deathMonth = calculatedPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2025, 1, 1 ) );

		MemberId tinaId = compiledPlan.Members.Single( m => m.Name == MemberTina ).MemberId;
		AssetId tinaTfsaId = compiledPlan.Assets
			.Single( a => a.MemberId == tinaId && a.TaxStatus == AssetTaxStatus.TaxExempt )
			.AssetId;

		CalculatedPeriod afterDeath = calculatedPlan.Periods.First( p => p.PeriodDate > new DateOnly( 2025, 1, 1 ) );
		CalculatedAsset survivorTfsa = afterDeath.EndingAssets.Single( a => a.AssetId == tinaTfsaId );

		using( Assert.EnterMultipleScope() ) {
			// The $250,000 payout is retained in a non-taxable (TFSA) account and, once the
			// rollover has run, sits with the surviving member.
			Assert.That( deathMonth.TotalAssets, Is.GreaterThan( 250_000m ) );
			Assert.That( survivorTfsa.Amount, Is.GreaterThanOrEqualTo( 250_000m ) );
		}
	}

	[Test]
	public void Calculate_MemberDies_ExtinguishesTheirUnusedContributionRoomButNotTheSurvivorsRoom() {
		// Unused contribution room is personal: it dies with the member and is never inherited,
		// while a successor-holder rollover leaves the survivor's own room untouched.
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2024, 1, 1 ),
			members: [
				new Member( "Todd", new DateOnly( 1955, 1, 1 ), 70, 65, 65, 100m ),
				new Member( "Tina", new DateOnly( 1955, 1, 1 ), 90, 65, 65, 100m )
			],
			annualInflationPercent: 0m,
			annualReturnPercent: 0m,
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 100m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 100m, 50_000m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 100m, 40_000m ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m ),

			],
			lifeInsurance: [],
			retirementIncome: new RetirementIncome(
				GoGo: 0m,
				SlowGo: 0m,
				SlowGoYears: 0,
				NoGo: 0m,
				NoGoYears: 0
			),
			contributions: []
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );

		MemberId toddId = compiledPlan.Members.Single( m => m.Name == "Todd" ).MemberId;
		MemberId tinaId = compiledPlan.Members.Single( m => m.Name == "Tina" ).MemberId;
		AssetId toddTfsaId = compiledPlan.Assets
			.Single( a => a.MemberId == toddId && a.TaxStatus == AssetTaxStatus.TaxExempt )
			.AssetId;
		AssetId tinaTfsaId = compiledPlan.Assets
			.Single( a => a.MemberId == tinaId && a.TaxStatus == AssetTaxStatus.TaxExempt )
			.AssetId;

		CalculatedPeriod afterDeath = calculatedPlan.Periods.First( p => p.PeriodDate > new DateOnly( 2025, 1, 1 ) );

		// The survivor's room is consumed normally by surplus deposits over time, so the test is
		// that it never *increases* — inheriting the account must not hand her Todd's room.
		decimal maxSurvivorRoom = calculatedPlan.Periods
			.Select( p => p.EndingAssets.Single( a => a.AssetId == tinaTfsaId ).ContributionBacklog )
			.Max();

		using( Assert.EnterMultipleScope() ) {
			Assert.That(
				afterDeath.EndingAssets.Single( a => a.AssetId == toddTfsaId ).ContributionBacklog,
				Is.Zero,
				"The deceased member's unused room must be extinguished." );
			Assert.That(
				maxSurvivorRoom,
				Is.LessThanOrEqualTo( 40_000m ),
				"The survivor must never inherit the deceased member's contribution room." );
		}
	}

	[Test]
	public void Calculate_SolventPlan_ReportsNoInsufficientFunds() {
		Plan plan = TestPlanFactory.Create(
			retirementIncome: new RetirementIncome(
				GoGo: 0m,
				SlowGo: 0m,
				SlowGoYears: 0,
				NoGo: 0m,
				NoGoYears: 0
			)
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );
		InsufficientFundsSummary summary = calculatedPlan.InsufficientFunds;

		using( Assert.EnterMultipleScope() ) {
			Assert.That( summary.HasShortfall, Is.False );
			Assert.That( summary.FirstShortfallDate, Is.Null );
			Assert.That( summary.FirstShortfallPeriod, Is.Null );
			Assert.That( summary.ShortfallPeriodCount, Is.Zero );
			Assert.That( summary.TotalUnfundedShortfall, Is.Zero );
		}
	}

	[Test]
	public void Calculate_TaxSettlesInDecemberOnly() {
		Plan plan = CreateTaxPlan();
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );

		IReadOnlyList<CalculatedPeriod> firstYear = [.. calculatedPlan.Periods.Where( p => p.PeriodDate.Year == 2026 )];

		using( Assert.EnterMultipleScope() ) {
			foreach( CalculatedPeriod period in firstYear.Where( p => p.PeriodDate.Month != 12 ) ) {
				Assert.That( period.Taxes, Is.Empty, $"{period.PeriodDate:yyyy-MM} should have no tax" );
				Assert.That( period.TotalTax, Is.Zero );
			}

			CalculatedPeriod december = firstYear.Single( p => p.PeriodDate.Month == 12 );
			Assert.That( december.Taxes, Is.Not.Empty );
			Assert.That( december.TotalTax, Is.GreaterThan( 0m ) );
		}
	}

	[Test]
	public void Calculate_TaxUsesTaxableWithdrawalsAndProgressiveRates() {
		Plan plan = CreateTaxPlan();
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledAsset toddRRSPCompiled = compiledPlan.Assets.First( a => a.Name == AssetRRSP && a.MemberId == toddCompiled.MemberId );

		CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );

		IReadOnlyList<CalculatedPeriod> firstYear = [.. calculatedPlan.Periods.Where( p => p.PeriodDate.Year == 2026 )];
		CalculatedPeriod december = firstYear.Single( p => p.PeriodDate.Month == 12 );

		// Each member's taxable base for the year equals the sum of their taxable-account
		// withdrawals (there is no other taxable income in this plan).
		decimal toddWithdrawals = firstYear
			.SelectMany( p => p.Withdrawals )
			.Where( w => w.AssetId == toddRRSPCompiled.AssetId )
			.Sum( w => w.Amount );

		CalculatedTax toddTax = december.Taxes.Single( t => t.MemberId == toddCompiled.MemberId );

		using( Assert.EnterMultipleScope() ) {
			// The pipeline wires the year's taxable-account withdrawals into the member's
			// taxable base; the progressive-rate arithmetic itself is covered directly by
			// TaxCalculatorTests.
			Assert.That( toddTax.TaxableAmount, Is.EqualTo( toddWithdrawals ) );
			Assert.That( toddTax.FederalTax, Is.GreaterThan( 0m ) );
			Assert.That( toddTax.ProvincialTax, Is.GreaterThan( 0m ) );
			Assert.That( toddTax.TotalTax, Is.EqualTo( toddTax.FederalTax + toddTax.ProvincialTax ) );
		}
	}

	[Test]
	public void Calculate_CapitalGainsAsset_TaxesFiftyPercentInclusion() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				new Member( MemberTodd, new DateOnly( 1970, 1, 1 ), 60, 50, 70, 80m ),
				new Member( MemberTina, new DateOnly( 1971, 1, 1 ), 60, 50, 70, 50m )
			],
			assets: [
				TestPlanFactory.CreateAsset( AssetNonReg, AssetTaxStatus.CapitalGains, MemberTodd, 100_000m ),
				TestPlanFactory.CreateAsset( AssetNonReg, AssetTaxStatus.CapitalGains, MemberTina, 100_000m ),
				TestPlanFactory.CreateAsset( AssetRRSP, AssetTaxStatus.Taxable, MemberTodd, 0m ),
				TestPlanFactory.CreateAsset( AssetRRSP, AssetTaxStatus.Taxable, MemberTina, 0m ),
				TestPlanFactory.CreateAsset( AssetTFSA, AssetTaxStatus.TaxExempt, MemberTodd, 0m ),
				TestPlanFactory.CreateAsset( AssetTFSA, AssetTaxStatus.TaxExempt, MemberTina, 0m )
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
			contributions: []
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledAsset toddNonRegCompiled = compiledPlan.Assets.First( a => a.Name == AssetNonReg && a.MemberId == toddCompiled.MemberId );

		CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );

		IReadOnlyList<CalculatedPeriod> firstYear = [.. calculatedPlan.Periods.Where( p => p.PeriodDate.Year == 2026 )];
		CalculatedPeriod december = firstYear.Single( p => p.PeriodDate.Month == 12 );

		decimal toddWithdrawals = firstYear
			.SelectMany( p => p.Withdrawals )
			.Where( w => w.AssetId == toddNonRegCompiled.AssetId )
			.Sum( w => w.Amount );

		CalculatedTax toddTax = december.Taxes.Single( t => t.MemberId == toddCompiled.MemberId );

		// These accounts have no cost base, so their entire balance is accrued gain and 50% of
		// every dollar withdrawn is included in the taxable base. The tolerance absorbs the
		// residue from expressing the withdrawal as a proportion of the balance.
		Assert.That( toddTax.TaxableAmount, Is.EqualTo( toddWithdrawals * 0.5m ).Within( 0.01m ) );
	}

	[Test]
	public void Calculate_PensionSplitting_ReducesCombinedTax() {
		static Plan CreateSplittingPlan( bool allowSplitting ) {
			TaxPolicy basePolicy = TestPlanFactory.CreateTaxPolicy();
			return TestPlanFactory.Create(
				startDate: new DateOnly( 2026, 1, 1 ),
				members: [
					new Member( "Todd", new DateOnly( 1955, 1, 1 ), 90, 60, 70, 80m ),
					new Member( "Tina", new DateOnly( 1956, 1, 1 ), 90, 60, 70, 50m )
				],
				assets: [
					TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 2_000_000m ),
					TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0m ),
					TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 0m ),
					TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0m ),
					TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m ),
					TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m )
				],
				annualInflationPercent: 0m,
				annualReturnPercent: 0m,
				lifeInsurance: [],
				retirementIncome: new RetirementIncome(
					GoGo: 8000m,
					SlowGo: 8000m,
					SlowGoYears: 0,
					NoGo: 8000m,
					NoGoYears: 0
				),
				contributions: [],
				taxPolicy: basePolicy with { AllowPensionSplitting = allowSplitting }
			);
		}

		Plan withoutSplitting = CreateSplittingPlan( allowSplitting: false );
		Plan withSplitting = CreateSplittingPlan( allowSplitting: true );

		CalculatedPlan withoutResult = new PlanCalculator().Calculate( withoutSplitting, new PlanCompiler().Compile( withoutSplitting ) );
		CalculatedPlan withResult = new PlanCalculator().Calculate( withSplitting, new PlanCompiler().Compile( withSplitting ) );

		using( Assert.EnterMultipleScope() ) {
			// Shifting RRSP income to the lower-income spouse lowers the combined progressive tax.
			Assert.That( withResult.TaxSummary.TotalTax, Is.LessThan( withoutResult.TaxSummary.TotalTax ) );
			Assert.That( withResult.TaxSummary.TotalTax, Is.GreaterThan( 0m ) );
		}
	}

	[Test]
	public void Calculate_TaxSummary_AggregatesAllYears() {
		Plan plan = CreateTaxPlan();
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );

		decimal expectedFederal = calculatedPlan.Periods.SelectMany( p => p.Taxes ).Sum( t => t.FederalTax );
		decimal expectedProvincial = calculatedPlan.Periods.SelectMany( p => p.Taxes ).Sum( t => t.ProvincialTax );

		using( Assert.EnterMultipleScope() ) {
			Assert.That( calculatedPlan.TaxSummary.TotalFederalTax, Is.EqualTo( expectedFederal ) );
			Assert.That( calculatedPlan.TaxSummary.TotalProvincialTax, Is.EqualTo( expectedProvincial ) );
			Assert.That( calculatedPlan.TaxSummary.TotalTax, Is.EqualTo( expectedFederal + expectedProvincial ) );
			Assert.That( calculatedPlan.TaxSummary.TotalTax, Is.GreaterThan( 0m ) );
		}
	}

	[Test]
	public void Calculate_TaxIsDeductedFromEndingAssets() {
		Plan plan = CreateTaxPlan();
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );

		IReadOnlyList<CalculatedPeriod> firstYear = [.. calculatedPlan.Periods.Where( p => p.PeriodDate.Year == 2026 )];
		CalculatedPeriod november = firstYear.Single( p => p.PeriodDate.Month == 11 );
		CalculatedPeriod december = firstYear.Single( p => p.PeriodDate.Month == 12 );

		// With no return or inflation, the only reason December total assets fall faster than
		// the flat monthly withdrawal is the tax deduction settled in December.
		decimal novemberToDecemberDrop = november.TotalAssets - december.TotalAssets;

		Assert.That(
			novemberToDecemberDrop,
			Is.GreaterThan( december.TotalTax ),
			"December assets should reflect both the monthly withdrawal and the tax deduction" );
	}

	[Test]
	public void Calculate_TaxFundingWithdrawal_MatchesFundedTax() {
		Plan plan = CreateTaxPlan();
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );

		CalculatedPeriod december = calculatedPlan.Periods.Single( p => p.PeriodDate.Year == 2026 && p.PeriodDate.Month == 12 );

		using( Assert.EnterMultipleScope() ) {
			Assert.That( december.TotalTax, Is.GreaterThan( 0m ) );
			// Assets fully cover the tax bill, so the funding withdrawal equals the tax and nothing is unfunded.
			Assert.That( december.TaxFundingWithdrawal, Is.EqualTo( december.TotalTax ) );
			Assert.That( december.UnfundedTax, Is.Zero );
		}
	}

	[Test]
	public void Calculate_TaxFundedFromTaxableAccount_DefersTaxableIncomeToNextYear() {
		Plan plan = CreateTaxPlan();
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );

		CalculatedPeriod firstDecember = calculatedPlan.Periods.Single( p => p.PeriodDate.Year == 2026 && p.PeriodDate.Month == 12 );
		CalculatedPeriod secondDecember = calculatedPlan.Periods.Single( p => p.PeriodDate.Year == 2027 && p.PeriodDate.Month == 12 );

		// The first year's taxable-account funding withdrawal is carried into the second year's
		// taxable base, so the taxed amount in the second year exceeds the plain withdrawals.
		decimal secondYearTaxable = secondDecember.Taxes.Sum( t => t.TaxableAmount );
		decimal secondYearWithdrawals = calculatedPlan.Periods
			.Where( p => p.PeriodDate.Year == 2027 )
			.SelectMany( p => p.Withdrawals )
			.Sum( w => w.Amount );

		using( Assert.EnterMultipleScope() ) {
			Assert.That( firstDecember.TaxFundingWithdrawal, Is.GreaterThan( 0m ) );
			Assert.That( secondYearTaxable, Is.GreaterThan( secondYearWithdrawals ) );
		}
	}

	private static Plan CreateTaxPlan() {
		// Two retired members, each with their own taxable RRSP and no government income yet,
		// running a full calendar year so December settlement can be observed. Zero inflation
		// and zero return keep withdrawals flat and the taxable base inside the lowest brackets.
		return TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				new Member( MemberTodd, new DateOnly( 1970, 1, 1 ), 60, 50, 70, 80m ),
				new Member( MemberTina, new DateOnly( 1971, 1, 1 ), 60, 50, 70, 50m )
			],
			assets: [
				TestPlanFactory.CreateAsset( AssetRRSP, AssetTaxStatus.Taxable, MemberTodd, 100_000m ),
				TestPlanFactory.CreateAsset( AssetRRSP, AssetTaxStatus.Taxable, MemberTina, 100_000m ),
				TestPlanFactory.CreateAsset( AssetTFSA, AssetTaxStatus.TaxExempt, MemberTodd, 0m ),
				TestPlanFactory.CreateAsset( AssetTFSA, AssetTaxStatus.TaxExempt, MemberTina, 0m ),
				TestPlanFactory.CreateAsset( AssetNonReg, AssetTaxStatus.CapitalGains, MemberTodd, 0m ),
				TestPlanFactory.CreateAsset( AssetNonReg, AssetTaxStatus.CapitalGains, MemberTina, 0m )
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
			contributions: []
		);
	}

	private static Plan CreateWithdrawalPlan(
		IEnumerable<Asset> assets
	) {
		return TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				new Member( MemberTodd, new DateOnly( 1970, 1, 1 ), 60, 50, 70, 80m ),
				new Member( MemberTina, new DateOnly( 1971, 1, 1 ), 60, 50, 70, 50m )
			],
			assets: assets,
			annualInflationPercent: 0m,
			annualReturnPercent: 0m,
			lifeInsurance: [],
			retirementIncome: new RetirementIncome(
				GoGo: 200m,
				SlowGo: 200m,
				SlowGoYears: 0,
				NoGo: 200m,
				NoGoYears: 0
			),
			contributions: []
		);
	}
}
