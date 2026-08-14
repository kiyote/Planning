using Planning.Calculator;
using Planning.Compiler;
using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Identifiers;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Reporter.Tests;

public class PlanReporterTests {

    [Test]
	[SetCulture( "en-US" )]
	public void WriteCompiledToCSV_WritesExpectedContent() {
		Plan plan = CreateReportPlan();
		CompiledPlan compiledPlan = CompilePlan( plan );
		using StringWriter writer = new StringWriter();

		new PlanReporter().WriteToCsv(
			writer,
			compiledPlan
		);

		string[] lines = writer.ToString()
			.Split( Environment.NewLine )
			.Where( l => l.Length > 0 )
			.ToArray();

		Assert.Multiple( () => {
			Assert.That(
				lines[0],
				Is.EqualTo( "Period,CPP (Todd),OAS (Todd),CPP Survivor (Todd),Todd Life Insurance (Todd),CPP (Tina),OAS (Tina),CPP Survivor (Tina),Tina Life Insurance (Tina),Todd Contribution,Tina Contribution,Retirement Income" ) );
			Assert.That(
				lines[1],
				Is.EqualTo( "Jan 2026,0.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,3500.00,0.00,0.00" ) );
		} );
	}

	[Test]
	[SetCulture( "en-US" )]
	public void WriteCalculatedToCSV_WritesExpectedContent() {
		Plan plan = CreateReportPlan();
		CompiledPlan compiledPlan = CompilePlan( plan );
		CalculatedPlan calculatedPlan = CalculatePlan( plan, compiledPlan );
		using StringWriter writer = new StringWriter();

		new PlanReporter().WriteToCsv(
			writer,
			compiledPlan,
			calculatedPlan
		);

		string output = writer.ToString();
		string[] lines = output.Split( Environment.NewLine );

		Assert.Multiple( () => {
			Assert.That(
				lines[0],
				Is.EqualTo( "Period,RRSP (Todd) [Start],TFSA (Todd) [Start],RRSP (Tina) [Start],TFSA (Tina) [Start],CapitalGains (Todd) [Start],CapitalGains (Tina) [Start],CPP (Todd),OAS (Todd),CPP Survivor (Todd),CPP (Tina),OAS (Tina),CPP Survivor (Tina),Todd Life Insurance (Todd),Tina Life Insurance (Tina),Total Taxable Income,Total Non-Taxable Income,Total Income,Retirement Income,Shortfall,Actual Retirement Income,Requested Withdrawal,Actual Withdrawal,Unfunded Shortfall,Plan Exhausted,RRSP (Todd) Withdrawl,CapitalGains (Todd) Withdrawl,TFSA (Todd) Withdrawl,RRSP (Tina) Withdrawl,CapitalGains (Tina) Withdrawl,TFSA (Tina) Withdrawl,RRSP (Todd) Contribution,TFSA (Todd) Contribution,RRSP (Tina) Contribution,TFSA (Tina) Contribution,CapitalGains (Todd) Contribution,CapitalGains (Tina) Contribution,RRSP (Todd) [End],TFSA (Todd) [End],RRSP (Tina) [End],TFSA (Tina) [End],CapitalGains (Todd) [End],CapitalGains (Tina) [End],RRSP (Todd) Backlog,TFSA (Todd) Backlog,RRSP (Tina) Backlog,TFSA (Tina) Backlog,CapitalGains (Todd) Backlog,CapitalGains (Tina) Backlog,Total Assets,Total Tax,Tax Funding Withdrawal,Unfunded Tax,Burndown Withdrawal,Burndown Tax,Burndown Transfer,RRIF Minimum Withdrawal,RRIF Minimum Transfer" ) );
			Assert.That(
				lines[1],
				Is.EqualTo( "Jan 2026,550000.00,0.00,30000.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00,False,0.00,0.00,0.00,0.00,0.00,0.00,3500.00,0.00,0.00,0.00,0.00,0.00,555791.67,0.00,30125.00,0.00,0.00,0.00,215581.00,109000.00,147614.00,109000.00,0.00,0.00,585916.67,0.00,0.00,0.00,0.00,0.00,0.00,0.00,0.00" ) );
			Assert.That( output, Does.Contain(
				string.Join(
					Environment.NewLine,
					"Insufficient Funds Summary",
					"Has Shortfall,True",
					"First Shortfall Date,Jan 2053",
					"First Shortfall Period,325",
					"Shortfall Period Count,199",
					"Total Unfunded Shortfall,2134841.89" ) ) );
			Assert.That( output, Does.Contain(
				string.Join(
					Environment.NewLine,
					"Tax Summary",
					"Total Federal Tax,655617.48",
					"Total Provincial Tax,286152.95",
					"Total Tax,941770.43" ) ) );
		} );
	}

	[Test]
	[SetCulture( "en-US" )]
	public void WriteCalculatedToCsv_EscapesSpecialCharactersInNames() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				new Member( "Todd, \"TJ\"", new DateOnly( 1976, 1, 15 ), 50, 40, 70, 80m ),
				new Member( "Tina", new DateOnly( 1976, 3, 15 ), 50, 40, 70, 50m )
			],
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd, \"TJ\"", 100m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 50m )
			],
			annualInflationPercent: 0m,
			annualReturnPercent: 0m,
			lifeInsurance: [],
			retirementIncome: new RetirementIncome(
				GoGo: 20m,
				SlowGo: 20m,
				SlowGoYears: 0,
				NoGo: 20m,
				NoGoYears: 0
			),
			contributions: []
		);
		CompiledPlan compiledPlan = CompilePlan( plan );
		CalculatedPlan calculatedPlan = CalculatePlan( plan, compiledPlan );
		using StringWriter writer = new StringWriter();

		new PlanReporter().WriteToCsv(
			writer,
			compiledPlan,
			calculatedPlan
		);

		string header = new StringReader( writer.ToString() ).ReadLine()!;

		Assert.That( header, Does.Contain( "\"RRSP (Todd, \"\"TJ\"\") [Start]\"" ) );
	}

	[Test]
	[SetCulture( "en-US" )]
	public void WriteCalculatedToCsv_EmptyPeriods_WritesHeaderOnly() {
		using StringWriter writer = new StringWriter();

		new PlanReporter().WriteToCsv(
			writer,
			CompilePlan( CreateReportPlan() ),
			new CalculatedPlan(
				[],
				new InsufficientFundsSummary( false, null, null, 0, 0m ),
				new TaxSummary( 0m, 0m, 0m ),
				new EstateSummary( 0m, 0m, 2026, 2026, 0m ),
				[],
				new RetirementIncome( 0m, 0m, 0, 0m, 0 )
			)
		);

		string expected = string.Join(
			Environment.NewLine,
			"Period",
			string.Empty,
			"Insufficient Funds Summary",
			"Has Shortfall,False",
			"First Shortfall Date,",
			"First Shortfall Period,",
			"Shortfall Period Count,0",
			"Total Unfunded Shortfall,0.00",
			string.Empty,
			"Tax Summary",
			"Total Federal Tax,0.00",
			"Total Provincial Tax,0.00",
			"Total Tax,0.00",
			"Terminal Federal Tax,0.00",
			"Terminal Provincial Tax,0.00",
			"Terminal Tax,0.00",
			"Total Tax Including Terminal,0.00",
			string.Empty,
			"Estate Summary",
			"Gross Estate,0.00",
			"Terminal Tax,0.00",
			"Net Estate,0.00",
			"Net Estate (2026 Dollars),0.00",
			string.Empty
		);
		Assert.That( writer.ToString(), Is.EqualTo( expected ) );
	}

	[Test]
	[SetCulture( "en-US" )]
	public void WriteCalculatedToCsv_InsolventPlan_AppendsInsufficientFundsSummary() {
		using StringWriter writer = new StringWriter();

		new PlanReporter().WriteToCsv(
			writer,
			CompilePlan( CreateReportPlan() ),
			new CalculatedPlan(
				[],
				new InsufficientFundsSummary(
					true,
					new DateOnly( 2030, 5, 1 ),
					new PeriodNumber( 53 ),
					4,
					1234.5m
				),
				new TaxSummary( 0m, 0m, 0m ),
				new EstateSummary( 0m, 0m, 2026, 2026, 0m ),
				[],
				new RetirementIncome( 0m, 0m, 0, 0m, 0 )
			)
		);

		string output = writer.ToString();

		Assert.Multiple( () => {
			Assert.That( output, Does.Contain( "Insufficient Funds Summary" ) );
			Assert.That( output, Does.Contain( "Has Shortfall,True" ) );
			Assert.That( output, Does.Contain( "First Shortfall Date,May 2030" ) );
			Assert.That( output, Does.Contain( "First Shortfall Period,53" ) );
			Assert.That( output, Does.Contain( "Shortfall Period Count,4" ) );
			Assert.That( output, Does.Contain( "Total Unfunded Shortfall,1234.50" ) );
		} );
	}

	[Test]
	[SetCulture( "en-US" )]
	public void WriteCalculatedToCsv_ColumnOrderingIsStable() {
		Plan plan = CreateReportPlan();
		CompiledPlan compiledPlan = CompilePlan( plan );
		CalculatedPlan calculatedPlan = CalculatePlan( plan, compiledPlan );
		using StringWriter first = new StringWriter();
		using StringWriter second = new StringWriter();

		new PlanReporter().WriteToCsv( first, compiledPlan, calculatedPlan );
		new PlanReporter().WriteToCsv( second, compiledPlan, calculatedPlan );

		string firstHeader = new StringReader( first.ToString() ).ReadLine()!;
		string secondHeader = new StringReader( second.ToString() ).ReadLine()!;

		Assert.Multiple( () => {
			Assert.That( secondHeader, Is.EqualTo( firstHeader ) );
			Assert.That(
				firstHeader,
				Is.EqualTo(
					"Period,RRSP (Todd) [Start],TFSA (Todd) [Start],RRSP (Tina) [Start],TFSA (Tina) [Start],CapitalGains (Todd) [Start],CapitalGains (Tina) [Start],CPP (Todd),OAS (Todd),CPP Survivor (Todd),CPP (Tina),OAS (Tina),CPP Survivor (Tina),Todd Life Insurance (Todd),Tina Life Insurance (Tina),Total Taxable Income,Total Non-Taxable Income,Total Income,Retirement Income,Shortfall,Actual Retirement Income,Requested Withdrawal,Actual Withdrawal,Unfunded Shortfall,Plan Exhausted,RRSP (Todd) Withdrawl,CapitalGains (Todd) Withdrawl,TFSA (Todd) Withdrawl,RRSP (Tina) Withdrawl,CapitalGains (Tina) Withdrawl,TFSA (Tina) Withdrawl,RRSP (Todd) Contribution,TFSA (Todd) Contribution,RRSP (Tina) Contribution,TFSA (Tina) Contribution,CapitalGains (Todd) Contribution,CapitalGains (Tina) Contribution,RRSP (Todd) [End],TFSA (Todd) [End],RRSP (Tina) [End],TFSA (Tina) [End],CapitalGains (Todd) [End],CapitalGains (Tina) [End],RRSP (Todd) Backlog,TFSA (Todd) Backlog,RRSP (Tina) Backlog,TFSA (Tina) Backlog,CapitalGains (Todd) Backlog,CapitalGains (Tina) Backlog,Total Assets,Total Tax,Tax Funding Withdrawal,Unfunded Tax,Burndown Withdrawal,Burndown Tax,Burndown Transfer,RRIF Minimum Withdrawal,RRIF Minimum Transfer"
				)
			);
		} );
	}

	private static Plan CreateReportPlan() {
		return TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				new Member( "Todd", new DateOnly( 1973, 12, 31 ), 85, 60, 70, 80m ),
				new Member( "Tina", new DateOnly( 1976, 7, 22 ), 95, null, 65, 50m )
			],
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 550000m, 219_081m, 22_000m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0m, 109_000m, 7_000m ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 30000m, 147_614m, 10_800m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0m, 109_000m, 7_000m )
			],
			annualInflationPercent: 3m,
			annualReturnPercent: 6m,
			lifeInsurance: [
				new LifeInsurance( "Todd", 250000 ),
				new LifeInsurance( "Tina", 250000 )
			],
			retirementIncome: new RetirementIncome(
				GoGo: 7000m,
				SlowGo: 6000m,
				SlowGoYears: 10,
				NoGo: 6500m,
				NoGoYears: 10
			),
			contributions: [
				new Contribution( "Todd", 3500, 2026, Indexed: true ),
				new Contribution( "Tina", 3000, 2028, Indexed: true )
			]
		);
	}

	private static CompiledPlan CompilePlan(
		Plan plan
	) {
		return new PlanCompiler().Compile( plan );
	}

	private static CalculatedPlan CalculatePlan(
		Plan plan,
		CompiledPlan compiledPlan
	) {
		return new PlanCalculator().Calculate(
			plan,
			compiledPlan
		);
	}
}
