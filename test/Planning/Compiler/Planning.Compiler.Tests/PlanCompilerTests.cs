using Planning.Model;
using Planning.Model.CompiledPlans;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Compiler.Tests;

public class PlanCompilerTests {

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
				MemberId: 1,
				Name: "Todd",
				BirthDate: new DateOnly( 1973, 12, 25 ),
				DeathDate: new DateOnly( 2058, 12, 31 ),
				RetirementDate: new DateOnly( 2034, 1, 1 ),
				CPPStartDate: new DateOnly( 2044, 1, 1 ),
				OASStartDate: new DateOnly( 2039, 1, 1 ),
				CPPPercent: 80m
			) ) );
			Assert.That( members[1].MemberId.Value, Is.EqualTo( 2 ) );
			Assert.That( members[1].DeathDate, Is.EqualTo( new DateOnly( 2072, 6, 30 ) ) );
			// Per decision D002 each member retires at their own age: Tina retires at 57 (2034-07-01),
			// distinct from Todd's retirement at 60 (2034-01-01).
			Assert.That( members[1].RetirementDate, Is.EqualTo( new DateOnly( 2034, 7, 1 ) ) );
			Assert.That( members[1].CPPStartDate, Is.EqualTo( new DateOnly( 2047, 7, 1 ) ) );
			Assert.That( members[1].OASStartDate, Is.EqualTo( new DateOnly( 2042, 7, 1 ) ) );
			Assert.That( periods, Has.Length.EqualTo( 552 ) );
			Assert.That( periods[0], Is.EqualTo( new CompiledPeriod( 1, new DateOnly( 2026, 7, 1 ) ) ) );
			Assert.That( periods[^1], Is.EqualTo( new CompiledPeriod( 552, new DateOnly( 2072, 6, 1 ) ) ) );
		} );
	}

	[Test]
	public void Compile_BenefitStartMonth_CompilesExpectedIncome() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2044, 1, 1 ),
			lifeInsurance: []
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledPeriod firstPeriod = compiledPlan.Periods.First();
		CompiledIncome[] income = [.. compiledPlan.Income[firstPeriod]];

		Assert.Multiple( () => {
			Assert.That( income.Single( i => i.MemberId == 1 && i.Name == "CPP" ).Amount, Is.EqualTo( 1206.120m ) );
			Assert.That( income.Single( i => i.MemberId == 1 && i.Name == "OAS" ).Amount, Is.EqualTo( 743.05m ) );
			Assert.That( income.Single( i => i.MemberId == 1 && i.Name == "CPP Survivor" ).Amount, Is.Zero );
			Assert.That( income.Single( i => i.MemberId == 2 && i.Name == "CPP" ).Amount, Is.Zero );
			Assert.That( income.Single( i => i.MemberId == 2 && i.Name == "OAS" ).Amount, Is.EqualTo( 743.05m ) );
		} );
	}

	[Test]
	public void Compile_MonthAfterPartnerDeath_CompilesExpectedSurvivorIncome() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2058, 12, 1 ),
			lifeInsurance: []
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledPeriod january = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2059, 1, 1 ) );
		CompiledIncome survivorIncome = compiledPlan.Income[january]
			.Single( i => i.MemberId == 2 && i.Name == "CPP Survivor" );

		Assert.That( survivorIncome.Amount, Is.EqualTo( 742.487472m ) );
	}

	[Test]
	public void Compile_Contributions_CharacterizesStartInflationAndRetirementStop() {
		CompiledPlan compiledPlan = new PlanCompiler().Compile( TestPlanFactory.Create() );
		CompiledPeriod firstPeriod = compiledPlan.Periods.First();
		CompiledPeriod january2027 = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2027, 1, 1 ) );
		CompiledPeriod retirement = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2034, 1, 1 ) );

		Assert.Multiple( () => {
			Assert.That( compiledPlan.Contribution[firstPeriod].Single( c => c.AssetId == 1 ).Amount, Is.EqualTo( 3200m ) );
			Assert.That( compiledPlan.Contribution[firstPeriod].Single( c => c.AssetId == 2 ).Amount, Is.Zero );
			Assert.That( compiledPlan.Contribution[january2027].Single( c => c.AssetId == 1 ).Amount, Is.EqualTo( 3283.2m ) );
			Assert.That( compiledPlan.Contribution[retirement].All( c => c.Amount == 0m ), Is.True );
		} );
	}

	[Test]
	public void Compile_PlanStartingAfterMemberDeath_CompilesRemainingPeriods() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2060, 1, 1 ),
			lifeInsurance: []
		);

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledPeriod[] periods = [.. compiledPlan.Periods];

		Assert.Multiple( () => {
			Assert.That( periods[0], Is.EqualTo( new CompiledPeriod( 1, new DateOnly( 2060, 1, 1 ) ) ) );
			Assert.That( periods[^1].PeriodDate, Is.EqualTo( new DateOnly( 2072, 6, 1 ) ) );
			// Todd (member 1) died 2058-12; his CPP is zero for every compiled period.
			Assert.That(
				periods.All( p => compiledPlan.Income[p].Single( i => i.MemberId == 1 && i.Name == "CPP" ).Amount == 0m ),
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
			lifeInsurance: []
		);

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledPeriod beforeStart = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2043, 12, 1 ) );
		CompiledPeriod atStart = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2044, 1, 1 ) );
		CompiledPeriod afterDeath = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2059, 1, 1 ) );

		Assert.Multiple( () => {
			Assert.That( compiledPlan.Income[beforeStart].Single( i => i.MemberId == 1 && i.Name == "CPP" ).Amount, Is.Zero );
			Assert.That( compiledPlan.Income[atStart].Single( i => i.MemberId == 1 && i.Name == "CPP" ).Amount, Is.EqualTo( 1206.12m ) );
			Assert.That( compiledPlan.Income[afterDeath].Single( i => i.MemberId == 1 && i.Name == "CPP" ).Amount, Is.Zero );
		} );
	}

	[Test]
	public void Compile_CPP_InflatesEachDecember() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2044, 1, 1 ),
			lifeInsurance: []
		);

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledPeriod january2044 = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2044, 1, 1 ) );
		CompiledPeriod december2044 = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2044, 12, 1 ) );
		CompiledPeriod january2045 = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2045, 1, 1 ) );

		Assert.Multiple( () => {
			Assert.That( compiledPlan.Income[january2044].Single( i => i.MemberId == 1 && i.Name == "CPP" ).Amount, Is.EqualTo( 1206.12m ) );
			Assert.That( compiledPlan.Income[december2044].Single( i => i.MemberId == 1 && i.Name == "CPP" ).Amount, Is.EqualTo( 1206.12m ) );
			Assert.That( compiledPlan.Income[january2045].Single( i => i.MemberId == 1 && i.Name == "CPP" ).Amount, Is.EqualTo( 1237.47912m ) );
		} );
	}

	[Test]
	public void Compile_OAS_StartsAtExpectedAge() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2038, 12, 1 ),
			annualInflationPercent: 0m,
			lifeInsurance: []
		);

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledPeriod beforeStart = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2038, 12, 1 ) );
		CompiledPeriod atStart = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2039, 1, 1 ) );

		Assert.Multiple( () => {
			Assert.That( compiledPlan.Income[beforeStart].Single( i => i.MemberId == 1 && i.Name == "OAS" ).Amount, Is.Zero );
			Assert.That( compiledPlan.Income[atStart].Single( i => i.MemberId == 1 && i.Name == "OAS" ).Amount, Is.EqualTo( 743.05m ) );
		} );
	}

	[Test]
	public void Compile_OAS_IncreasesAtAge75() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2039, 1, 1 ),
			annualInflationPercent: 0m,
			lifeInsurance: []
		);

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		// Todd born 1973-12-25 turns 75 on 2048-12-25.
		CompiledPeriod beforeSeventyFive = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2048, 12, 1 ) );
		CompiledPeriod afterSeventyFive = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2049, 1, 1 ) );

		Assert.Multiple( () => {
			Assert.That( compiledPlan.Income[beforeSeventyFive].Single( i => i.MemberId == 1 && i.Name == "OAS" ).Amount, Is.EqualTo( 743.05m ) );
			Assert.That( compiledPlan.Income[afterSeventyFive].Single( i => i.MemberId == 1 && i.Name == "OAS" ).Amount, Is.EqualTo( 817.355m ) );
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
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 100m )
			],
			lifeInsurance: [],
			contributions: []
		);

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledPeriod afterToddDeath = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2025, 2, 1 ) );
		CompiledIncome survivorIncome = compiledPlan.Income[afterToddDeath]
			.Single( i => i.MemberId == 2 && i.Name == "CPP Survivor" );

		// Survivor already receives the full CPP (1507.65); the partner top-up is capped
		// by the combined survivor maximum (1531.56), leaving 23.91.
		Assert.That( survivorIncome.Amount, Is.EqualTo( 23.91m ) );
	}

	[Test]
	public void Compile_LifeInsurance_PaidExactlyOnce() {
		CompiledPlan compiledPlan = new PlanCompiler().Compile( TestPlanFactory.Create() );

		CompiledIncome[] toddInsurance = [..
			compiledPlan.Periods
				.SelectMany( p => compiledPlan.Income[p] )
				.Where( i => i.Name == "Todd Life Insurance" && i.Amount != 0m )
		];

		Assert.Multiple( () => {
			Assert.That( toddInsurance, Has.Length.EqualTo( 1 ) );
			Assert.That( toddInsurance[0].Amount, Is.EqualTo( 250_000m ) );
			// Todd born 1973-12-25 with target age 85 dies in December 2058.
			CompiledPeriod deathPeriod = compiledPlan.Periods.Single( p => p.PeriodDate == new DateOnly( 2058, 12, 1 ) );
			Assert.That( compiledPlan.Income[deathPeriod].Single( i => i.Name == "Todd Life Insurance" ).Amount, Is.EqualTo( 250_000m ) );
		} );
	}

	[Test]
	public void Compile_Income_ClassifiesTaxableAndNonTaxable() {
		CompiledPlan compiledPlan = new PlanCompiler().Compile( TestPlanFactory.Create() );
		CompiledPeriod firstPeriod = compiledPlan.Periods.First();
		CompiledIncome[] income = [.. compiledPlan.Income[firstPeriod]];

		Assert.Multiple( () => {
			Assert.That( income.First( i => i.Name == "CPP" ).Taxable, Is.True );
			Assert.That( income.First( i => i.Name == "OAS" ).Taxable, Is.True );
			Assert.That( income.First( i => i.Name == "CPP Survivor" ).Taxable, Is.True );
			Assert.That( income.First( i => i.Name == "Todd Life Insurance" ).Taxable, Is.False );
		} );
	}

	[Test]
	public void Compile_RetirementIncome_CharacterizesPhaseBoundaries() {
		Plan plan = TestPlanFactory.Create( annualInflationPercent: 0m );

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		Assert.Multiple( () => {
			Assert.That( RetirementIncomeAt( compiledPlan, new DateOnly( 2033, 12, 1 ) ), Is.Zero );
			Assert.That( RetirementIncomeAt( compiledPlan, new DateOnly( 2034, 1, 1 ) ), Is.EqualTo( 7000m ) );
			Assert.That( RetirementIncomeAt( compiledPlan, new DateOnly( 2052, 4, 1 ) ), Is.EqualTo( 7000m ) );
			Assert.That( RetirementIncomeAt( compiledPlan, new DateOnly( 2052, 5, 1 ) ), Is.EqualTo( 6500m ) );
			Assert.That( RetirementIncomeAt( compiledPlan, new DateOnly( 2062, 5, 1 ) ), Is.EqualTo( 6500m ) );
			Assert.That( RetirementIncomeAt( compiledPlan, new DateOnly( 2062, 6, 1 ) ), Is.EqualTo( 6000m ) );
			Assert.That( compiledPlan.RetirementIncome[compiledPlan.Periods.Last()], Is.EqualTo( 6000m ) );
		} );
	}

	[Test]
	public void Compile_RetirementIncome_InflatesEachDecember() {
		CompiledPlan compiledPlan = new PlanCompiler().Compile( TestPlanFactory.Create() );

		decimal january2034 = RetirementIncomeAt( compiledPlan, new DateOnly( 2034, 1, 1 ) );
		decimal november2034 = RetirementIncomeAt( compiledPlan, new DateOnly( 2034, 11, 1 ) );
		decimal january2035 = RetirementIncomeAt( compiledPlan, new DateOnly( 2035, 1, 1 ) );

		Assert.Multiple( () => {
			Assert.That( november2034, Is.EqualTo( january2034 ) );
			Assert.That( january2035, Is.EqualTo( january2034 * 1.026m ) );
		} );
	}

	[Test]
	public void Compile_RetirementIncome_PhaseDurationsExceedingPlanFillAllPeriods() {
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
		Assert.That( compiledPlan.RetirementIncome[compiledPlan.Periods.First()], Is.EqualTo( 6000m ) );
	}

	private static decimal RetirementIncomeAt(
		CompiledPlan compiledPlan,
		DateOnly periodDate
	) {
		CompiledPeriod period = compiledPlan.Periods.Single( p => p.PeriodDate == periodDate );
		return compiledPlan.RetirementIncome[period];
	}
}
