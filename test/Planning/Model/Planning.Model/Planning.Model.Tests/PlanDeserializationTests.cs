using System.Text.Json;
using System.Text.Json.Serialization;

using Planning.Model.Plans;

namespace Planning.Model.Tests;

/// <summary>
/// Pins the JSON contract of <see cref="Plan"/> and every record reachable from it.
///
/// The CLI is the only production consumer of this binding, and it does not reject unknown
/// properties, so a renamed or mistyped JSON property would otherwise bind to its default and
/// be silently wrong. Every value below is deliberately distinct and non-default, so a
/// property that fails to bind shows up as a failed assertion rather than a plausible zero.
/// </summary>
public class PlanDeserializationTests {

	/// <summary>
	/// Mirrors the options used by the CLI. These must stay in sync, or this test stops
	/// describing real behaviour.
	/// </summary>
	private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions {
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		RespectRequiredConstructorParameters = true,
		Converters = { new JsonStringEnumConverter() }
	};

	private const string PlanJson = """
	{
		"StartDate": "2026-03-15",
		"Members": [
			{ "Name": "Todd", "BirthDate": "1973-12-31", "TargetAgeInYears": 85, "RetirementAgeInYears": 60, "CPPStartInYears": 70, "CPPPercent": 90 },
			{ "Name": "Tina", "BirthDate": "1976-07-22", "TargetAgeInYears": 95, "RetirementAgeInYears": 62, "CPPStartInYears": 65, "CPPPercent": 50 }
		],
		"CPPMaximum": 1507.65,
		"CPPCombinedSurvivorMaximum": 1531.56,
		"OASMaximum": 743.05,
		"Assets": [
			{ "Name": "RRSP", "TaxStatus": "Taxable", "Member": "Todd", "Amount": 550000, "ContributionBacklog": 219081, "AnnualContributionLimit": 22000, "HasUnlimitedContributionRoom": false, "CostBase": 0, "AnnualContributionIncreasePercent": 2.0 },
			{ "Name": "TFSA", "TaxStatus": "TaxExempt", "Member": "Tina", "Amount": 12345, "ContributionBacklog": 109000, "AnnualContributionLimit": 7000, "HasUnlimitedContributionRoom": false, "CostBase": 0, "AnnualContributionIncreasePercent": null }
		],
		"AnnualInflationPercent": 3.0,
		"AnnualReturnPercent": 6.0,
		"LifeInsurance": [
			{ "Member": "Todd", "Amount": 250000 },
			{ "Member": "Tina", "Amount": 275000 }
		],
		"RetirementIncome": {
			"GoGo": 6000,
			"SlowGo": 5000,
			"SlowGoYears": 10,
			"NoGo": 5500,
			"NoGoYears": 12
		},
		"Contributions": [
			{ "Member": "Todd", "Amount": 3500, "StartYear": 2026, "Indexed": true, "AnnualIncreasePercent": 2.0, "Spousal": "Tina" },
			{ "Member": "Tina", "Amount": 3000, "StartYear": 2028, "Indexed": false, "AnnualIncreasePercent": 0.0, "Spousal": "Todd" }
		],
		"TaxPolicy": {
			"Year": 2024,
			"FederalBrackets": [
				{ "LowerBound": 0, "Rate": 15.0 },
				{ "LowerBound": 55867, "Rate": 20.5 }
			],
			"ProvincialBrackets": [
				{ "LowerBound": 0, "Rate": 5.05 }
			],
			"AllowPensionSplitting": true,
			"BasicPersonalAmount": 15705,
			"ProvincialBasicPersonalAmount": 12399,
			"AgeAmountBase": 8790,
			"AgeAmountIncomeThreshold": 44325,
			"AgeAmountReductionRate": 15.0,
			"AgeAmountEligibilityAge": 65,
			"PensionIncomeAmount": 2000,
			"PensionIncomeEligibilityAge": 67,
			"RRIFMinimums": [
				{ "Age": 71, "Percent": 5.28 },
				{ "Age": 95, "Percent": 20.00 }
			],
			"OasClawbackThreshold": 90997,
			"OasClawbackRate": 15.0
		},
		"Burndown": {
			"BurndownYears": 15
		},
		"Inheritance": [
			{ "Member": "Todd", "Amount": 50000, "AgeReceived": 65 }
		]
	}
	""";

	[Test]
	public void Deserialize_FullPlan_BindsEveryScalarOnThePlan() {
		Plan plan = Deserialize();

		using( Assert.EnterMultipleScope() ) {
			Assert.That( plan.StartDate, Is.EqualTo( new DateOnly( 2026, 3, 15 ) ) );
			Assert.That( plan.CPPMaximum, Is.EqualTo( 1507.65m ) );
			Assert.That( plan.CPPCombinedSurvivorMaximum, Is.EqualTo( 1531.56m ) );
			Assert.That( plan.OASMaximum, Is.EqualTo( 743.05m ) );
			Assert.That( plan.AnnualInflationPercent, Is.EqualTo( 3.0m ) );
			Assert.That( plan.AnnualReturnPercent, Is.EqualTo( 6.0m ) );
		}
	}

	[Test]
	public void Deserialize_FullPlan_BindsEveryMember() {
		Member[] members = [.. Deserialize().Members];

		using( Assert.EnterMultipleScope() ) {
			Assert.That( members, Has.Length.EqualTo( 2 ) );

			Assert.That( members[0].Name, Is.EqualTo( "Todd" ) );
			Assert.That( members[0].BirthDate, Is.EqualTo( new DateOnly( 1973, 12, 31 ) ) );
			Assert.That( members[0].TargetAgeInYears, Is.EqualTo( 85 ) );
			Assert.That( members[0].RetirementAgeInYears, Is.EqualTo( 60 ) );
			Assert.That( members[0].CPPStartInYears, Is.EqualTo( 70 ) );
			Assert.That( members[0].CPPPercent, Is.EqualTo( 90m ) );

			Assert.That( members[1].Name, Is.EqualTo( "Tina" ) );
			Assert.That( members[1].BirthDate, Is.EqualTo( new DateOnly( 1976, 7, 22 ) ) );
			Assert.That( members[1].TargetAgeInYears, Is.EqualTo( 95 ) );
			Assert.That( members[1].RetirementAgeInYears, Is.EqualTo( 62 ) );
			Assert.That( members[1].CPPStartInYears, Is.EqualTo( 65 ) );
			Assert.That( members[1].CPPPercent, Is.EqualTo( 50m ) );
		}
	}

	[Test]
	public void Deserialize_FullPlan_BindsEveryAssetIncludingTheTaxStatusEnum() {
		Asset[] assets = [.. Deserialize().Assets];

		using( Assert.EnterMultipleScope() ) {
			Assert.That( assets, Has.Length.EqualTo( 2 ) );

			Assert.That( assets[0].Name, Is.EqualTo( "RRSP" ) );
			Assert.That( assets[0].TaxStatus, Is.EqualTo( AssetTaxStatus.Taxable ) );
			Assert.That( assets[0].Member, Is.EqualTo( "Todd" ) );
			Assert.That( assets[0].Amount, Is.EqualTo( 550_000m ) );
			Assert.That( assets[0].ContributionBacklog, Is.EqualTo( 219_081m ) );
			Assert.That( assets[0].AnnualContributionLimit, Is.EqualTo( 22_000m ) );
			Assert.That( assets[0].HasUnlimitedContributionRoom, Is.False );
			Assert.That( assets[0].CostBase, Is.Zero );
			Assert.That( assets[0].AnnualContributionIncreasePercent, Is.EqualTo( 2.0m ) );

			Assert.That( assets[1].Name, Is.EqualTo( "TFSA" ) );
			Assert.That( assets[1].TaxStatus, Is.EqualTo( AssetTaxStatus.TaxExempt ) );
			Assert.That( assets[1].Member, Is.EqualTo( "Tina" ) );
			Assert.That( assets[1].Amount, Is.EqualTo( 12_345m ) );
			Assert.That( assets[1].ContributionBacklog, Is.EqualTo( 109_000m ) );
			Assert.That( assets[1].AnnualContributionLimit, Is.EqualTo( 7_000m ) );
			Assert.That( assets[1].HasUnlimitedContributionRoom, Is.False );
			Assert.That( assets[1].CostBase, Is.Zero );
			// A null rate is the signal to fall back to the plan's inflation, so it must survive
			// binding as null rather than collapsing to zero.
			Assert.That( assets[1].AnnualContributionIncreasePercent, Is.Null );
		}
	}

	[Test]
	public void Deserialize_AssetWithoutACostBase_Throws() {
		// Both assets above are registered accounts, so their cost base is legitimately zero and
		// a failure to bind would look identical to a correct parse. The property is therefore
		// required rather than defaulted, and that requirement is what this pins.
		string json = PlanJson.Replace( ", \"CostBase\": 0", "", StringComparison.Ordinal );

		Assert.That(
			() => JsonSerializer.Deserialize<Plan>( json, SerializerOptions ),
			Throws.InstanceOf<JsonException>() );
	}

	[Test]
	public void Deserialize_FullPlan_BindsTheLifeInsuranceMemberName() {
		// The member property was once named "MemberName" in the sample plan, which bound to
		// null without complaint. This pins the name.
		LifeInsurance[] policies = [.. Deserialize().LifeInsurance];

		using( Assert.EnterMultipleScope() ) {
			Assert.That( policies, Has.Length.EqualTo( 2 ) );

			Assert.That( policies[0].Member, Is.EqualTo( "Todd" ) );
			Assert.That( policies[0].Amount, Is.EqualTo( 250_000m ) );

			Assert.That( policies[1].Member, Is.EqualTo( "Tina" ) );
			Assert.That( policies[1].Amount, Is.EqualTo( 275_000m ) );
		}
	}

	[Test]
	public void Deserialize_FullPlan_BindsTheRetirementIncomePhases() {
		RetirementIncome income = Deserialize().RetirementIncome;

		using( Assert.EnterMultipleScope() ) {
			Assert.That( income.GoGo, Is.EqualTo( 6_000m ) );
			Assert.That( income.SlowGo, Is.EqualTo( 5_000m ) );
			Assert.That( income.SlowGoYears, Is.EqualTo( 10 ) );
			Assert.That( income.NoGo, Is.EqualTo( 5_500m ) );
			Assert.That( income.NoGoYears, Is.EqualTo( 12 ) );
		}
	}

	[Test]
	public void Deserialize_FullPlan_BindsEveryContributionIncludingTheSpousalContributor() {
		Contribution[] contributions = [.. Deserialize().Contributions];

		using( Assert.EnterMultipleScope() ) {
			Assert.That( contributions, Has.Length.EqualTo( 2 ) );

			Assert.That( contributions[0].Member, Is.EqualTo( "Todd" ) );
			Assert.That( contributions[0].Amount, Is.EqualTo( 3_500m ) );
			Assert.That( contributions[0].StartYear, Is.EqualTo( 2026 ) );
			Assert.That( contributions[0].Indexed, Is.True );
			Assert.That( contributions[0].AnnualIncreasePercent, Is.EqualTo( 2.0m ) );
			Assert.That( contributions[0].Spousal, Is.EqualTo( "Tina" ) );
			Assert.That( contributions[0].IsSpousal, Is.True );

			Assert.That( contributions[1].Member, Is.EqualTo( "Tina" ) );
			Assert.That( contributions[1].Amount, Is.EqualTo( 3_000m ) );
			Assert.That( contributions[1].StartYear, Is.EqualTo( 2028 ) );
			Assert.That( contributions[1].Indexed, Is.False );
			Assert.That( contributions[1].AnnualIncreasePercent, Is.EqualTo( 0.0m ) );
			Assert.That( contributions[1].Spousal, Is.EqualTo( "Todd" ) );
			Assert.That( contributions[1].IsSpousal, Is.True );
		}
	}

	[Test]
	public void Deserialize_FullPlan_BindsEveryTaxPolicyValue() {
		TaxPolicy policy = Deserialize().TaxPolicy;
		TaxBracket[] federal = [.. policy.FederalBrackets];
		TaxBracket[] provincial = [.. policy.ProvincialBrackets];
		RrifMinimum[] minimums = [.. policy.RrifMinimums ?? []];

		using( Assert.EnterMultipleScope() ) {
			Assert.That( federal, Has.Length.EqualTo( 2 ) );
			Assert.That( federal[0].LowerBound, Is.Zero );
			Assert.That( federal[0].Rate, Is.EqualTo( 15.0m ) );
			Assert.That( federal[1].LowerBound, Is.EqualTo( 55_867m ) );
			Assert.That( federal[1].Rate, Is.EqualTo( 20.5m ) );

			Assert.That( provincial, Has.Length.EqualTo( 1 ) );
			Assert.That( provincial[0].LowerBound, Is.Zero );
			Assert.That( provincial[0].Rate, Is.EqualTo( 5.05m ) );

			Assert.That( policy.Year, Is.EqualTo( 2024 ) );
			Assert.That( policy.AllowPensionSplitting, Is.True );
			Assert.That( policy.BasicPersonalAmount, Is.EqualTo( 15_705m ) );
			Assert.That( policy.ProvincialBasicPersonalAmount, Is.EqualTo( 12_399m ) );
			Assert.That( policy.AgeAmountBase, Is.EqualTo( 8_790m ) );
			Assert.That( policy.AgeAmountIncomeThreshold, Is.EqualTo( 44_325m ) );
			Assert.That( policy.AgeAmountReductionRate, Is.EqualTo( 15.0m ) );
			Assert.That( policy.AgeAmountEligibilityAge, Is.EqualTo( 65 ) );
			Assert.That( policy.PensionIncomeAmount, Is.EqualTo( 2_000m ) );
			Assert.That( policy.PensionIncomeEligibilityAge, Is.EqualTo( 67 ) );
			Assert.That( policy.OasClawbackThreshold, Is.EqualTo( 90_997m ) );
			Assert.That( policy.OasClawbackRate, Is.EqualTo( 15.0m ) );

			// The JSON spells this "RRIFMinimums", which only binds because the CLI matches
			// property names case-insensitively.
			Assert.That( minimums, Has.Length.EqualTo( 2 ) );
			Assert.That( minimums[0].Age, Is.EqualTo( 71 ) );
			Assert.That( minimums[0].Percent, Is.EqualTo( 5.28m ) );
			Assert.That( minimums[1].Age, Is.EqualTo( 95 ) );
			Assert.That( minimums[1].Percent, Is.EqualTo( 20.00m ) );
		}
	}

	[Test]
	public void Deserialize_FullPlan_BindsTheBurndownAndInheritance() {
		Plan plan = Deserialize();
		Inheritance[] inheritances = [.. plan.Inheritance];

		using( Assert.EnterMultipleScope() ) {
			Assert.That( plan.Burndown.BurndownYears, Is.EqualTo( 15 ) );

			Assert.That( inheritances, Has.Length.EqualTo( 1 ) );
			Assert.That( inheritances[0].Member, Is.EqualTo( "Todd" ) );
			Assert.That( inheritances[0].Amount, Is.EqualTo( 50_000m ) );
			Assert.That( inheritances[0].AgeReceived, Is.EqualTo( 65 ) );
		}
	}

	[Test]
	public void Deserialize_PlanMissingARequiredValue_Throws() {
		// RespectRequiredConstructorParameters is what turns an omitted value into a hard
		// failure rather than a silent zero.
		string json = PlanJson.Replace( "\"OASMaximum\": 743.05,", "", StringComparison.Ordinal );

		Assert.That(
			() => JsonSerializer.Deserialize<Plan>( json, SerializerOptions ),
			Throws.InstanceOf<JsonException>()
		);
	}

	private static Plan Deserialize() {
		return JsonSerializer.Deserialize<Plan>( PlanJson, SerializerOptions )
			?? throw new InvalidOperationException( "The plan JSON deserialized to null." );
	}
}
