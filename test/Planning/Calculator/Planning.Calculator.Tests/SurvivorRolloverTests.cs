using Planning.Compiler;
using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Identifiers;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Calculator.Tests;

public class SurvivorRolloverTests {

	[Test]
	public void Calculate_MemberDies_RollsRemainingAssetsOverToTheSurvivor() {
		// Todd dies at 70, Tina lives to 90, so Todd's balances must pass to Tina.
		( CalculatedPlan calculatedPlan, CompiledPlan compiledPlan ) = Calculate();

		DateOnly deathDate = DeathDate( compiledPlan, "Todd" );
		CalculatedPeriod beforeDeath = calculatedPlan.Periods.Last( p => p.PeriodDate < deathDate );
		CalculatedPeriod afterDeath = calculatedPlan.Periods.First( p => p.PeriodDate >= deathDate );

		decimal toddBefore = MemberTotal( compiledPlan, beforeDeath, "Todd" );
		decimal tinaBefore = MemberTotal( compiledPlan, beforeDeath, "Tina" );

		Assert.Multiple( () => {
			Assert.That( toddBefore, Is.GreaterThan( 0m ) );
			Assert.That( MemberTotal( compiledPlan, afterDeath, "Todd" ), Is.Zero );

			// The survivor now holds both sides of the household's balances.
			Assert.That(
				MemberTotal( compiledPlan, afterDeath, "Tina" ),
				Is.GreaterThanOrEqualTo( toddBefore + tinaBefore - 2_000m ) );
		} );
	}

	[Test]
	public void Calculate_MemberDies_PreservesTaxStatusOfRolledOverAssets() {
		( CalculatedPlan calculatedPlan, CompiledPlan compiledPlan ) = Calculate();

		DateOnly deathDate = DeathDate( compiledPlan, "Todd" );
		CalculatedPeriod beforeDeath = calculatedPlan.Periods.Last( p => p.PeriodDate < deathDate );
		CalculatedPeriod afterDeath = calculatedPlan.Periods.First( p => p.PeriodDate >= deathDate );

		decimal taxableBefore = beforeDeath.EndingAssets
			.Where( a => a.TaxStatus == AssetTaxStatus.Taxable )
			.Sum( a => a.Amount );
		decimal taxableAfter = afterDeath.EndingAssets
			.Where( a => a.TaxStatus == AssetTaxStatus.Taxable )
			.Sum( a => a.Amount );

		Assert.That( taxableAfter, Is.EqualTo( taxableBefore ).Within( 1m ) );
	}

	private static DateOnly DeathDate(
		CompiledPlan compiledPlan,
		string memberName
	) {
		return compiledPlan.Members.First( m => m.Name == memberName ).DeathDate;
	}

	private static decimal MemberTotal(
		CompiledPlan compiledPlan,
		CalculatedPeriod period,
		string memberName
	) {
		MemberId memberId = compiledPlan.Members.First( m => m.Name == memberName ).MemberId;
		HashSet<AssetId> assetIds = compiledPlan.Assets
			.Where( a => a.MemberId == memberId )
			.Select( a => a.AssetId )
			.ToHashSet();

		return period.EndingAssets
			.Where( a => assetIds.Contains( a.AssetId ) )
			.Sum( a => a.Amount );
	}

	private static (CalculatedPlan, CompiledPlan) Calculate() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				new Member( "Todd", new DateOnly( 1960, 1, 1 ), 70, 66, 70, 80m ),
				new Member( "Tina", new DateOnly( 1961, 1, 1 ), 90, 65, 70, 50m )
			],
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 300_000m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 100_000m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 50_000m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m, -1m, -1m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m, -1m, -1m )
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
			burndown: null
		);

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		return ( new PlanCalculator().Calculate( plan, compiledPlan ), compiledPlan );
	}
}
