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
	public void Validate_TaxPolicyYearAfterPlanStart_ReportsError() {
		// A future-dated policy would be indexed by a negative exponent, silently deflating
		// its brackets rather than carrying them forward.
		Plan plan = CreatePlanWithTaxPolicyYear( 2027 );

		PlanValidationResult result = new PlanValidator().Validate( plan );

		Assert.That( result.Errors, Has.Some.Contains( "must not be after the plan start year" ) );
	}

	[Test]
	public void Validate_TaxPolicyYearTooFarBeforePlanStart_ReportsError() {
		Plan plan = CreatePlanWithTaxPolicyYear( 2020 );

		PlanValidationResult result = new PlanValidator().Validate( plan );

		Assert.That( result.Errors, Has.Some.Contains( "must not be more than 5 years before" ) );
	}

	[Test]
	public void Validate_TaxPolicyYearUnsetAtDefault_ReportsError() {
		// A policy left at the default year would index by roughly two millennia of inflation
		// and produce absurd figures rather than failing loudly.
		Plan plan = CreatePlanWithTaxPolicyYear( 0 );

		PlanValidationResult result = new PlanValidator().Validate( plan );

		Assert.That( result.Errors, Has.Some.Contains( "must not be more than 5 years before" ) );
	}

	[TestCase( 2021 )]
	[TestCase( 2024 )]
	[TestCase( 2026 )]
	public void Validate_TaxPolicyYearWithinTheAllowedWindow_IsValid( int policyYear ) {
		// The window is inclusive at both ends: the plan start year itself and exactly five
		// years earlier are both acceptable.
		Plan plan = CreatePlanWithTaxPolicyYear( policyYear );

		PlanValidationResult result = new PlanValidator().Validate( plan );

		Assert.That( result.IsValid, Is.True, string.Join( "; ", result.Errors ) );
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
	public void Validate_CapitalGainsAssetWithoutUnlimitedContributionRoom_ReportsError() {
		Plan plan = TestPlanFactory.Create(
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 100m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 100m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m, hasUnlimitedContributionRoom: true )
			],
			contributions: []
		);

		PlanValidationResult result = new PlanValidator().Validate( plan );

		Assert.That( result.Errors, Has.Some.Contains( "must have unlimited contribution room" ) );
	}

	[Test]
	public void Validate_RegisteredAssetWithUnlimitedContributionRoom_ReportsError() {
		Plan plan = TestPlanFactory.Create(
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 100m, hasUnlimitedContributionRoom: true ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m, hasUnlimitedContributionRoom: true ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 100m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m, hasUnlimitedContributionRoom: true )
			],
			contributions: []
		);

		PlanValidationResult result = new PlanValidator().Validate( plan );

		Assert.That( result.Errors, Has.Some.Contains( "only CapitalGains assets can have unlimited contribution room" ) );
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
	public void Validate_MemberWithPartialAssets_PlanDoesNotValidated() {
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

		Assert.That( result.IsValid, Is.False );
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

	private static Plan CreatePlanWithTaxPolicyYear(
		int policyYear
	) {
		return TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			taxPolicy: TestPlanFactory.CreateTaxPolicy() with { Year = policyYear }
		);
	}
}
