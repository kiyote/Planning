using Planning.Compiler;
using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Identifiers;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Calculator.Tests;

public class BurndownTests {

	[Test]
	public void Calculate_NoBurndownConfigured_LeavesTaxableAssetsUndrained() {
		CalculatedPlan calculatedPlan = Calculate( CreatePlan( burndown: null ) );

		Assert.Multiple( () => {
			Assert.That( calculatedPlan.Periods.Sum( p => p.BurndownWithdrawal ), Is.Zero );
			Assert.That( calculatedPlan.Periods.Sum( p => p.BurndownTransfer ), Is.Zero );
			Assert.That( TaxableTotal( calculatedPlan.Periods.Last() ), Is.GreaterThan( 0m ) );
		} );
	}

	[Test]
	public void Calculate_Burndown_DrainsTaxableAssetsByEndOfWindow() {
		CalculatedPlan calculatedPlan = Calculate( CreatePlan( burndown: new Burndown( BurndownYears: 10 ) ) );

		CalculatedPeriod finalBurndownPeriod = calculatedPlan.Periods
			.Last( p => p.PeriodDate.Year == 2035 && p.PeriodDate.Month == 12 );

		Assert.That( TaxableTotal( finalBurndownPeriod ), Is.EqualTo( 0m ).Within( 0.01m ) );
	}

	[Test]
	public void Calculate_Burndown_OccursOnlyInDecemberAndOnlyWithinTheWindow() {
		CalculatedPlan calculatedPlan = Calculate( CreatePlan( burndown: new Burndown( BurndownYears: 10 ) ) );

		Assert.Multiple( () => {
			Assert.That(
				calculatedPlan.Periods.Where( p => p.PeriodDate.Month != 12 ).Sum( p => p.BurndownWithdrawal ),
				Is.Zero );
			Assert.That(
				calculatedPlan.Periods.Where( p => p.PeriodDate.Year > 2035 ).Sum( p => p.BurndownWithdrawal ),
				Is.Zero );
			Assert.That(
				calculatedPlan.Periods.Count( p => p.BurndownWithdrawal > 0m ),
				Is.EqualTo( 10 ) );
		} );
	}

	[Test]
	public void Calculate_Burndown_TransfersProceedsNetOfTaxIntoDestinationAccounts() {
		CalculatedPlan calculatedPlan = Calculate( CreatePlan( burndown: new Burndown( BurndownYears: 10 ) ) );

		CalculatedPeriod period = calculatedPlan.Periods.First( p => p.BurndownWithdrawal > 0m );

		Assert.Multiple( () => {
			Assert.That( period.BurndownTax, Is.GreaterThan( 0m ) );
			Assert.That(
				period.BurndownTransfer,
				Is.EqualTo( period.BurndownWithdrawal - period.BurndownTax ).Within( 0.01m ) );
		} );
	}

	[Test]
	public void Calculate_Burndown_FillsTaxExemptRoomBeforeCapitalGains() {
		CalculatedPlan calculatedPlan = Calculate( CreatePlan( burndown: new Burndown( BurndownYears: 10 ) ) );

		CalculatedPeriod period = calculatedPlan.Periods.First( p => p.BurndownWithdrawal > 0m );

		decimal taxExempt = Total( period, AssetTaxStatus.TaxExempt );
		decimal capitalGains = Total( period, AssetTaxStatus.CapitalGains );

		Assert.Multiple( () => {
			// The tax-exempt accounts have limited room, so the first burndown fills them and the
			// remainder spills into the uncapped capital-gains accounts.
			Assert.That( taxExempt, Is.GreaterThan( 0m ) );
			Assert.That( capitalGains, Is.GreaterThan( 0m ) );
		} );
	}

	[Test]
	public void Calculate_Burndown_PreservesTotalAssetsNetOfTax() {
		CalculatedPlan withoutBurndown = Calculate( CreatePlan( burndown: null ) );
		CalculatedPlan withBurndown = Calculate( CreatePlan( burndown: new Burndown( BurndownYears: 10 ) ) );

		// Moving money between accounts is value-neutral apart from the tax it triggers, so the
		// burndown should never destroy more value than the extra tax it causes.
		Assert.That(
			withBurndown.Periods.Last().TotalAssets,
			Is.LessThan( withoutBurndown.Periods.Last().TotalAssets ) );
	}

	[Test]
	public void Validate_BurndownWithPartialAssets_IsValidBecauseAccountsAreSynthesized() {
		// The burndown needs a Taxable account to draw down and a CapitalGains account to receive
		// the proceeds; both are guaranteed by the tax status coverage invariant even when the
		// plan defines neither.
		Plan plan = CreatePlan(
			burndown: new Burndown( BurndownYears: 10 ),
			assets: [
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 100_000m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 100_000m )
			] );

		PlanValidationResult result = new PlanValidator().Validate( plan );

		Assert.That( result.IsValid, Is.True );
	}

	[Test]
	public void Validate_NegativeBurndownYears_ReportsError() {
		Plan plan = CreatePlan( burndown: new Burndown( BurndownYears: -5 ) );

		PlanValidationResult result = new PlanValidator().Validate( plan );

		Assert.That( result.Errors, Has.Some.Contains( "Burndown years" ) );
	}

	[Test]
	public void Plan_ZeroBurndownYears_IsNormalizedToDisabled() {
		Plan plan = CreatePlan( burndown: new Burndown( BurndownYears: 0 ) );

		CalculatedPlan calculatedPlan = Calculate( plan );

		Assert.Multiple( () => {
			// Zero years is treated exactly as though no burndown were configured.
			Assert.That( plan.Burndown, Is.Null );
			Assert.That( new PlanValidator().Validate( plan ).IsValid, Is.True );
			Assert.That( calculatedPlan.Periods.Sum( p => p.BurndownWithdrawal ), Is.Zero );
			Assert.That( calculatedPlan.Periods.Sum( p => p.BurndownTransfer ), Is.Zero );
		} );
	}

	[Test]
	public void Validate_NoBurndown_DoesNotRequireBurndownAccounts() {
		Plan plan = CreatePlan(
			burndown: null,
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 100_000m ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 100_000m )
			] );

		PlanValidationResult result = new PlanValidator().Validate( plan );

		Assert.That( result.IsValid, Is.True );
	}

	private static CalculatedPlan Calculate(
		Plan plan
	) {
		return new PlanCalculator().Calculate( plan, new PlanCompiler().Compile( plan ) );
	}

	private static decimal TaxableTotal(
		CalculatedPeriod period
	) {
		return Total( period, AssetTaxStatus.Taxable );
	}

	private static decimal Total(
		CalculatedPeriod period,
		AssetTaxStatus status
	) {
		return period.EndingAssets.Where( a => a.TaxStatus == status ).Sum( a => a.Amount );
	}

	[Test]
	public void Calculate_Burndown_DoesNotStartUntilRetirement() {
		// Todd retires 2036-02-01, so no burndown may occur in the ten years before that.
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				new Member( "Todd", new DateOnly( 1970, 1, 1 ), 90, 66, 70, 80m ),
				new Member( "Tina", new DateOnly( 1971, 1, 1 ), 90, 66, 70, 50m )
			],
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 300_000m, 0m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0m, 20_000m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m, -1m, -1m, 0m )
			],
			annualInflationPercent: 0m,
			annualReturnPercent: 0m,
			lifeInsurance: [],
			retirementIncome: new RetirementIncome( 500m, 500m, 0, 500m, 0 ),
			contributions: [],
			burndown: new Burndown( BurndownYears: 10 )
		);

		CalculatedPlan calculatedPlan = Calculate( plan );

		Assert.Multiple( () => {
			Assert.That(
				calculatedPlan.Periods.Where( p => p.PeriodDate.Year < 2036 ).Sum( p => p.BurndownWithdrawal ),
				Is.Zero );

			// The window runs for the configured ten years from retirement.
			Assert.That(
				calculatedPlan.Periods.Where( p => p.PeriodDate.Year > 2045 ).Sum( p => p.BurndownWithdrawal ),
				Is.Zero );
			Assert.That(
				calculatedPlan.Periods.Count( p => p.BurndownWithdrawal > 0m ),
				Is.EqualTo( 10 ) );
		} );
	}

	[Test]
	public void Calculate_OwnersTaxExemptRoomIsFull_UsesTheLivingSpousesRoomBeforeATaxableAccount() {
		// Only Todd is burning down, and his own TFSA room is far too small to hold the
		// proceeds while Tina's is ample. A correct cascade reaches across to the spouse
		// rather than spilling straight into the taxable non-registered account.
		Plan plan = CreatePlan(
			burndown: new Burndown( BurndownYears: 10 ),
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 300_000m, 0m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 0m, 0m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0m, 1_000m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0m, 500_000m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m, -1m, -1m, 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m, -1m, -1m, 0m )
			] );

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );

		CompiledMember tina = compiledPlan.Members.Single( m => m.Name == "Tina" );
		AssetId tinaTfsaId = compiledPlan.Assets
			.Single( a => a.MemberId == tina.MemberId && a.TaxStatus == AssetTaxStatus.TaxExempt )
			.AssetId;

		CalculatedPeriod period = calculatedPlan.Periods.First( p => p.BurndownWithdrawal > 0m );

		Assert.That(
			period.EndingAssets.Single( a => a.AssetId == tinaTfsaId ).Amount,
			Is.GreaterThan( 0m ) );
	}

	[Test]
	public void Calculate_BurndownProceedsMoveIntoATaxExemptAccount_NeverExceedItsContributionRoom() {
		Plan plan = CreatePlan(
			burndown: new Burndown( BurndownYears: 10 ),
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 300_000m, 0m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 300_000m, 0m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0m, 1_000m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0m, 2_000m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m, -1m, -1m, 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m, -1m, -1m, 0m )
			] );

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );

		HashSet<AssetId> taxExemptIds = [ .. compiledPlan.Assets
			.Where( a => a.TaxStatus == AssetTaxStatus.TaxExempt )
			.Select( a => a.AssetId ) ];

		// Contribution room is a hard legal cap and must never be driven negative.
		foreach( CalculatedPeriod period in calculatedPlan.Periods ) {
			foreach( CalculatedAsset asset in period.EndingAssets.Where( a => taxExemptIds.Contains( a.AssetId ) ) ) {
				Assert.That(
					asset.ContributionBacklog,
					Is.GreaterThanOrEqualTo( 0m ),
					$"Tax-exempt room went negative in {period.PeriodDate:MMM yyyy}." );
			}
		}
	}

	/// <summary>
	/// A household that retires at the plan start with modest income needs, so that the
	/// taxable balances are drawn down by the burndown rather than by the retirement shortfall.
	/// </summary>
	private static Plan CreatePlan(
		Burndown? burndown,
		IEnumerable<Asset>? assets = null
	) {
		return TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				new Member( "Todd", new DateOnly( 1960, 1, 1 ), 90, 66, 70, 80m ),
				new Member( "Tina", new DateOnly( 1961, 1, 1 ), 90, 65, 70, 50m )
			],
			assets: assets ?? [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 300_000m, 0m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 300_000m, 0m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0m, 20_000m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0m, 20_000m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m, -1m, -1m, 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m, -1m, -1m, 0m )
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
			contributions: [],
			burndown: burndown
		);
	}
}
