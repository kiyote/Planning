using Planning.Model.CompiledPlans;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Compiler.Tests;

public class ExpenditureTests {

	[TestCase( null )]
	[TestCase( 0 )]
	[TestCase( -1 )]
	public void Compile_RepeatingExpenditureWithoutPositiveInterval_ThrowsNamedValidationError(
		int? intervalYears
	) {
		Plan plan = TestPlanFactory.Create( expenditures: [
			new Expenditure( 50_000m, Indexed: true, Repeats: true, intervalYears, StartYear: 2020, Name: "Vehicle" )
		] );

		Assert.That(
			() => new PlanCompiler().Compile( plan ),
			Throws.TypeOf<PlanValidationException>().With.Message.Contains( "Vehicle" ).And.Message.Contains( "IntervalYears" ) );
	}

	[TestCase( null )]
	[TestCase( "" )]
	[TestCase( " " )]
	public void Validate_EmptyExpenditureName_ReportsError(
		string? name
	) {
		Plan plan = TestPlanFactory.Create( expenditures: [
			new Expenditure( 1m, Indexed: false, Repeats: false, IntervalYears: null, StartYear: 2035, Name: name! )
		] );

		Assert.That( new PlanValidator().Validate( plan ).Errors, Has.Some.Contains( "expenditure has an empty name" ) );
	}

	[Test]
	public void Validate_NegativeExpenditureAmount_ReportsNamedError() {
		Plan plan = TestPlanFactory.Create( expenditures: [
			new Expenditure( -1m, Indexed: false, Repeats: false, IntervalYears: null, StartYear: 2035, Name: "Roof" )
		] );

		Assert.That( new PlanValidator().Validate( plan ).Errors, Has.Some.Contains( "Expenditure 'Roof' amount (-1) must be nonnegative" ) );
	}

	[TestCase( 0 )]
	[TestCase( -1 )]
	[TestCase( 10000 )]
	public void Validate_InvalidExpenditureYear_ReportsNamedError(
		int year
	) {
		Plan plan = TestPlanFactory.Create( expenditures: [
			new Expenditure( 1m, Indexed: false, Repeats: false, IntervalYears: null, StartYear: year, Name: "Roof" )
		] );

		Assert.That( new PlanValidator().Validate( plan ).Errors, Has.Some.Contains( "Expenditure 'Roof' start year" ) );
	}

	[TestCase( 1 )]
	[TestCase( 9999 )]
	public void Validate_CalendarYearBounds_AreValid(
		int year
	) {
		Plan plan = TestPlanFactory.Create( expenditures: [
			new Expenditure( 1m, Indexed: false, Repeats: false, IntervalYears: null, StartYear: year, Name: "Roof" )
		] );

		Assert.That( new PlanValidator().Validate( plan ).IsValid, Is.True );
	}

	[Test]
	public void Validate_NullExpenditures_ReportsError() {
		Plan plan = TestPlanFactory.Create() with { Expenditures = null! };

		Assert.That( new PlanValidator().Validate( plan ).Errors, Has.Some.Contains( "must specify Expenditures" ) );
	}

	[Test]
	public void Validate_NullExpenditureEntry_ReportsError() {
		Plan plan = TestPlanFactory.Create( expenditures: [null!] );

		Assert.That( new PlanValidator().Validate( plan ).Errors, Has.Some.Contains( "expenditure must not be null" ) );
	}

	[TestCase( true )]
	[TestCase( false )]
	public void Compile_HistoricalRepeatingExpense_AddsFullAmountOnlyInDueJanuaries(
		bool indexed
	) {
		Plan plan = TestPlanFactory.Create(
			annualInflationPercent: 3m,
			expenditures: [
				new Expenditure( 50_000m, indexed, Repeats: true, IntervalYears: 15, StartYear: 2020, Name: "Vehicle" )
			] );
		CompiledPlan baseline = new PlanCompiler().Compile( plan with { Expenditures = [] } );

		CompiledPlan compiled = new PlanCompiler().Compile( plan );

		using( Assert.EnterMultipleScope() ) {
			Assert.That( compiled.Expenditures.Select( e => e.Period.PeriodDate ), Is.EqualTo( new[] {
				new DateOnly( 2035, 1, 1 ), new DateOnly( 2050, 1, 1 ), new DateOnly( 2065, 1, 1 )
			} ) );
			Assert.That( compiled.Expenditures.Select( e => e.Name ), Is.All.EqualTo( "Vehicle" ) );
			foreach( CompiledPeriod period in compiled.Periods ) {
				int year = period.PeriodDate.Year;
				bool isDue = period.PeriodDate.Month == 1 && ( year == 2035 || year == 2050 || year == 2065 );
				decimal expectedExtra = isDue
					? 50_000m * ( indexed ? (decimal)Math.Pow( 1.03, year - 2020 ) : 1m )
					: 0m;
				Assert.That( compiled.DesiredIncome[period] - baseline.DesiredIncome[period],
					Is.EqualTo( expectedExtra ).Within( 0.000001m ), period.PeriodDate.ToString() );
			}
		}
	}

	[Test]
	public void Compile_FutureIndexedExpense_StartYearAmountIsNotInflatedFromPlanStart() {
		Plan plan = TestPlanFactory.Create(
			annualInflationPercent: 3m,
			expenditures: [
				new Expenditure( 25_000m, Indexed: true, Repeats: false, IntervalYears: null, StartYear: 2045, Name: "Roof" )
			] );

		CompiledExpenditure expenditure = new PlanCompiler().Compile( plan ).Expenditures.Single();

		using( Assert.EnterMultipleScope() ) {
			Assert.That( expenditure.Period.PeriodDate, Is.EqualTo( new DateOnly( 2045, 1, 1 ) ) );
			Assert.That( expenditure.Amount, Is.EqualTo( 25_000m ) );
		}
	}

	[TestCase( 2020, 0 )]
	[TestCase( 2033, 0 )]
	[TestCase( 2034, 1 )]
	[TestCase( 2072, 1 )]
	[TestCase( 2073, 0 )]
	public void Compile_OneOffExpense_OnlyOccursWithinRetirementAndPlanWindow(
		int startYear,
		int expectedCount
	) {
		Plan plan = TestPlanFactory.Create( expenditures: [
			new Expenditure( 1_000m, Indexed: false, Repeats: false, IntervalYears: null, startYear, Name: "Roof" )
		] );

		IReadOnlyList<CompiledExpenditure> expenditures = new PlanCompiler().Compile( plan ).Expenditures;

		Assert.That( expenditures, Has.Count.EqualTo( expectedCount ) );
		if( expectedCount > 0 ) {
			Assert.That( expenditures.Single().Period.PeriodDate, Is.EqualTo( new DateOnly( startYear, 1, 1 ) ) );
		}
	}

	[TestCase( null )]
	[TestCase( 0 )]
	[TestCase( -1 )]
	[TestCase( 5 )]
	public void Compile_NonRepeatingExpense_IgnoresInterval(
		int? intervalYears
	) {
		Plan plan = TestPlanFactory.Create( expenditures: [
			new Expenditure( 1_000m, Indexed: false, Repeats: false, intervalYears, StartYear: 2035, Name: "Roof" )
		] );

		Assert.That( new PlanCompiler().Compile( plan ).Expenditures, Has.Count.EqualTo( 1 ) );
	}

	[TestCase( 1, 1, 2035 )]
	[TestCase( 1, 15, 2045 )]
	[TestCase( 7, 1, 2045 )]
	public void Compile_PlanStartAfterJanuary_SkipsPaidOccurrenceWithoutShiftingSchedule(
		int month,
		int day,
		int firstOccurrenceYear
	) {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2035, month, day ),
			taxPolicy: TestPlanFactory.CreateTaxPolicy() with { Year = 2035 },
			expenditures: [
				new Expenditure( 1_000m, Indexed: false, Repeats: true, IntervalYears: 10, StartYear: 2025, Name: "Vehicle" ),
				new Expenditure( 1_000m, Indexed: false, Repeats: false, IntervalYears: null, StartYear: 2034, Name: "Paid" )
			] );

		IReadOnlyList<CompiledExpenditure> expenditures = new PlanCompiler().Compile( plan ).Expenditures;

		using( Assert.EnterMultipleScope() ) {
			Assert.That( expenditures[0].Period.PeriodDate, Is.EqualTo( new DateOnly( firstOccurrenceYear, 1, 1 ) ) );
			Assert.That( expenditures.Select( e => e.Period.PeriodDate.Year ), Does.Not.Contain( 2034 ) );
			Assert.That( expenditures.Select( e => e.Name ), Is.All.EqualTo( "Vehicle" ) );
		}
	}

	[Test]
	public void Compile_MidYearRetirement_SkipsThatJanuaryWithoutShiftingSchedule() {
		Member[] members = TestPlanFactory.CreateMembers();
		// Tina retires in July 2034; Todd's retirement moves to January 2035.
		members[0] = members[0] with { RetirementAgeInYears = 61 };
		Plan plan = TestPlanFactory.Create(
			members: members,
			expenditures: [
				new Expenditure( 1_000m, Indexed: false, Repeats: true, IntervalYears: 2, StartYear: 2034, Name: "Vehicle" )
			] );

		CompiledPlan compiled = new PlanCompiler().Compile( plan );

		using( Assert.EnterMultipleScope() ) {
			Assert.That( compiled.RetirementPhaseSchedule.GoGoStart, Is.EqualTo( new DateOnly( 2034, 7, 1 ) ) );
			Assert.That( compiled.Expenditures[0].Period.PeriodDate, Is.EqualTo( new DateOnly( 2036, 1, 1 ) ) );
		}
	}

	[Test]
	public void Compile_DuplicateNamesAndSimultaneousExpenses_AreIndependentAndAddTogether() {
		Plan plan = TestPlanFactory.Create(
			annualInflationPercent: 0m,
			expenditures: [
				new Expenditure( 1_000m, Indexed: false, Repeats: false, IntervalYears: null, StartYear: 2034, Name: "Repairs" ),
				new Expenditure( 2_000m, Indexed: false, Repeats: false, IntervalYears: null, StartYear: 2034, Name: "Repairs" )
			] );

		CompiledPlan compiled = new PlanCompiler().Compile( plan );
		CompiledPeriod january = compiled.Periods.Single( p => p.PeriodDate == new DateOnly( 2034, 1, 1 ) );

		using( Assert.EnterMultipleScope() ) {
			Assert.That( compiled.Expenditures.Select( e => e.Amount ), Is.EqualTo( new[] { 1_000m, 2_000m } ) );
			Assert.That( compiled.DesiredIncome[january], Is.EqualTo( 7_000m + 1_000m + 2_000m ) );
		}
	}

	[TestCase( 1, 39 )]
	[TestCase( int.MaxValue, 1 )]
	public void Compile_PositiveIntervals_IncludeStartYearWithoutOverflow(
		int intervalYears,
		int expectedCount
	) {
		Plan plan = TestPlanFactory.Create( expenditures: [
			new Expenditure( 1m, Indexed: false, Repeats: true, intervalYears, StartYear: 2034, Name: "Maintenance" )
		] );

		Assert.That( new PlanCompiler().Compile( plan ).Expenditures, Has.Count.EqualTo( expectedCount ) );
	}

	[Test]
	public void Compile_ZeroExpenditure_IsANoOp() {
		Plan plan = TestPlanFactory.Create();
		CompiledPlan baseline = new PlanCompiler().Compile( plan );
		Plan withZero = plan with { Expenditures = [
			new Expenditure( 0m, Indexed: true, Repeats: true, IntervalYears: 1, StartYear: 2020, Name: "Unused" )
		] };

		CompiledPlan compiled = new PlanCompiler().Compile( withZero );

		using( Assert.EnterMultipleScope() ) {
			Assert.That( compiled.Expenditures, Is.Empty );
			Assert.That( compiled.DesiredIncome, Is.EqualTo( baseline.DesiredIncome ) );
		}
	}
}
