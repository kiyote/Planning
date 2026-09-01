using Planning.Calculator;
using Planning.Compiler;
using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Graphing.Tests;

/// <summary>
/// Sweeps over every CPP start age available to a member, holding the other member fixed, and
/// reports the age that leaves the largest estate at the end of the plan. The trade-off is real
/// because the compiler applies the CPP actuarial adjustment: starting early pays a permanently
/// smaller pension but preserves registered assets, while deferring pays more but draws those
/// assets down in the meantime. Vary the plan inputs in <see cref="CreatePlan"/> to sweep a
/// different scenario.
/// </summary>
public class CppStartAgeSearchTests {

	// The range the plan validator permits for a CPP start age.
	private const int MinimumCPPStartAge = 60;
	private const int MaximumCPPStartAge = 70;

	// The start age each member holds while the other one is being swept.
	private const int ToddFixedCPPStartAge = 70;
	private const int TinaFixedCPPStartAge = 65;

	[Test]
	public void FindToddCppStartAgeMaximizingNetEstate() {
		Sweep( "Todd" );
	}

	[Test]
	public void FindTinaCppStartAgeMaximizingNetEstate() {
		Sweep( "Tina" );
	}

	private static void Sweep( string sweptMember ) {
		List<(int Age, decimal NetEstate, bool HasShortfall)> results = [];

		for( int age = MinimumCPPStartAge; age <= MaximumCPPStartAge; age++ ) {
			CalculatedPlan result = Calculate( sweptMember, age );
			results.Add( (age, result.EstateSummary.NetEstate, result.InsufficientFunds.HasShortfall) );
		}

		(int Age, decimal NetEstate, bool HasShortfall) best = results.MaxBy( r => r.NetEstate );
		decimal worst = results.Min( r => r.NetEstate );

		string heldMember = sweptMember == "Todd" ? "Tina" : "Todd";
		TestContext.Out.WriteLine(
			$"{sweptMember} CPP start age sweep ({heldMember} held fixed), maximizing nominal net estate:" );
		foreach( (int Age, decimal NetEstate, bool HasShortfall) result in results ) {
			string marker = result.Age == best.Age ? "  <== best" : string.Empty;
			TestContext.Out.WriteLine(
				$"  Age {result.Age}: NetEstate={result.NetEstate:N2} Shortfall={result.HasShortfall}{marker}" );
		}
		TestContext.Out.WriteLine( $"Best age: {best.Age} at {best.NetEstate:N2}" );
		TestContext.Out.WriteLine( $"Spread between best and worst: {best.NetEstate - worst:N2}" );

		// An insolvent plan exhausts its assets under every start age, leaving only the life
		// insurance behind. The comparison is only meaningful while the plan stays funded.
		Assert.That( results.Any( r => r.HasShortfall ), Is.False,
			"The sweep must run on a solvent plan for the estate comparison to mean anything." );
	}

	private static CalculatedPlan Calculate( string sweptMember, int cppStartInYears ) {
		Plan plan = CreatePlan( sweptMember, cppStartInYears );
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		return new PlanCalculator().Calculate( plan, compiledPlan );
	}

	private static Plan CreatePlan( string sweptMember, int cppStartInYears ) {
		int toddCppStartInYears = sweptMember == "Todd" ? cppStartInYears : ToddFixedCPPStartAge;
		int tinaCppStartInYears = sweptMember == "Tina" ? cppStartInYears : TinaFixedCPPStartAge;

		return TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				new Member( "Todd", new DateOnly( 1973, 12, 31 ), 85, 60, toddCppStartInYears, 90m ),
				new Member( "Tina", new DateOnly( 1976, 7, 22 ), 95, null, tinaCppStartInYears, 50m )
			],
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 550000m, 219_081m, 22_000m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0m, 109_000m, 7_000m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m, hasUnlimitedContributionRoom: true ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 30000m, 147_614m, 10_800m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0m, 109_000m, 7_000m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m, hasUnlimitedContributionRoom: true )
			],
			annualInflationPercent: 3.0m,
			annualReturnPercent: 6.0m,
			lifeInsurance: [
				new LifeInsurance( "Todd", 250000 ),
				new LifeInsurance( "Tina", 250000 )
			],
			retirementIncome: new RetirementIncome(
				GoGo: 4000m,
				SlowGo: 3200m,
				SlowGoYears: 10,
				NoGo: 3600m,
				NoGoYears: 10
			),
			contributions: [
				new Contribution( "Todd", 3500, 2026, Indexed: false ),
				new Contribution( "Tina", 3000, 2028, Indexed: false )
			]
		);
	}
}
