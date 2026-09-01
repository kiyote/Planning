using Planning.Model.CompiledPlans;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Compiler.Tests;

[TestFixture]
public sealed class PlanCompilerTests {

	public const string MemberTodd = "Todd";
	public const string MemberTina = "Tina";
	public const string AssetRRSP = "RRSP";
	public const string AssetTFSA = "TFSA";


	[Test]
	public void Compile_ValidPlan_DoesNotThrow() {
		Plan plan = TestPlanFactory.Create();
		PlanCompiler compiler = new PlanCompiler();

		Assert.That(
			() => compiler.Compile( plan ),
			Throws.Nothing
		);
	}

	[Test]
	public void Compile_DefaultPlan_CompilesExpectedMembersAndPeriods() {
		CompiledPlan compiledPlan = new PlanCompiler().Compile( TestPlanFactory.Create() );
		CompiledMember[] members = [.. compiledPlan.Members];
		CompiledPeriod[] periods = [.. compiledPlan.Periods];

		Assert.Multiple( () => {
			Assert.That( members, Has.Length.EqualTo( 2 ) );
			Assert.That( members[0], Is.EqualTo( new CompiledMember(
				MemberId: new( 1 ),
				Name: MemberTodd,
				BirthDate: new DateOnly( 1973, 12, 25 ),
				DeathDate: new DateOnly( 2058, 12, 31 ),
				RetirementDate: new DateOnly( 2034, 1, 1 ),
				CPPStartDate: new DateOnly( 2044, 1, 1 ),
				OASStartDate: new DateOnly( 2039, 1, 1 ),
				// Todd defers CPP to 70, so his 80% age-65 entitlement is uplifted by 42%.
				CPPPercent: 113.6m
			) ) );
			Assert.That( members[1].MemberId.Value, Is.EqualTo( 2 ) );
			Assert.That( members[1].DeathDate, Is.EqualTo( new DateOnly( 2072, 6, 30 ) ) );
			// Per decision D002 each member retires at their own age: Tina retires at 57 (2034-07-01),
			// distinct from Todd's retirement at 60 (2034-01-01).
			Assert.That( members[1].RetirementDate, Is.EqualTo( new DateOnly( 2034, 7, 1 ) ) );
			Assert.That( members[1].CPPStartDate, Is.EqualTo( new DateOnly( 2047, 7, 1 ) ) );
			Assert.That( members[1].OASStartDate, Is.EqualTo( new DateOnly( 2042, 7, 1 ) ) );
			Assert.That( periods, Has.Length.EqualTo( 552 ) );
			Assert.That( periods[0], Is.EqualTo( new CompiledPeriod( new( 1 ), new DateOnly( 2026, 7, 1 ) ) ) );
			Assert.That( periods[^1], Is.EqualTo( new CompiledPeriod( new( 552 ), new DateOnly( 2072, 6, 1 ) ) ) );
		} );
	}

	[Test]
	public void Compile_BenefitStartMonth_CompilesExpectedIncome() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2044, 1, 1 ),
			lifeInsurance: [],
			taxPolicy: TestPlanFactory.CreateTaxPolicy() with { Year = 2044 }
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledMember tinaCompiled = compiledPlan.Members.First( m => m.Name == MemberTina );
		CompiledPeriod firstPeriod = compiledPlan.Periods.First();
		CompiledIncome[] income = [.. compiledPlan.ScheduledIncome[firstPeriod]];

		Assert.Multiple( () => {
			Assert.That( income.Single( i => i.MemberId == toddCompiled.MemberId && i.Name == "CPP" ).Amount, Is.EqualTo( 1712.6904m ) );
			Assert.That( income.Single( i => i.MemberId == toddCompiled.MemberId && i.Name == "OAS" ).Amount, Is.EqualTo( 743.05m ) );
			Assert.That( income.Single( i => i.MemberId == toddCompiled.MemberId && i.Name == "CPP Survivor" ).Amount, Is.Zero );
			Assert.That( income.Single( i => i.MemberId == tinaCompiled.MemberId && i.Name == "CPP" ).Amount, Is.Zero );
			Assert.That( income.Single( i => i.MemberId == tinaCompiled.MemberId && i.Name == "OAS" ).Amount, Is.EqualTo( 743.05m ) );
		} );
	}

	[Test]
	public void Compile_MonthAfterPartnerDeath_CompilesExpectedSurvivorIncome() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2058, 12, 1 ),
			lifeInsurance: [],
			taxPolicy: TestPlanFactory.CreateTaxPolicy() with { Year = 2058 }
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledMember tinaCompiled = compiledPlan.Members.First( m => m.Name == MemberTina );
		CompiledPeriod january = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2059, 1, 1 ) );
		CompiledIncome survivorIncome = compiledPlan.ScheduledIncome[january]
			.Single( i => i.MemberId == tinaCompiled.MemberId && i.Name == "CPP Survivor" );

		Assert.That( survivorIncome.Amount, Is.EqualTo( 473.117841m ) );
	}

	[Test]
	public void Compile_Contributions_CharacterizesStartInflationAndRetirementStop() {
		CompiledPlan compiledPlan = new PlanCompiler().Compile( TestPlanFactory.Create() );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledMember tinaCompiled = compiledPlan.Members.First( m => m.Name == MemberTina );
		CompiledPeriod firstPeriod = compiledPlan.Periods.First();
		CompiledPeriod january2027 = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2027, 1, 1 ) );
		CompiledPeriod toddRetirement = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2034, 1, 1 ) );

		Assert.Multiple( () => {
			Assert.That( compiledPlan.Contribution[firstPeriod].Single( c => c.MemberId == toddCompiled.MemberId ).Amount, Is.EqualTo( 3200m ) );
			Assert.That( compiledPlan.Contribution[firstPeriod].Single( c => c.MemberId == tinaCompiled.MemberId ).Amount, Is.Zero );
			Assert.That( compiledPlan.Contribution[january2027].Single( c => c.MemberId == toddCompiled.MemberId ).Amount, Is.EqualTo( 3283.2m ) );
			Assert.That( compiledPlan.Contribution[toddRetirement].Single( c => c.MemberId == toddCompiled.MemberId ).Amount, Is.Zero );
		} );
	}

	[Test]
	public void Compile_Contributions_StopInTheRetirementMonthOfTheirOwnMember() {
		CompiledPlan compiledPlan = new PlanCompiler().Compile( TestPlanFactory.Create() );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledMember tinaCompiled = compiledPlan.Members.First( m => m.Name == MemberTina );

		// Todd retires 2034-01-01 and Tina 2034-07-01, so each member's contributions run
		// through their own final working month rather than stopping on a shared year boundary.
		CompiledPeriod december2033 = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2033, 12, 1 ) );
		CompiledPeriod january2034 = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2034, 1, 1 ) );
		CompiledPeriod june2034 = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2034, 6, 1 ) );
		CompiledPeriod july2034 = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2034, 7, 1 ) );

		Assert.Multiple( () => {
			Assert.That( compiledPlan.Contribution[december2033].Single( c => c.MemberId == toddCompiled.MemberId ).Amount, Is.Positive );
			Assert.That( compiledPlan.Contribution[january2034].Single( c => c.MemberId == toddCompiled.MemberId ).Amount, Is.Zero );

			Assert.That( compiledPlan.Contribution[january2034].Single( c => c.MemberId == tinaCompiled.MemberId ).Amount, Is.Positive );
			Assert.That( compiledPlan.Contribution[june2034].Single( c => c.MemberId == tinaCompiled.MemberId ).Amount, Is.Positive );
			Assert.That( compiledPlan.Contribution[july2034].Single( c => c.MemberId == tinaCompiled.MemberId ).Amount, Is.Zero );
		} );
	}

	[Test]
	public void Compile_SpousalContribution_FundedByContributorAndDestinedForAnnuitant() {
		Plan plan = TestPlanFactory.Create(
			contributions: [
				new Contribution(
					Member: "Tina",
					Amount: 3000.0m,
					StartYear: 2026,
					Indexed: false,
					Spousal: "Todd"
				)
			]
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledMember tinaCompiled = compiledPlan.Members.First( m => m.Name == MemberTina );
		CompiledContribution contribution = compiledPlan.Contribution[compiledPlan.Periods.First()].Single();

		Assert.Multiple( () => {
			// Todd funds it, so it consumes his room, while Tina's account receives it.
			Assert.That( contribution.MemberId == toddCompiled.MemberId, Is.True );
			Assert.That( contribution.DestinationMemberId == tinaCompiled.MemberId, Is.True );
			Assert.That( contribution.IsSpousal, Is.True );
			Assert.That( contribution.Amount, Is.EqualTo( 3000m ) );
		} );
	}

	[Test]
	public void Compile_SpousalContribution_StopsInTheContributorsRetirementMonth() {
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

		// Todd retires 2034-01-01 and Tina 2034-07-01. The contribution is funded from Todd's
		// employment income, so it stops when he retires rather than when Tina does.
		CompiledPeriod december2033 = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2033, 12, 1 ) );
		CompiledPeriod january2034 = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2034, 1, 1 ) );

		Assert.Multiple( () => {
			Assert.That( compiledPlan.Contribution[december2033].Single().Amount, Is.Positive );
			Assert.That( compiledPlan.Contribution[january2034].Single().Amount, Is.Zero );
		} );
	}

	[Test]
	public void Compile_ContributionNamingItselfAsSpousal_IsNotTreatedAsSpousal() {
		Plan plan = TestPlanFactory.Create(
			contributions: [
				new Contribution(
					Member: MemberTodd,
					Amount: 3000.0m,
					StartYear: 2026,
					Indexed: false,
					Spousal: MemberTodd
				)
			]
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledContribution contribution = compiledPlan.Contribution[compiledPlan.Periods.First()].Single();

		Assert.Multiple( () => {
			Assert.That( contribution.IsSpousal, Is.False );
			Assert.That( contribution.MemberId, Is.EqualTo( contribution.DestinationMemberId ) );
		} );
	}

	[Test]
	public void Compile_Inheritance_IsNonTaxableIncomeInTheBirthdayMonthIndexedForInflation() {
		Plan plan = TestPlanFactory.Create(
			inheritance: [
				new Inheritance( MemberTodd, 500_000m, AgeReceived: 65 )
			]
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		// Todd is born 1973-12-25, so age 65 falls in December 2038, twelve years after the
		// 2026 plan start.
		CompiledPeriod receipt = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2038, 12, 1 ) );
		CompiledPeriod monthBefore = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2038, 11, 1 ) );
		decimal expected = 500_000m * (decimal)Math.Pow( 1.026, 12 );

		CompiledIncome received = compiledPlan.ScheduledIncome[receipt].Single( i => i.Name == "Todd Inheritance" );

		Assert.Multiple( () => {
			Assert.That( received.Amount, Is.EqualTo( expected ).Within( 0.01m ) );
			Assert.That( received.MemberId.Value, Is.EqualTo( 1 ) );
			Assert.That( received.Taxable, Is.False );
			Assert.That( compiledPlan.ScheduledIncome[monthBefore].Single( i => i.Name == "Todd Inheritance" ).Amount, Is.Zero );
		} );
	}

	[Test]
	public void Compile_ZeroAmountInheritance_CompilesNoInheritanceIncome() {
		Plan plan = TestPlanFactory.Create(
			inheritance: [
				new Inheritance( MemberTodd, 0m, AgeReceived: 65 )
			]
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledPeriod receipt = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2038, 12, 1 ) );

		Assert.That( compiledPlan.ScheduledIncome[receipt].Any( i => i.Name.EndsWith( "Inheritance" ) ), Is.False );
	}

	[Test]
	public void Compile_NoInheritance_CompilesNoInheritanceIncome() {
		CompiledPlan compiledPlan = new PlanCompiler().Compile( TestPlanFactory.Create() );
		CompiledPeriod firstPeriod = compiledPlan.Periods.First();

		Assert.That( compiledPlan.ScheduledIncome[firstPeriod].Any( i => i.Name.EndsWith( "Inheritance" ) ), Is.False );
	}

	[Test]
	public void Compile_UnindexedContribution_UsesConfiguredAmountForEntirePlan() {
		Plan plan = TestPlanFactory.Create(
			contributions: [
				new Contribution( MemberTodd, 3200m, 2026, Indexed: false )
			]
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledMember tinaCompiled = compiledPlan.Members.First( m => m.Name == MemberTina );

		CompiledPeriod firstPeriod = compiledPlan.Periods.First();
		CompiledPeriod january2027 = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2027, 1, 1 ) );
		CompiledPeriod january2033 = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2033, 1, 1 ) );

		Assert.Multiple( () => {
			Assert.That( compiledPlan.Contribution[firstPeriod].Single( c => c.MemberId == toddCompiled.MemberId ).Amount, Is.EqualTo( 3200m ) );
			Assert.That( compiledPlan.Contribution[january2027].Single( c => c.MemberId == toddCompiled.MemberId ).Amount, Is.EqualTo( 3200m ) );
			Assert.That( compiledPlan.Contribution[january2033].Single( c => c.MemberId == toddCompiled.MemberId ).Amount, Is.EqualTo( 3200m ) );
		} );
	}

	[Test]
	public void Compile_IndexedContribution_GrowsAmountWithInflation() {
		Plan plan = TestPlanFactory.Create(
			contributions: [
				new Contribution( MemberTodd, 3200m, 2026, Indexed: true )
			]
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledMember tinaCompiled = compiledPlan.Members.First( m => m.Name == MemberTina );

		CompiledPeriod firstPeriod = compiledPlan.Periods.First();
		CompiledPeriod january2027 = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2027, 1, 1 ) );

		Assert.Multiple( () => {
			Assert.That( compiledPlan.Contribution[firstPeriod].Single( c => c.MemberId == toddCompiled.MemberId ).Amount, Is.EqualTo( 3200m ) );
			Assert.That( compiledPlan.Contribution[january2027].Single( c => c.MemberId == toddCompiled.MemberId ).Amount, Is.EqualTo( 3283.2m ) );
		} );
	}

	[Test]
	public void Compile_PlanStartingAfterMemberDeath_CompilesRemainingPeriods() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2060, 1, 1 ),
			lifeInsurance: [],
			taxPolicy: TestPlanFactory.CreateTaxPolicy() with { Year = 2060 }
		);

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledMember tinaCompiled = compiledPlan.Members.First( m => m.Name == MemberTina );
		CompiledPeriod[] periods = [.. compiledPlan.Periods];

		Assert.Multiple( () => {
			Assert.That( periods[0], Is.EqualTo( new CompiledPeriod( new( 1 ), new DateOnly( 2060, 1, 1 ) ) ) );
			Assert.That( periods[^1].PeriodDate, Is.EqualTo( new DateOnly( 2072, 6, 1 ) ) );
			// Todd (member 1) died 2058-12; his CPP is zero for every compiled period.
			Assert.That(
				periods.All( p => compiledPlan.ScheduledIncome[p].Single( i => i.MemberId == toddCompiled.MemberId && i.Name == "CPP" ).Amount == 0m ),
				Is.True
			);
		} );
	}

	[Test]
	public void Compile_EmptyMembers_Throws() {
		Plan plan = TestPlanFactory.Create(
			members: [],
			assets: [],
			lifeInsurance: [],
			contributions: []
		);

		Assert.That(
			() => new PlanCompiler().Compile( plan ),
			Throws.TypeOf<PlanValidationException>()
		);
	}

	[Test]
	public void Compile_MemberWithoutRetirementAge_RetiresWithHousehold() {
		Plan plan = TestPlanFactory.Create(
			members: [
				new Member( "Todd", new DateOnly( 1973, 12, 25 ), 85, 60, 70, 80m ),
				new Member( "Tina", new DateOnly( 1977, 6, 20 ), 95, null, 70, 50m )
			]
		);

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember[] members = [.. compiledPlan.Members];

		Assert.Multiple( () => {
			// Todd retires at 60 (2034-01-01); Tina, who specifies no retirement age,
			// inherits the shared household retirement date.
			Assert.That( members[0].RetirementDate, Is.EqualTo( new DateOnly( 2034, 1, 1 ) ) );
			Assert.That( members[1].RetirementDate, Is.EqualTo( new DateOnly( 2034, 1, 1 ) ) );
		} );
	}

	[Test]
	public void Compile_NoMemberSpecifiesRetirementAge_Throws() {
		Plan plan = TestPlanFactory.Create(
			members: [
				new Member( "Todd", new DateOnly( 1973, 12, 25 ), 85, null, 70, 80m ),
				new Member( "Tina", new DateOnly( 1977, 6, 20 ), 95, null, 70, 50m )
			]
		);

		Assert.That(
			() => new PlanCompiler().Compile( plan ),
			Throws.TypeOf<PlanValidationException>()
		);
	}

	[Test]
	public void Compile_CPP_ZeroBeforeStartNonZeroAfterStartZeroAfterDeath() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2043, 12, 1 ),
			annualInflationPercent: 0m,
			lifeInsurance: [],
			taxPolicy: TestPlanFactory.CreateTaxPolicy() with { Year = 2043 }
		);

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledPeriod beforeStart = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2043, 12, 1 ) );
		CompiledPeriod atStart = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2044, 1, 1 ) );
		CompiledPeriod afterDeath = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2059, 1, 1 ) );

		Assert.Multiple( () => {
			Assert.That( compiledPlan.ScheduledIncome[beforeStart].Single( i => i.MemberId == toddCompiled.MemberId && i.Name == "CPP" ).Amount, Is.Zero );
			Assert.That( compiledPlan.ScheduledIncome[atStart].Single( i => i.MemberId == toddCompiled.MemberId && i.Name == "CPP" ).Amount, Is.EqualTo( 1712.6904m ) );
			Assert.That( compiledPlan.ScheduledIncome[afterDeath].Single( i => i.MemberId == toddCompiled.MemberId && i.Name == "CPP" ).Amount, Is.Zero );
		} );
	}

	[Test]
	[TestCase( 60, 64.0 )]
	[TestCase( 62, 78.4 )]
	[TestCase( 65, 100.0 )]
	[TestCase( 67, 116.8 )]
	[TestCase( 70, 142.0 )]
	public void Compile_CPPStartAge_AppliesTheActuarialAdjustment(
		int cppStartInYears,
		double expectedPercent
	) {
		// A full entitlement is used so the compiled percent is the adjustment factor itself:
		// 0.6% per month before 65 and 0.7% per month after.
		Plan plan = TestPlanFactory.Create(
			members: [
				new Member( "Todd", new DateOnly( 1973, 12, 25 ), 85, 60, cppStartInYears, 100m ),
				new Member( "Tina", new DateOnly( 1977, 6, 20 ), 95, 57, 65, 100m )
			]
		);

		CompiledMember todd = new PlanCompiler().Compile( plan ).Members.Single( m => m.Name == "Todd" );

		Assert.That( todd.CPPPercent, Is.EqualTo( (decimal)expectedPercent ).Within( 0.0001m ) );
	}

	[Test]
	public void Compile_CPPStartAge_ScalesTheConfiguredEntitlementRatherThanReplacingIt() {
		Plan plan = TestPlanFactory.Create(
			members: [
				new Member( "Todd", new DateOnly( 1973, 12, 25 ), 85, 60, 70, 80m ),
				new Member( "Tina", new DateOnly( 1977, 6, 20 ), 95, 57, 60, 50m )
			]
		);

		CompiledMember[] members = [.. new PlanCompiler().Compile( plan ).Members];

		Assert.Multiple( () => {
			Assert.That( members.Single( m => m.Name == "Todd" ).CPPPercent, Is.EqualTo( 113.6m ) );
			Assert.That( members.Single( m => m.Name == "Tina" ).CPPPercent, Is.EqualTo( 32m ) );
		} );
	}

	[Test]
	public void Compile_CPP_InflatesEachDecember() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2044, 1, 1 ),
			lifeInsurance: [],
			taxPolicy: TestPlanFactory.CreateTaxPolicy() with { Year = 2044 }
		);

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledMember tinaCompiled = compiledPlan.Members.First( m => m.Name == MemberTina );
		CompiledPeriod january2044 = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2044, 1, 1 ) );
		CompiledPeriod december2044 = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2044, 12, 1 ) );
		CompiledPeriod january2045 = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2045, 1, 1 ) );

		Assert.Multiple( () => {
			Assert.That( compiledPlan.ScheduledIncome[january2044].Single( i => i.MemberId == toddCompiled.MemberId && i.Name == "CPP" ).Amount, Is.EqualTo( 1712.6904m ) );
			Assert.That( compiledPlan.ScheduledIncome[december2044].Single( i => i.MemberId == toddCompiled.MemberId && i.Name == "CPP" ).Amount, Is.EqualTo( 1712.6904m ) );
			Assert.That( compiledPlan.ScheduledIncome[january2045].Single( i => i.MemberId == toddCompiled.MemberId && i.Name == "CPP" ).Amount, Is.EqualTo( 1757.2203504m ) );
		} );
	}

	[Test]
	public void Compile_OAS_StartsAtExpectedAge() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2038, 12, 1 ),
			annualInflationPercent: 0m,
			lifeInsurance: [],
			taxPolicy: TestPlanFactory.CreateTaxPolicy() with { Year = 2038 }
		);

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledMember tinaCompiled = compiledPlan.Members.First( m => m.Name == MemberTina );
		CompiledPeriod beforeStart = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2038, 12, 1 ) );
		CompiledPeriod atStart = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2039, 1, 1 ) );

		Assert.Multiple( () => {
			Assert.That( compiledPlan.ScheduledIncome[beforeStart].Single( i => i.MemberId == toddCompiled.MemberId && i.Name == "OAS" ).Amount, Is.Zero );
			Assert.That( compiledPlan.ScheduledIncome[atStart].Single( i => i.MemberId == toddCompiled.MemberId && i.Name == "OAS" ).Amount, Is.EqualTo( 743.05m ) );
		} );
	}

	[Test]
	public void Compile_OAS_IncreasesAtAge75() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2039, 1, 1 ),
			annualInflationPercent: 0m,
			lifeInsurance: [],
			taxPolicy: TestPlanFactory.CreateTaxPolicy() with { Year = 2039 }
		);

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledMember tinaCompiled = compiledPlan.Members.First( m => m.Name == MemberTina );
		// Todd born 1973-12-25 turns 75 on 2048-12-25.
		CompiledPeriod beforeSeventyFive = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2048, 12, 1 ) );
		CompiledPeriod afterSeventyFive = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2049, 1, 1 ) );

		Assert.Multiple( () => {
			Assert.That( compiledPlan.ScheduledIncome[beforeSeventyFive].Single( i => i.MemberId == toddCompiled.MemberId && i.Name == "OAS" ).Amount, Is.EqualTo( 743.05m ) );
			Assert.That( compiledPlan.ScheduledIncome[afterSeventyFive].Single( i => i.MemberId == toddCompiled.MemberId && i.Name == "OAS" ).Amount, Is.EqualTo( 817.355m ) );
		} );
	}

	[Test]
	public void Compile_CPPSurvivor_AppliesCombinedMaximum() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2025, 2, 1 ),
			members: [
				new Member( "Todd", new DateOnly( 1950, 1, 1 ), 75, 60, 65, 100m ),
				new Member( "Tina", new DateOnly( 1950, 1, 1 ), 90, 60, 65, 100m )
			],
			annualInflationPercent: 0m,
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 100m ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 100m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0.0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0.0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0.0m, hasUnlimitedContributionRoom: true ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0.0m, hasUnlimitedContributionRoom: true )
			],
			lifeInsurance: [],
			contributions: []
		);

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledMember toddCompiled = compiledPlan.Members.First( m => m.Name == MemberTodd );
		CompiledMember tinaCompiled = compiledPlan.Members.First( m => m.Name == MemberTina );
		CompiledPeriod afterToddDeath = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2025, 2, 1 ) );
		CompiledIncome survivorIncome = compiledPlan.ScheduledIncome[afterToddDeath]
			.Single( i => i.MemberId == tinaCompiled.MemberId && i.Name == "CPP Survivor" );

		// Survivor already receives the full CPP (1507.65); the partner top-up is capped
		// by the combined survivor maximum (1531.56), leaving 23.91.
		Assert.That( survivorIncome.Amount, Is.EqualTo( 23.91m ) );
	}

	[Test]
	public void Compile_LifeInsurance_PaidExactlyOnce() {
		CompiledPlan compiledPlan = new PlanCompiler().Compile( TestPlanFactory.Create() );

		CompiledIncome[] toddInsurance = [..
			compiledPlan.Periods
				.SelectMany( p => compiledPlan.ScheduledIncome[p] )
				.Where( i => i.Name == "Todd Life Insurance" && i.Amount != 0m )
		];

		Assert.Multiple( () => {
			Assert.That( toddInsurance, Has.Length.EqualTo( 1 ) );
			Assert.That( toddInsurance[0].Amount, Is.EqualTo( 250_000m ) );
			// Todd born 1973-12-25 with target age 85 dies in December 2058.
			CompiledPeriod deathPeriod = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2058, 12, 1 ) );
			Assert.That( compiledPlan.ScheduledIncome[deathPeriod].Single( i => i.Name == "Todd Life Insurance" ).Amount, Is.EqualTo( 250_000m ) );
		} );
	}

	[Test]
	public void Compile_ScheduledIncome_ClassifiesTaxableAndNonTaxable() {
		CompiledPlan compiledPlan = new PlanCompiler().Compile( TestPlanFactory.Create() );
		CompiledPeriod firstPeriod = compiledPlan.Periods.First();
		CompiledIncome[] income = [.. compiledPlan.ScheduledIncome[firstPeriod]];

		Assert.Multiple( () => {
			Assert.That( income.First( i => i.Name == "CPP" ).Taxable, Is.True );
			Assert.That( income.First( i => i.Name == "OAS" ).Taxable, Is.True );
			Assert.That( income.First( i => i.Name == "CPP Survivor" ).Taxable, Is.True );
			Assert.That( income.First( i => i.Name == "Todd Life Insurance" ).Taxable, Is.False );
		} );
	}

	[Test]
	public void Compile_DesiredIncome_CharacterizesPhaseBoundaries() {
		Plan plan = TestPlanFactory.Create( annualInflationPercent: 0m );

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		Assert.Multiple( () => {
			Assert.That( DesiredIncomeAt( compiledPlan, new DateOnly( 2033, 12, 1 ) ), Is.Zero );
			Assert.That( DesiredIncomeAt( compiledPlan, new DateOnly( 2034, 1, 1 ) ), Is.EqualTo( 7000m ) );
			Assert.That( DesiredIncomeAt( compiledPlan, new DateOnly( 2052, 4, 1 ) ), Is.EqualTo( 7000m ) );
			Assert.That( DesiredIncomeAt( compiledPlan, new DateOnly( 2052, 5, 1 ) ), Is.EqualTo( 6500m ) );
			Assert.That( DesiredIncomeAt( compiledPlan, new DateOnly( 2062, 5, 1 ) ), Is.EqualTo( 6500m ) );
			Assert.That( DesiredIncomeAt( compiledPlan, new DateOnly( 2062, 6, 1 ) ), Is.EqualTo( 6000m ) );
			Assert.That( compiledPlan.DesiredIncome[compiledPlan.Periods.Last()], Is.EqualTo( 6000m ) );
		} );
	}

	[Test]
	public void Compile_DesiredIncome_InflatesEachDecember() {
		CompiledPlan compiledPlan = new PlanCompiler().Compile( TestPlanFactory.Create() );

		decimal january2034 = DesiredIncomeAt( compiledPlan, new DateOnly( 2034, 1, 1 ) );
		decimal november2034 = DesiredIncomeAt( compiledPlan, new DateOnly( 2034, 11, 1 ) );
		decimal january2035 = DesiredIncomeAt( compiledPlan, new DateOnly( 2035, 1, 1 ) );

		Assert.Multiple( () => {
			Assert.That( november2034, Is.EqualTo( january2034 ) );
			Assert.That( january2035, Is.EqualTo( january2034 * 1.026m ) );
		} );
	}

	[Test]
	public void Compile_DesiredIncome_PhaseDurationsExceedingPlanFillAllPeriods() {
		Plan plan = TestPlanFactory.Create(
			annualInflationPercent: 0m,
			retirementIncome: new RetirementIncome(
				GoGo: 7000m,
				SlowGo: 6500m,
				SlowGoYears: 10,
				NoGo: 6000m,
				NoGoYears: 100
			)
		);

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		// The No-Go window spans the entire plan, so even the first period receives No-Go income.
		Assert.That( compiledPlan.DesiredIncome[compiledPlan.Periods.First()], Is.EqualTo( 6000m ) );
	}

	private static decimal DesiredIncomeAt(
		CompiledPlan compiledPlan,
		DateOnly periodDate
	) {
		CompiledPeriod period = compiledPlan.Periods.Single( p => p.PeriodDate == periodDate );
		return compiledPlan.DesiredIncome[period];
	}
}
