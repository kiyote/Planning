using Planning.Model.CompiledPlans;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Compiler.Tests;

public class CppSurvivorTests {

	[TestCase( 60 )]
	[TestCase( 65 )]
	[TestCase( 70 )]
	public void Compile_DeceasedStartAge_DoesNotChangeSurvivorEntitlement( int startAge ) {
		Plan plan = CreatePlan( startAge, 65, deceasedPercent: 80m, survivorPercent: 50m );

		CompiledPlan compiled = new PlanCompiler().Compile( plan );

		Assert.That( Income( compiled, new DateOnly( 2025, 2, 1 ), "Tina", "CPP Survivor" ), Is.EqualTo( 288m ) );
	}

	[TestCase( 60, 389.12 )]
	[TestCase( 65, 500.0 )]
	[TestCase( 66, 542.0 )]
	[TestCase( 70, 710.0 )]
	public void Compile_SurvivorStartAge_PreservesDeferralAndAdjustsEarlyRetirement(
		int startAge,
		double expectedRetirement
	) {
		Plan plan = CreatePlan( 65, startAge, deceasedPercent: 80m, survivorPercent: 50m );

		CompiledPlan compiled = new PlanCompiler().Compile( plan );
		DateOnly afterDeath = new DateOnly( 2025, 2, 1 );

		Assert.Multiple( () => {
			Assert.That( Income( compiled, afterDeath, "Tina", "CPP Survivor" ), Is.EqualTo( 288m ) );
			Assert.That( Income( compiled, afterDeath, "Tina", "CPP" ), Is.EqualTo( (decimal)expectedRetirement ) );
		} );
	}

	[TestCase( 0, 480.0 )]
	[TestCase( 10, 440.0 )]
	[TestCase( 50, 288.0 )]
	[TestCase( 90, 100.0 )]
	[TestCase( 100, 0.0 )]
	public void Compile_CombinedBasePension_UsesLesserOverlapReductionAndUnadjustedCap(
		int survivorPercent,
		double expectedSurvivor
	) {
		Plan plan = CreatePlan( 65, 70, deceasedPercent: 80m, survivorPercent: survivorPercent );

		CompiledPlan compiled = new PlanCompiler().Compile( plan );

		Assert.That(
			Income( compiled, new DateOnly( 2025, 2, 1 ), "Tina", "CPP Survivor" ),
			Is.EqualTo( (decimal)expectedSurvivor ) );
	}

	[TestCase( 65, 1000.0 )]
	[TestCase( 70, 1420.0 )]
	public void Compile_PublishedMaxima_OnlySupplyBaseReferenceWithoutAccrual(
		int startAge,
		double expectedRetirement
	) {
		// The members stopped work before 2019, so only the 1,000 base reference applies.
		Plan plan = CreatePlan( 65, startAge, deceasedPercent: 100m, survivorPercent: 100m ) with {
			CPPMaximum = 1_100m,
			CPPCombinedSurvivorMaximum = 1_136m
		};

		CompiledPlan compiled = new PlanCompiler().Compile( plan );
		DateOnly afterDeath = new DateOnly( 2025, 2, 1 );

		Assert.Multiple( () => {
			Assert.That( Income( compiled, afterDeath, "Tina", "CPP Survivor" ), Is.Zero );
			Assert.That( Income( compiled, afterDeath, "Tina", "CPP" ), Is.EqualTo( (decimal)expectedRetirement ) );
		} );
	}

	[Test]
	public void Compile_PublishedReferenceEnhancement_IsNotAddedWithoutAccrual() {
		Plan plan = CreatePlan( 65, 70, deceasedPercent: 80m, survivorPercent: 50m ) with {
			CPPMaximum = 1_100m,
			CPPCombinedSurvivorMaximum = 1_136m
		};

		CompiledPlan compiled = new PlanCompiler().Compile( plan );

		Assert.That(
			Income( compiled, new DateOnly( 2025, 2, 1 ), "Tina", "CPP Survivor" ),
			Is.EqualTo( 288m ) );
	}

	[Test]
	public void Compile_UnaccruedEnhancements_DoNotAffectEarlyAdjustment() {
		Plan plan = CreatePlan( 65, 60, deceasedPercent: 100m, survivorPercent: 90m ) with {
			CPPMaximum = 1_100m,
			CPPCombinedSurvivorMaximum = 1_136m
		};

		CompiledPlan compiled = new PlanCompiler().Compile( plan );
		DateOnly afterDeath = new DateOnly( 2025, 2, 1 );

		Assert.Multiple( () => {
			Assert.That( Income( compiled, afterDeath, "Tina", "CPP Survivor" ), Is.EqualTo( 100m ) );
			Assert.That( Income( compiled, afterDeath, "Tina", "CPP" ), Is.EqualTo( 756m ) );
		} );
	}

	[Test]
	public void Compile_OwnCppNotStarted_PaysFullSurvivorPensionUntilOwnStart() {
		Plan plan = CreatePlan( 65, 70, deceasedPercent: 80m, survivorPercent: 50m );
		plan = plan with {
			Members = [.. plan.Members.Select( m => m.Name == "Tina"
				? m with { BirthDate = new DateOnly( 1958, 6, 15 ) }
				: m )]
		};

		CompiledPlan compiled = new PlanCompiler().Compile( plan );
		DateOnly beforeOwnStart = new DateOnly( 2028, 6, 1 );
		DateOnly ownStart = new DateOnly( 2028, 7, 1 );

		Assert.Multiple( () => {
			Assert.That( Income( compiled, beforeOwnStart, "Tina", "CPP" ), Is.Zero );
			Assert.That( Income( compiled, beforeOwnStart, "Tina", "CPP Survivor" ), Is.EqualTo( 480m ) );
			Assert.That( Income( compiled, ownStart, "Tina", "CPP" ), Is.EqualTo( 710m ) );
			Assert.That( Income( compiled, ownStart, "Tina", "CPP Survivor" ), Is.EqualTo( 288m ) );
		} );
	}

	[Test]
	public void Compile_DeathMonthAndIndexing_KeepPaymentsAndRetirementAdjustmentAligned() {
		Plan plan = CreatePlan( 65, 60, deceasedPercent: 80m, survivorPercent: 50m ) with {
			AnnualInflationPercent = 2m
		};

		CompiledPlan compiled = new PlanCompiler().Compile( plan );
		DateOnly deathMonth = new DateOnly( 2025, 1, 1 );
		DateOnly afterDeath = new DateOnly( 2025, 2, 1 );
		DateOnly indexed = new DateOnly( 2026, 1, 1 );

		Assert.Multiple( () => {
			Assert.That( Income( compiled, deathMonth, "Todd", "CPP" ), Is.EqualTo( 800m ) );
			Assert.That( Income( compiled, deathMonth, "Tina", "CPP" ), Is.EqualTo( 320m ) );
			Assert.That( Income( compiled, deathMonth, "Tina", "CPP Survivor" ), Is.Zero );
			Assert.That( Income( compiled, afterDeath, "Todd", "CPP" ), Is.Zero );
			Assert.That( Income( compiled, afterDeath, "Todd", "CPP Survivor" ), Is.Zero );
			Assert.That( Income( compiled, afterDeath, "Tina", "CPP Survivor" ), Is.EqualTo( 288m ) );
			Assert.That( Income( compiled, afterDeath, "Tina", "CPP" ), Is.EqualTo( 389.12m ) );
			Assert.That( Income( compiled, indexed, "Tina", "CPP Survivor" ), Is.EqualTo( 293.76m ) );
			Assert.That( Income( compiled, indexed, "Tina", "CPP" ), Is.EqualTo( 396.9024m ) );
		} );
	}

	[Test]
	public void Compile_NoAccruedEnhancements_LeavesNoSurvivorPensionAtFullBaseEntitlement() {
		Plan plan = CreatePlan( 65, 65, deceasedPercent: 100m, survivorPercent: 100m ) with {
			CPPMaximum = 1_100m,
			CPPCombinedSurvivorMaximum = 1_136m,
			AnnualInflationPercent = 2m
		};

		CompiledPlan compiled = new PlanCompiler().Compile( plan );

		Assert.That( Income( compiled, new DateOnly( 2026, 1, 1 ), "Tina", "CPP Survivor" ), Is.Zero );
	}

	[Test]
	public void Compile_ZeroCppMaximum_DisablesSurvivorIncomeToo() {
		Plan plan = CreatePlan( 65, 60, deceasedPercent: 80m, survivorPercent: 50m ) with {
			CPPMaximum = 0m
		};

		CompiledPlan compiled = new PlanCompiler().Compile( plan );

		Assert.That(
			compiled.ScheduledIncome.Values.SelectMany( i => i ).Where( i => i.Name is "CPP" or "CPP Survivor" ),
			Has.All.Property( nameof( CompiledIncome.Amount ) ).Zero );
	}

	[Test]
	public void Compile_ZeroDeceasedEntitlement_PaysNoSurvivorIncome() {
		Plan plan = CreatePlan( 65, 60, deceasedPercent: 0m, survivorPercent: 50m );

		CompiledPlan compiled = new PlanCompiler().Compile( plan );
		DateOnly afterDeath = new DateOnly( 2025, 2, 1 );

		Assert.Multiple( () => {
			Assert.That( Income( compiled, afterDeath, "Tina", "CPP Survivor" ), Is.Zero );
			Assert.That( Income( compiled, afterDeath, "Tina", "CPP" ), Is.EqualTo( 320m ) );
		} );
	}

	[Test]
	public void Compile_MembersReversed_ProducesSameSurvivorIncome() {
		Plan plan = CreatePlan( 65, 60, deceasedPercent: 80m, survivorPercent: 50m );
		plan = plan with { Members = plan.Members.Reverse().ToArray() };

		CompiledPlan compiled = new PlanCompiler().Compile( plan );
		DateOnly afterDeath = new DateOnly( 2025, 2, 1 );

		Assert.Multiple( () => {
			Assert.That( Income( compiled, afterDeath, "Tina", "CPP Survivor" ), Is.EqualTo( 288m ) );
			Assert.That( Income( compiled, afterDeath, "Tina", "CPP" ), Is.EqualTo( 389.12m ) );
		} );
	}

	[Test]
	public void Compile_SimultaneousDeaths_PaysNoSurvivorIncome() {
		Plan plan = CreatePlan( 65, 65, deceasedPercent: 80m, survivorPercent: 50m );
		Member todd = plan.Members.Single( m => m.Name == "Todd" );
		plan = plan with {
			Members = [.. plan.Members.Select( m => m.Name == "Tina"
				? m with { BirthDate = todd.BirthDate, TargetAgeInYears = todd.TargetAgeInYears }
				: m )]
		};

		CompiledPlan compiled = new PlanCompiler().Compile( plan );

		Assert.That(
			compiled.ScheduledIncome.Values.SelectMany( i => i ).Where( i => i.Name == "CPP Survivor" ),
			Has.All.Property( nameof( CompiledIncome.Amount ) ).Zero );
	}

	private static Plan CreatePlan(
		int deceasedStartAge,
		int survivorStartAge,
		decimal deceasedPercent,
		decimal survivorPercent
	) {
		return TestPlanFactory.Create(
			startDate: new DateOnly( 2025, 1, 1 ),
			members: [
				new Member( "Todd", new DateOnly( 1950, 1, 15 ), 75, 60, deceasedStartAge, deceasedPercent, 65, 0m, 0m ),
				new Member( "Tina", new DateOnly( 1950, 6, 15 ), 85, 60, survivorStartAge, survivorPercent, 65, 0m, 0m )
			],
			cppMaximum: 1_000m,
			cppCombinedSurvivorMaximum: 1_000m,
			annualInflationPercent: 0m
		);
	}

	private static decimal Income(
		CompiledPlan plan,
		DateOnly date,
		string memberName,
		string incomeName
	) {
		CompiledMember member = plan.Members.Single( m => m.Name == memberName );
		CompiledPeriod period = plan.Periods.Single( p => p.PeriodDate == date );
		return plan.ScheduledIncome[period].Single( i => i.MemberId == member.MemberId && i.Name == incomeName ).Amount;
	}
}
