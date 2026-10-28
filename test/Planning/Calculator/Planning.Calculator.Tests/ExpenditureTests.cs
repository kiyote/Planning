using Planning.Compiler;
using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Calculator.Tests;

public class ExpenditureTests {

	[Test]
	public void Calculate_Expense_IncreasesJanuarySpendingAndReducesEstateByTheFullAmount() {
		Plan plan = CreatePlan(
			expenditures: [
				new Expenditure( 5_000m, Indexed: false, Repeats: false, IntervalYears: null, StartYear: 2026, Name: "Roof" )
			],
			openingAssets: 10_000m,
			fundingStatus: AssetTaxStatus.TaxExempt,
			annualReturnPercent: 0m );

		CalculatedPlan result = Calculate( plan );
		CalculatedPlan baseline = Calculate( plan with { Expenditures = [] } );
		CalculatedPeriod january = result.Periods[0];

		using( Assert.EnterMultipleScope() ) {
			Assert.That( january.DesiredRetirementIncome, Is.EqualTo( 5_000m ) );
			Assert.That( january.RequestedWithdrawal, Is.EqualTo( 5_000m ) );
			Assert.That( january.ActualWithdrawal, Is.EqualTo( 5_000m ) );
			Assert.That( january.TotalIncome, Is.Zero );
			Assert.That( january.TotalAssets, Is.EqualTo( 5_000m ) );
			Assert.That( result.Periods.Skip( 1 ).Select( p => p.DesiredRetirementIncome ), Is.All.Zero );
			Assert.That( result.InsufficientFunds.HasShortfall, Is.False );
			Assert.That( result.TaxSummary.TotalTax, Is.Zero );
			Assert.That( baseline.EstateSummary.NetEstate - result.EstateSummary.NetEstate, Is.EqualTo( 5_000m ) );
		}
	}

	[Test]
	public void Calculate_Expense_IsWithdrawnBeforeJanuaryAssetGrowth() {
		Plan plan = CreatePlan(
			expenditures: [
				new Expenditure( 5_000m, Indexed: false, Repeats: false, IntervalYears: null, StartYear: 2026, Name: "Roof" )
			],
			openingAssets: 10_000m,
			fundingStatus: AssetTaxStatus.TaxExempt,
			annualReturnPercent: 6m );

		CalculatedPeriod january = Calculate( plan ).Periods[0];

		Assert.That( january.TotalAssets, Is.EqualTo( ( 10_000m - 5_000m ) * ( 1m + 0.06m / 12m ) ) );
	}

	[Test]
	public void Calculate_ExpenseFundedFromTaxableAssets_UsesNormalAnnualTaxSettlement() {
		Plan plan = CreatePlan(
			expenditures: [
				new Expenditure( 10_000m, Indexed: false, Repeats: false, IntervalYears: null, StartYear: 2026, Name: "Vehicle" )
			],
			openingAssets: 100_000m,
			fundingStatus: AssetTaxStatus.Taxable,
			annualReturnPercent: 0m );

		CalculatedPlan result = Calculate( plan );
		CalculatedPeriod december = result.Periods.Single( p => p.PeriodDate == new DateOnly( 2026, 12, 1 ) );

		using( Assert.EnterMultipleScope() ) {
			Assert.That( result.Periods[0].ActualWithdrawal, Is.EqualTo( 10_000m ) );
			Assert.That( december.TotalTax, Is.EqualTo( 10_000m * 0.20m ) );
			Assert.That( december.TaxFundingWithdrawal, Is.EqualTo( 2_000m ) );
			Assert.That( december.UnfundedTax, Is.Zero );
			Assert.That( result.InsufficientFunds.HasShortfall, Is.False );
		}
	}

	[Test]
	public void Calculate_UnfundedExpense_ReportsTheMissingAmountAsAShortfall() {
		Plan plan = CreatePlan(
			expenditures: [
				new Expenditure( 15_000m, Indexed: false, Repeats: false, IntervalYears: null, StartYear: 2026, Name: "Vehicle" )
			],
			openingAssets: 10_000m,
			fundingStatus: AssetTaxStatus.TaxExempt,
			annualReturnPercent: 0m );

		CalculatedPlan result = Calculate( plan );

		using( Assert.EnterMultipleScope() ) {
			Assert.That( result.Periods[0].RequestedWithdrawal, Is.EqualTo( 15_000m ) );
			Assert.That( result.Periods[0].ActualWithdrawal, Is.EqualTo( 10_000m ) );
			Assert.That( result.Periods[0].UnfundedShortfall, Is.EqualTo( 5_000m ) );
			Assert.That( result.Periods[0].PlanExhausted, Is.True );
			Assert.That( result.InsufficientFunds.HasShortfall, Is.True );
			Assert.That( result.InsufficientFunds.FirstShortfallDate, Is.EqualTo( new DateOnly( 2026, 1, 1 ) ) );
			Assert.That( result.InsufficientFunds.ShortfallPeriodCount, Is.EqualTo( 1 ) );
			Assert.That( result.InsufficientFunds.TotalUnfundedShortfall, Is.EqualTo( 5_000m ) );
		}
	}

	[Test]
	public void Calculate_ExpenditureEvents_MatchTheCompiledOccurrencesAndRemainOrdered() {
		Plan plan = CreatePlan(
			expenditures: [
				new Expenditure( 1_000m, Indexed: true, Repeats: true, IntervalYears: 3, StartYear: 2020, Name: "Vehicle" ),
				new Expenditure( 2_000m, Indexed: false, Repeats: false, IntervalYears: null, StartYear: 2026, Name: "Roof" ),
				new Expenditure( 0m, Indexed: false, Repeats: false, IntervalYears: null, StartYear: 2026, Name: "Unused" ),
				new Expenditure( 1_000m, Indexed: false, Repeats: false, IntervalYears: null, StartYear: 2025, Name: "Paid" )
			],
			openingAssets: 10_000m,
			fundingStatus: AssetTaxStatus.TaxExempt,
			annualReturnPercent: 0m );
		CompiledPlan compiled = new PlanCompiler().Compile( plan );

		CalculatedPlan result = new PlanCalculator().Calculate( plan, compiled );

		using( Assert.EnterMultipleScope() ) {
			Assert.That( result.Events.Where( e => e.Kind == PlanEventKind.Expenditure ), Is.EqualTo(
				compiled.Expenditures.Select( e => new PlanEvent( e.Period.PeriodDate, e.Name, PlanEventKind.Expenditure ) ) ) );
			Assert.That( result.Events.Count( e => e.Kind == PlanEventKind.Expenditure ), Is.EqualTo( 3 ) );
			Assert.That( result.Events, Is.Ordered.By( nameof( PlanEvent.Date ) ) );
		}
	}

	[Test]
	public void Calculate_ZeroExpense_PreservesProjectionAndAddsNoEvents() {
		Plan plan = CreatePlan(
			expenditures: [],
			openingAssets: 10_000m,
			fundingStatus: AssetTaxStatus.TaxExempt,
			annualReturnPercent: 6m );
		CalculatedPlan baseline = Calculate( plan );

		CalculatedPlan result = Calculate( plan with { Expenditures = [
			new Expenditure( 0m, Indexed: true, Repeats: true, IntervalYears: 1, StartYear: 2020, Name: "Unused" )
		] } );

		using( Assert.EnterMultipleScope() ) {
			Assert.That(
				result.Periods.Select( p => (p.PeriodDate, p.DesiredRetirementIncome, p.TotalAssets, p.TotalTax) ),
				Is.EqualTo( baseline.Periods.Select( p => (p.PeriodDate, p.DesiredRetirementIncome, p.TotalAssets, p.TotalTax) ) ) );
			Assert.That( result.EstateSummary, Is.EqualTo( baseline.EstateSummary ) );
			Assert.That( result.Events, Is.EqualTo( baseline.Events ) );
		}
	}

	private static Plan CreatePlan(
		IEnumerable<Expenditure> expenditures,
		decimal openingAssets,
		AssetTaxStatus fundingStatus,
		decimal annualReturnPercent
	) {
		Asset[] assets = [
			.. new[] { "Todd", "Tina" }.SelectMany( member =>
				new[] { AssetTaxStatus.Taxable, AssetTaxStatus.TaxExempt, AssetTaxStatus.CapitalGains }
					.Select( status => TestPlanFactory.CreateAsset(
						status.ToString(), status, member,
						member == "Todd" && status == fundingStatus ? openingAssets : 0m,
						hasUnlimitedContributionRoom: status == AssetTaxStatus.CapitalGains ) ) )
		];

		return TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				new Member( "Todd", new DateOnly( 1970, 1, 1 ), 60, 55, 70, 0m, 65, 0m, 0m ),
				new Member( "Tina", new DateOnly( 1970, 1, 1 ), 60, 55, 70, 0m, 65, 0m, 0m )
			],
			cppMaximum: 0m,
			cppCombinedSurvivorMaximum: 0m,
			oasMaximum: 0m,
			assets: assets,
			annualInflationPercent: 0m,
			annualReturnPercent: annualReturnPercent,
			lifeInsurance: [],
			retirementIncome: RetirementIncome.None,
			contributions: [],
			taxPolicy: TaxPolicy.None with { Year = 2026, FederalBrackets = [new TaxBracket( 0m, 20m )] },
			expenditures: expenditures );
	}

	private static CalculatedPlan Calculate(
		Plan plan
	) {
		return new PlanCalculator().Calculate( plan, new PlanCompiler().Compile( plan ) );
	}
}
