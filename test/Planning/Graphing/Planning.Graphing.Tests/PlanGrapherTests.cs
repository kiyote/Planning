using Planning.Calculator;
using Planning.Compiler;
using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Graphing.Tests;

public class PlanGrapherTests {

	[Test]
	public void SaveTotalAssetsByYear_ProducesNonEmptyPngFile() {
		CalculatedPlan calculatedPlan = CreateCalculatedPlan();
		string filePath = Path.Combine( Path.GetTempPath(), $"planning-graph-{Guid.NewGuid():N}.png" );

		try {
			new PlanGrapher().SaveTotalAssetsByYear( calculatedPlan, filePath );

			Assert.Multiple( () => {
				Assert.That( File.Exists( filePath ), Is.True );
				Assert.That( new FileInfo( filePath ).Length, Is.GreaterThan( 0 ) );
			} );

			byte[] header = new byte[8];
			using( FileStream stream = File.OpenRead( filePath ) ) {
				int read = stream.Read( header, 0, header.Length );
				Assert.That( read, Is.EqualTo( header.Length ) );
			}

			// PNG files start with the 8-byte signature 89 50 4E 47 0D 0A 1A 0A.
			byte[] pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
			Assert.That( header, Is.EqualTo( pngSignature ) );
		} finally {
			if( File.Exists( filePath ) ) {
				File.Delete( filePath );
			}
		}
	}

	[Test]
	public void SaveTotalAssetsByYear_NullPlan_Throws() {
		Assert.That(
			() => new PlanGrapher().SaveTotalAssetsByYear( null!, "graph.png" ),
			Throws.ArgumentNullException );
	}

	[Test]
	public void SaveTotalAssetsByYear_EmptyFilePath_Throws() {
		CalculatedPlan calculatedPlan = CreateCalculatedPlan();

		Assert.That(
			() => new PlanGrapher().SaveTotalAssetsByYear( calculatedPlan, "  " ),
			Throws.ArgumentException );
	}

	private static CalculatedPlan CreateCalculatedPlan() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				new Member( "Todd", new DateOnly( 1973, 12, 31 ), 85, 60, 70, 90m ),
				new Member( "Tina", new DateOnly( 1976, 7, 22 ), 95, null, 65, 50m )
			],
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 550000m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0m ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 30000m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m, hasUnlimitedContributionRoom: true ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m, hasUnlimitedContributionRoom: true )
			],
			annualInflationPercent: 3.0m,
			annualReturnPercent: 6.0m,
			lifeInsurance: [
				new LifeInsurance( "Todd", 250000 ),
				new LifeInsurance( "Tina", 250000 )
			],
			retirementIncome: new RetirementIncome(
				GoGo: 5800,
				SlowGo: 4700m,
				SlowGoYears: 10,
				NoGo: 5200m,
				NoGoYears: 10
			),
			contributions: [
				new Contribution( "Todd", 3500, 2026, Indexed: false, AnnualIncreasePercent: 0m ),
				new Contribution( "Tina", 3000, 2028, Indexed: false, AnnualIncreasePercent: 0m )
			]
		);
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		return new PlanCalculator().Calculate( plan, compiledPlan );
	}
}
