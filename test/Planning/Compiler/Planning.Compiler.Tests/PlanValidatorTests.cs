using Planning.Model;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Compiler.Tests;

public class PlanValidatorTests {

	[Test]
	public void Validate_DefaultPlan_IsValid() {
		PlanValidationResult result = new PlanValidator().Validate( TestPlanFactory.Create() );

		Assert.That( result.IsValid, Is.True, string.Join( "; ", result.Errors ) );
	}

	[Test]
	public void Validate_NoMembers_ReportsError() {
		Plan plan = TestPlanFactory.Create(
			members: [],
			assets: [],
			lifeInsurance: [],
			contributions: []
		);

		PlanValidationResult result = new PlanValidator().Validate( plan );

		Assert.That( result.Errors, Has.Some.Contains( "at least one member" ) );
	}

	[Test]
	public void Validate_WrongHouseholdSize_ReportsError() {
		Plan plan = TestPlanFactory.Create(
			members: [
				new Member( "Todd", new DateOnly( 1970, 1, 1 ), 85, 60, 70, 80m )
			],
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 100m )
			],
			lifeInsurance: [],
			contributions: []
		);

		PlanValidationResult result = new PlanValidator().Validate( plan );

		Assert.That( result.Errors, Has.Some.Contains( "exactly 2 members" ) );
	}

	[Test]
	public void Validate_DuplicateMemberNames_ReportsError() {
		Plan plan = TestPlanFactory.Create(
			members: [
				new Member( "Todd", new DateOnly( 1970, 1, 1 ), 85, 60, 70, 80m ),
				new Member( "Todd", new DateOnly( 1972, 1, 1 ), 85, 60, 70, 80m )
			],
			assets: [],
			lifeInsurance: [],
			contributions: []
		);

		PlanValidationResult result = new PlanValidator().Validate( plan );

		Assert.That( result.Errors, Has.Some.Contains( "used more than once" ) );
	}

	[Test]
	public void Validate_RetirementAgeNotBeforeTarget_ReportsError() {
		Plan plan = TestPlanFactory.Create(
			members: [
				new Member( "Todd", new DateOnly( 1970, 1, 1 ), 60, 60, 70, 80m ),
				new Member( "Tina", new DateOnly( 1972, 1, 1 ), 85, 60, 70, 50m )
			],
			assets: [],
			lifeInsurance: [],
			contributions: []
		);

		PlanValidationResult result = new PlanValidator().Validate( plan );

		Assert.That( result.Errors, Has.Some.Contains( "must be before the target age" ) );
	}

	[TestCase( 59 )]
	[TestCase( 71 )]
	public void Validate_CPPStartAgeOutOfRange_ReportsError(
		int cppStartAge
	) {
		Plan plan = TestPlanFactory.Create(
			members: [
				new Member( "Todd", new DateOnly( 1970, 1, 1 ), 85, 60, cppStartAge, 80m ),
				new Member( "Tina", new DateOnly( 1972, 1, 1 ), 85, 60, 70, 50m )
			],
			assets: [],
			lifeInsurance: [],
			contributions: []
		);

		PlanValidationResult result = new PlanValidator().Validate( plan );

		Assert.That( result.Errors, Has.Some.Contains( "CPP start age" ) );
	}

	[Test]
	public void Validate_NoMemberSpecifiesRetirementAge_ReportsError() {
		Plan plan = TestPlanFactory.Create(
			members: [
				new Member( "Todd", new DateOnly( 1970, 1, 1 ), 85, null, 70, 80m ),
				new Member( "Tina", new DateOnly( 1972, 1, 1 ), 85, null, 70, 50m )
			],
			assets: [],
			lifeInsurance: [],
			contributions: []
		);

		PlanValidationResult result = new PlanValidator().Validate( plan );

		Assert.That( result.Errors, Has.Some.Contains( "at least one household member must specify a retirement age" ).IgnoreCase );
	}

	[Test]
	public void Validate_AssetReferencesUnknownMember_ReportsError() {
		Plan plan = TestPlanFactory.Create(
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Nobody", 100m )
			],
			contributions: []
		);

		PlanValidationResult result = new PlanValidator().Validate( plan );

		Assert.That( result.Errors, Has.Some.Contains( "references unknown member" ) );
	}

	[Test]
	public void Validate_ContributionReferencesUnknownSpousalContributor_ReportsError() {
		Plan plan = TestPlanFactory.Create(
			contributions: [
				new Contribution( "Tina", 3000m, 2026, Indexed: false, Spousal: "Nobody" )
			]
		);

		PlanValidationResult result = new PlanValidator().Validate( plan );

		Assert.That( result.Errors, Has.Some.Contains( "unknown spousal contributor" ) );
	}

	[Test]
	public void Validate_ContributionNamingItselfAsSpousal_ReportsNoError() {
		Plan plan = TestPlanFactory.Create(
			contributions: [
				new Contribution( "Todd", 3000m, 2026, Indexed: false, Spousal: "Todd" )
			]
		);

		PlanValidationResult result = new PlanValidator().Validate( plan );

		Assert.That( result.Errors, Has.None.Contains( "spousal" ) );
	}

	[Test]
	public void Validate_MemberWithPartialAssets_HasEveryTaxStatusSynthesized() {
		// A member given only a Taxable account still ends up holding one account of every tax
		// status, so contributions and rollovers always have a destination.
		Plan plan = TestPlanFactory.Create(
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 100m )
			],
			contributions: [
				new Contribution( "Todd", 1000m, 2030, Indexed: false )
			]
		);

		PlanValidationResult result = new PlanValidator().Validate( plan );

		Assert.Multiple( () => {
			Assert.That( result.IsValid, Is.True );

			foreach( string memberName in new[] { "Todd", "Tina" } ) {
				Assert.That(
					plan.Assets.Where( a => a.Member == memberName ).Select( a => a.TaxStatus ),
					Is.EquivalentTo( Enum.GetValues<AssetTaxStatus>() ) );
			}
		} );
	}

	[Test]
	public void Validate_BirthDateNotBeforeStartDate_ReportsError() {
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				new Member( "Todd", new DateOnly( 2030, 1, 1 ), 85, 60, 70, 80m ),
				new Member( "Tina", new DateOnly( 1972, 1, 1 ), 85, 60, 70, 50m )
			],
			assets: [],
			lifeInsurance: [],
			contributions: []
		);

		PlanValidationResult result = new PlanValidator().Validate( plan );

		Assert.That( result.Errors, Has.Some.Contains( "must be before the plan start date" ) );
	}
}
