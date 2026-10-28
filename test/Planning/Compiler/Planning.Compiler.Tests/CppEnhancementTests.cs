using Planning.Model.CompiledPlans;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Compiler.Tests;

public class CppEnhancementTests {

	[Test]
	public void Compile_SampleAssumptions_ReplacesReferenceEnhancementWithWorkingLifeAccrual() {
		Plan plan = CreatePlan( retirementAge: 60, cppStartAge: 70, firstPercent: 100m, secondPercent: 100m ) with {
			CPPMaximum = 1_507.65m, CPPCombinedSurvivorMaximum = 1_531.56m
		};
		plan = ReplaceTodd( plan, m => m with { CPPBasePercent = 90m } );

		CompiledPlan compiled = new PlanCompiler().Compile( plan );
		CompiledCPPEntitlement todd = compiled.Members.Single( m => m.Name == "Todd" ).CPPEntitlement;
		CompiledCPPEntitlement tina = compiled.Members.Single( m => m.Name == "Tina" ).CPPEntitlement;

		Assert.Multiple( () => {
			Assert.That( todd.BaseMonthlyAt65, Is.EqualTo( 1_297.11m ).Within( 0.000001m ) );
			Assert.That( todd.FirstAdditionalMonthlyAt65, Is.InRange( 150m, 155m ) );
			Assert.That( todd.SecondAdditionalMonthlyAt65, Is.InRange( 60m, 65m ) );
			Assert.That( tina.BaseMonthlyAt65, Is.EqualTo( 720.6166666667m ).Within( 0.000001m ) );
			Assert.That( tina.FirstAdditionalMonthlyAt65, Is.EqualTo( todd.FirstAdditionalMonthlyAt65 * 0.9m ) );
			Assert.That( tina.SecondAdditionalMonthlyAt65, Is.Zero );
		} );
	}

	[TestCase( 45, 0.0 )]
	[TestCase( 46, 1.4994 )]
	[TestCase( 47, 4.4982 )]
	[TestCase( 48, 9.4962 )]
	[TestCase( 49, 16.9932 )]
	[TestCase( 50, 26.9892 )]
	[TestCase( 51, 36.9852 )]
	public void Compile_FirstAdditional_AppliesIntroductionAndPhaseIn(
		int retirementAge,
		double expected
	) {
		Plan plan = CreatePlan( retirementAge, cppStartAge: 70, firstPercent: 100m, secondPercent: 0m );

		CompiledCPPEntitlement cpp = Todd( plan ).CPPEntitlement;

		Assert.That( cpp.FirstAdditionalMonthlyAt65, Is.EqualTo( (decimal)expected ).Within( 0.000001m ) );
		Assert.That( cpp.SecondAdditionalMonthlyAt65, Is.Zero );
	}

	[Test]
	public void Compile_SecondAdditional_AppliesHistoricalBandsAndFutureFullBand() {
		Plan plan = CreatePlan( retirementAge: 60, cppStartAge: 70, firstPercent: 100m, secondPercent: 100m );

		CompiledCPPEntitlement cpp = Todd( plan ).CPPEntitlement;
		decimal secondBands = 4_700m / 68_500m + 9_900m / 71_300m + 10_400m / 74_600m + 7m * 0.14m;

		Assert.Multiple( () => {
			Assert.That( cpp.BaseMonthlyAt65, Is.EqualTo( 960m ) );
			Assert.That( cpp.FirstAdditionalMonthlyAt65, Is.EqualTo( 126.9492m ) );
			Assert.That( cpp.SecondAdditionalMonthlyAt65, Is.EqualTo( 1_599.84m * secondBands / 40m ).Within( 0.000001m ) );
		} );
	}

	[Test]
	public void Compile_Percentages_ScaleOnlyTheirOwnComponents() {
		Plan full = CreatePlan( retirementAge: 60, cppStartAge: 70, firstPercent: 100m, secondPercent: 100m );
		Plan partial = CreatePlan( retirementAge: 60, cppStartAge: 70, firstPercent: 90m, secondPercent: 0m );

		CompiledCPPEntitlement fullCPP = Todd( full ).CPPEntitlement;
		CompiledCPPEntitlement partialCPP = Todd( partial ).CPPEntitlement;

		Assert.Multiple( () => {
			Assert.That( partialCPP.BaseMonthlyAt65, Is.EqualTo( fullCPP.BaseMonthlyAt65 ) );
			Assert.That( partialCPP.FirstAdditionalMonthlyAt65, Is.EqualTo( fullCPP.FirstAdditionalMonthlyAt65 * 0.9m ) );
			Assert.That( partialCPP.SecondAdditionalMonthlyAt65, Is.Zero );
		} );
	}

	[Test]
	public void Compile_MidYearRetirement_CountsOnlyWorkingMonths() {
		Plan plan = CreatePlan( retirementAge: 51, cppStartAge: 70, firstPercent: 100m, secondPercent: 100m );
		plan = ReplaceTodd( plan, m => m with { BirthDate = new DateOnly( 1973, 6, 30 ) } );

		CompiledCPPEntitlement cpp = Todd( plan ).CPPEntitlement;

		Assert.Multiple( () => {
			Assert.That( cpp.FirstAdditionalMonthlyAt65, Is.EqualTo( 31.9872m ) );
			Assert.That( cpp.SecondAdditionalMonthlyAt65,
				Is.EqualTo( 1_599.84m * ( 4_700m / 68_500m ) / 80m ).Within( 0.000001m ) );
		} );
	}

	[Test]
	public void Compile_Age18Eligibility_ExcludesEarlierMonths() {
		Plan plan = CreatePlan( retirementAge: 19, cppStartAge: 70, firstPercent: 100m, secondPercent: 0m );
		plan = ReplaceTodd( plan, m => m with { BirthDate = new DateOnly( 2006, 6, 30 ) } );

		CompiledCPPEntitlement cpp = Todd( plan ).CPPEntitlement;

		Assert.That( cpp.FirstAdditionalMonthlyAt65, Is.EqualTo( 9.996m ) );
	}

	[Test]
	public void Compile_LongCareer_UsesBest480MonthsForEachComponent() {
		Plan plan = CreatePlan( retirementAge: 66, cppStartAge: 70, firstPercent: 100m, secondPercent: 100m );
		plan = ReplaceTodd( plan, m => m with { BirthDate = new DateOnly( 2000, 12, 31 ) } );

		CompiledCPPEntitlement cpp = Todd( plan ).CPPEntitlement;

		Assert.Multiple( () => {
			Assert.That( cpp.FirstAdditionalMonthlyAt65, Is.EqualTo( 399.84m ) );
			Assert.That( cpp.SecondAdditionalMonthlyAt65, Is.EqualTo( 223.9776m ) );
		} );
	}

	[Test]
	public void Compile_CppBeforeRetirement_StopsAccrualAtCppCommencement() {
		Plan retiring = CreatePlan( retirementAge: 65, cppStartAge: 70, firstPercent: 100m, secondPercent: 100m );
		Plan claiming = CreatePlan( retirementAge: 70, cppStartAge: 65, firstPercent: 100m, secondPercent: 100m );

		Assert.That( Todd( claiming ).CPPEntitlement, Is.EqualTo( Todd( retiring ).CPPEntitlement ) );
	}

	[Test]
	public void Compile_DeferralAfterRetirement_DoesNotAddContributionYears() {
		Plan early = CreatePlan( retirementAge: 60, cppStartAge: 60, firstPercent: 100m, secondPercent: 100m );
		Plan late = CreatePlan( retirementAge: 60, cppStartAge: 70, firstPercent: 100m, secondPercent: 100m );

		Assert.Multiple( () => {
			Assert.That( Todd( early ).CPPEntitlement, Is.EqualTo( Todd( late ).CPPEntitlement ) );
			Assert.That( Todd( early ).CPPAdjustmentFactor, Is.EqualTo( 0.64m ) );
			Assert.That( Todd( late ).CPPAdjustmentFactor, Is.EqualTo( 1.42m ) );
		} );
	}

	[Test]
	public void Compile_MemberWithoutRetirementAge_UsesSharedRetirementDate() {
		Plan plan = CreatePlan( retirementAge: 60, cppStartAge: 70, firstPercent: 100m, secondPercent: 100m );
		CompiledPlan compiled = new PlanCompiler().Compile( plan );
		CompiledMember todd = compiled.Members.Single( m => m.Name == "Todd" );
		CompiledMember tina = compiled.Members.Single( m => m.Name == "Tina" );

		Assert.Multiple( () => {
			Assert.That( tina.RetirementDate, Is.EqualTo( todd.RetirementDate ) );
			Assert.That( tina.CPPEntitlement.FirstAdditionalMonthlyAt65,
				Is.EqualTo( todd.CPPEntitlement.FirstAdditionalMonthlyAt65 * 0.9m ) );
			Assert.That( tina.CPPEntitlement.SecondAdditionalMonthlyAt65, Is.Zero );
		} );
	}

	[Test]
	public void Compile_DeathBeforeRetirement_StopsAccrualAfterDeathMonth() {
		Plan plan = CreatePlan( retirementAge: 70, cppStartAge: 70, firstPercent: 100m, secondPercent: 100m );
		plan = plan with { Members = [
			plan.Members.Single( m => m.Name == "Todd" ) with { TargetAgeInYears = 66, RetirementAgeInYears = null },
			plan.Members.Single( m => m.Name == "Tina" ) with { BirthDate = new DateOnly( 1970, 1, 1 ), RetirementAgeInYears = 74 }
		] };

		Assert.That( Todd( plan ).CPPEntitlement.FirstAdditionalMonthlyAt65, Is.EqualTo( 399.84m * 18.7m / 40m ) );
	}

	[Test]
	public void Compile_OrdinaryCpp_SumsAdjustsAndIndexesAllComponents() {
		Plan plan = CreatePlan( retirementAge: 60, cppStartAge: 70, firstPercent: 100m, secondPercent: 100m ) with {
			AnnualInflationPercent = 2m
		};
		CompiledPlan compiled = new PlanCompiler().Compile( plan );
		CompiledMember todd = compiled.Members.Single( m => m.Name == "Todd" );
		decimal indexed = todd.CPPEntitlement.TotalMonthlyAt65 * 1.42m * (decimal)Math.Pow( 1.02, 18 );

		Assert.That( Income( compiled, new DateOnly( 2044, 1, 1 ), "Todd", "CPP" ),
			Is.EqualTo( indexed ).Within( 0.000001m ) );
	}

	[TestCase( 60 )]
	[TestCase( 65 )]
	[TestCase( 70 )]
	public void Compile_SurvivorComponents_AreReducedIndependentlyAndNotCapped( int survivorStartAge ) {
		Plan plan = CreatePlan( retirementAge: 60, cppStartAge: 70, firstPercent: 100m, secondPercent: 0m );
		Member toddInput = plan.Members.Single( m => m.Name == "Todd" );
		plan = plan with { Members = [
			toddInput with { CPPBasePercent = 100m },
			plan.Members.Single( m => m.Name == "Tina" ) with {
				BirthDate = toddInput.BirthDate, CPPBasePercent = 100m,
				CPPFirstAdditionalPercent = 0m, CPPSecondAdditionalPercent = 100m,
				CPPStartInYears = survivorStartAge
			}
		] };
		CompiledPlan compiled = new PlanCompiler().Compile( plan );
		CompiledMember todd = compiled.Members.Single( m => m.Name == "Todd" );
		CompiledMember tina = compiled.Members.Single( m => m.Name == "Tina" );

		// Tina has no first-additional entitlement to trigger an overlap reduction.
		Assert.Multiple( () => {
			Assert.That( Income( compiled, new DateOnly( 2059, 1, 1 ), "Tina", "CPP Survivor" ),
				Is.EqualTo( todd.CPPEntitlement.FirstAdditionalMonthlyAt65 * 0.6m ) );
			Assert.That( Income( compiled, new DateOnly( 2059, 1, 1 ), "Tina", "CPP" ),
				Is.EqualTo( tina.CPPEntitlement.TotalMonthlyAt65 * tina.CPPAdjustmentFactor
					+ 720m * Math.Max( 1m - tina.CPPAdjustmentFactor, 0m ) ) );
		} );
	}

	[Test]
	public void Compile_ZeroBaseShare_DoesNotDisableEnhancements() {
		Plan plan = CreatePlan( retirementAge: 60, cppStartAge: 70, firstPercent: 100m, secondPercent: 100m );
		plan = ReplaceTodd( plan, m => m with { CPPBasePercent = 0m } );

		CompiledCPPEntitlement cpp = Todd( plan ).CPPEntitlement;

		Assert.Multiple( () => {
			Assert.That( cpp.BaseMonthlyAt65, Is.Zero );
			Assert.That( cpp.FirstAdditionalMonthlyAt65, Is.GreaterThan( 0m ) );
			Assert.That( cpp.SecondAdditionalMonthlyAt65, Is.GreaterThan( 0m ) );
		} );
	}

	[Test]
	public void Compile_ZeroMaximum_DisablesAllComponents() {
		Plan plan = CreatePlan( retirementAge: 60, cppStartAge: 70, firstPercent: 100m, secondPercent: 100m ) with {
			CPPMaximum = 0m, CPPCombinedSurvivorMaximum = 0m
		};

		Assert.That( Todd( plan ).CPPEntitlement, Is.EqualTo( new CompiledCPPEntitlement( 0m, 0m, 0m ) ) );
	}

	[TestCase( 65 )]
	[TestCase( 70 )]
	public void Compile_FullBaseAndEnhancedEntitlements_OnlyCapsBase( int survivorStartAge ) {
		Plan plan = CreatePlan( retirementAge: 60, cppStartAge: 70, firstPercent: 100m, secondPercent: 100m );
		Member todd = plan.Members.Single( m => m.Name == "Todd" );
		plan = plan with { Members = [
			todd with { CPPBasePercent = 100m },
			plan.Members.Single( m => m.Name == "Tina" ) with {
				BirthDate = todd.BirthDate, CPPBasePercent = 100m,
				CPPStartInYears = survivorStartAge,
				CPPFirstAdditionalPercent = 100m, CPPSecondAdditionalPercent = 100m
			}
		] };
		CompiledPlan compiled = new PlanCompiler().Compile( plan );
		CompiledMember tina = compiled.Members.Single( m => m.Name == "Tina" );
		decimal enhanced = tina.CPPEntitlement.FirstAdditionalMonthlyAt65 + tina.CPPEntitlement.SecondAdditionalMonthlyAt65;

		Assert.Multiple( () => {
			Assert.That( Income( compiled, new DateOnly( 2059, 1, 1 ), "Tina", "CPP Survivor" ),
				Is.EqualTo( enhanced * 0.36m ).Within( 0.000001m ) );
			Assert.That( Income( compiled, new DateOnly( 2059, 1, 1 ), "Tina", "CPP" ),
				Is.EqualTo( tina.CPPEntitlement.TotalMonthlyAt65 * tina.CPPAdjustmentFactor ) );
		} );
	}

	private static Plan CreatePlan(
		int retirementAge,
		int cppStartAge,
		decimal firstPercent,
		decimal secondPercent
	) {
		return TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			cppMaximum: 1_200m,
			cppCombinedSurvivorMaximum: 1_200m,
			annualInflationPercent: 0m,
			members: [
				new Member( "Todd", new DateOnly( 1973, 12, 31 ), 85, retirementAge, cppStartAge, 80m, 65, firstPercent, secondPercent ),
				new Member( "Tina", new DateOnly( 1976, 7, 22 ), 95, null, 65, 50m, 65, 90m, 0m )
			] );
	}

	private static Plan ReplaceTodd(
		Plan plan,
		Func<Member, Member> replace
	) => plan with { Members = [.. plan.Members.Select( m => m.Name == "Todd" ? replace( m ) : m )] };

	private static CompiledMember Todd(
		Plan plan
	) => new PlanCompiler().Compile( plan ).Members.Single( m => m.Name == "Todd" );

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
