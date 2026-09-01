using System.Text.Json;
using System.Text.Json.Serialization;

using Planning.Compiler;
using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Identifiers;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Calculator.Tests;

public class RrifMinimumTests {

	private const string SpouseName = "Tina";

	private static readonly JsonSerializerOptions SampleOptions = new JsonSerializerOptions {
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		RespectRequiredConstructorParameters = true,
		Converters = { new JsonStringEnumConverter() }
	};

	[Test]
	public void Deserialize_SampleStyleTaxPolicy_BindsRrifMinimums() {
		// The CLI does not reject unknown JSON properties, so a name mismatch would be silently
		// ignored and the minimums would never reach the calculator. This pins the binding.
		const string json = """
		{
			"Year": 2024,
			"FederalBrackets": [ { "LowerBound": 0, "Rate": 15.0 } ],
			"ProvincialBrackets": [ { "LowerBound": 0, "Rate": 5.05 } ],
			"AllowPensionSplitting": false,
			"AgeAmountBase": 8790,
			"AgeAmountIncomeThreshold": 44325,
			"AgeAmountReductionRate": 15.0,
			"AgeAmountEligibilityAge": 65,
			"PensionIncomeAmount": 2000,
			"PensionIncomeEligibilityAge": 65,
			"OasClawbackThreshold": 90997,
			"OasClawbackRate": 15.0,
			"RRIFMinimums": [
				{ "Age": 71, "Percent": 5.28 },
				{ "Age": 95, "Percent": 20.00 }
			]
		}
		""";

		TaxPolicy policy = JsonSerializer.Deserialize<TaxPolicy>( json, SampleOptions )!;
		RrifMinimum[] minimums = policy.RrifMinimums?.ToArray() ?? [];

		Assert.Multiple( () => {
			Assert.That( minimums, Has.Length.EqualTo( 2 ) );
			Assert.That( minimums[0].Age, Is.EqualTo( 71 ) );
			Assert.That( minimums[0].Percent, Is.EqualTo( 5.28m ) );
			Assert.That( minimums[1].Percent, Is.EqualTo( 20.00m ) );
		} );
	}

	[Test]
	public void Compile_PlanWithRrifMinimums_CarriesThemThroughToTheCompiledPlan() {
		Plan plan = CreatePlan( rrifMinimums: [new RrifMinimum( 71, 5.28m )] );
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		Assert.That( compiledPlan.TaxPolicy.RrifMinimums?.Single().Percent, Is.EqualTo( 5.28m ) );
	}

	[Test]
	public void Calculate_MinimumExceedsDesiredIncome_ForcesTheExcessOutOfTheTaxableAccount() {
		// Desired income is tiny relative to the RRSP, so the minimum is the binding constraint.
		CalculatedPlan withMinimum = Calculate( rrifMinimums: [new RrifMinimum( 71, 10m )] );
		CalculatedPlan withoutMinimum = Calculate( rrifMinimums: [] );

		decimal forced = withMinimum.Periods.Sum( p => p.RrifMinimumWithdrawal );

		Assert.Multiple( () => {
			Assert.That( forced, Is.GreaterThan( 0m ) );
			Assert.That( withoutMinimum.Periods.Sum( p => p.RrifMinimumWithdrawal ), Is.Zero );
		} );
	}

	[Test]
	public void Calculate_ForcedWithdrawalOccurs_MovesTheExcessIntoShelterRatherThanSpendingIt() {
		CalculatedPlan calculatedPlan = Calculate( rrifMinimums: [new RrifMinimum( 71, 10m )] );

		CalculatedPeriod period = calculatedPlan.Periods
			.First( p => p.RrifMinimumWithdrawal > 0m );

		// Every forced dollar has somewhere to go here: the TFSA has room and the non-registered
		// account is uncapped, so nothing should be left uninvested.
		Assert.That( period.RrifMinimumTransfer, Is.EqualTo( period.RrifMinimumWithdrawal ).Within( 0.01m ) );
	}

	[Test]
	public void Calculate_OwnersTfsaIsFull_UsesTheLivingSpousesRoomBeforeATaxableAccount() {
		// The owner's TFSA is capped well below the forced amount while the spouse has ample
		// room, so a correct cascade must reach across to the spouse rather than spilling
		// straight into the taxable non-registered account.
		Plan plan = CreatePlan(
			rrifMinimums: [new RrifMinimum( 71, 10m )],
			ownerTfsaBacklog: 1000m,
			spouseTfsaBacklog: 500_000m );
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );

		CompiledMember spouse = compiledPlan.Members.Single( m => m.Name == SpouseName );
		AssetId spouseTfsaId = compiledPlan.Assets
			.Single( a => a.MemberId == spouse.MemberId && a.TaxStatus == AssetTaxStatus.TaxExempt )
			.AssetId;

		CalculatedPeriod period = calculatedPlan.Periods
			.First( p => p.RrifMinimumWithdrawal > 0m );

		CalculatedAsset spouseTfsa = period.EndingAssets.Single( a => a.AssetId == spouseTfsaId );

		Assert.Multiple( () => {
			Assert.That( period.RrifMinimumTransfer, Is.EqualTo( period.RrifMinimumWithdrawal ).Within( 0.01m ) );
			Assert.That( spouseTfsa.Amount, Is.GreaterThan( 0m ) );
		} );
	}

	[Test]
	public void Calculate_ForcedExcessMovesIntoATfsa_NeverExceedsThatAccountsContributionRoom() {
		Plan plan = CreatePlan(
			rrifMinimums: [new RrifMinimum( 71, 10m )],
			ownerTfsaBacklog: 1000m,
			spouseTfsaBacklog: 2000m );
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );

		HashSet<AssetId> tfsaIds = [ .. compiledPlan.Assets
			.Where( a => a.TaxStatus == AssetTaxStatus.TaxExempt )
			.Select( a => a.AssetId ) ];

		// Contribution room is a hard legal cap, so it must never be driven negative no matter
		// how large the forced withdrawal is.
		foreach( CalculatedPeriod period in calculatedPlan.Periods ) {
			foreach( CalculatedAsset asset in period.EndingAssets.Where( a => tfsaIds.Contains( a.AssetId ) ) ) {
				Assert.That(
					asset.ContributionBacklog,
					Is.GreaterThanOrEqualTo( 0m ),
					$"TFSA room went negative in {period.PeriodDate:MMM yyyy}." );
			}
		}
	}

	[Test]
	public void Calculate_ForcedWithdrawalOccurs_IsTaxedAsIncomeInThatYear() {
		CalculatedPlan withMinimum = Calculate( rrifMinimums: [new RrifMinimum( 71, 10m )] );
		CalculatedPlan withoutMinimum = Calculate( rrifMinimums: [] );

		// Forcing income out of the RRIF earlier must raise lifetime tax; if it did not, the
		// forced withdrawal was escaping taxation.
		Assert.That(
			withMinimum.TaxSummary.TotalTax,
			Is.GreaterThan( withoutMinimum.TaxSummary.TotalTax ) );
	}

	[Test]
	public void Calculate_WithdrawalsAlreadyExceedTheMinimum_ForcesNothingFurther() {
		// A high desired income forces large voluntary withdrawals, which already satisfy a
		// negligible 0.01% factor, so the minimum should never bind.
		CalculatedPlan calculatedPlan = Calculate(
			rrifMinimums: [new RrifMinimum( 71, 0.01m )],
			desiredMonthlyIncome: 8_000m );

		Assert.That( calculatedPlan.Periods.Sum( p => p.RrifMinimumWithdrawal ), Is.Zero );
	}

	[Test]
	public void Calculate_NoRrifMinimumsConfigured_BehavesExactlyAsBefore() {
		CalculatedPlan withEmpty = Calculate( rrifMinimums: [] );
		CalculatedPlan withNull = Calculate( rrifMinimums: null );

		Assert.Multiple( () => {
			Assert.That( withNull.TaxSummary.TotalTax, Is.EqualTo( withEmpty.TaxSummary.TotalTax ) );
			Assert.That(
				withNull.Periods[^1].TotalAssets,
				Is.EqualTo( withEmpty.Periods[^1].TotalAssets ) );
		} );
	}

	private static CalculatedPlan Calculate(
		IEnumerable<RrifMinimum>? rrifMinimums,
		decimal desiredMonthlyIncome = 500m,
		decimal ownerTfsaBacklog = 100_000m,
		decimal spouseTfsaBacklog = 100_000m
	) {
		Plan plan = CreatePlan( rrifMinimums, desiredMonthlyIncome, ownerTfsaBacklog, spouseTfsaBacklog );
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		return new PlanCalculator().Calculate( plan, compiledPlan );
	}

	private static Plan CreatePlan(
		IEnumerable<RrifMinimum>? rrifMinimums,
		decimal desiredMonthlyIncome = 500m,
		decimal ownerTfsaBacklog = 100_000m,
		decimal spouseTfsaBacklog = 100_000m
	) {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				// Already past the mandatory conversion age at the plan start.
				new Member( "Todd", new DateOnly( 1950, 1, 1 ), 85, 60, 70, 90m ),
				new Member( "Tina", new DateOnly( 1951, 1, 1 ), 80, 60, 65, 50m )
			],
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 500_000m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0m, ownerTfsaBacklog, 7_000m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m, hasUnlimitedContributionRoom: true ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 0m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0m, spouseTfsaBacklog, 7_000m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m, hasUnlimitedContributionRoom: true )
			],
			annualInflationPercent: 0m,
			annualReturnPercent: 0m,
			lifeInsurance: [],
			retirementIncome: new RetirementIncome(
				GoGo: desiredMonthlyIncome,
				SlowGo: desiredMonthlyIncome,
				SlowGoYears: 0,
				NoGo: desiredMonthlyIncome,
				NoGoYears: 0
			),
			contributions: [],
			burndown: null
		);

		return plan with {
			TaxPolicy = plan.TaxPolicy with { RrifMinimums = rrifMinimums }
		};
	}
}
