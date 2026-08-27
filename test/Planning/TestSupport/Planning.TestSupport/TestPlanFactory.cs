using Planning.Model;
using Planning.Model.Plans;

namespace Planning.TestSupport;

public static class TestPlanFactory {

	public static Plan Create(
		DateOnly? startDate = null,
		IEnumerable<Member>? members = null,
		decimal cppMaximum = 1507.65m,
		decimal cppCombinedSurvivorMaximum = 1531.56m,
		decimal oasMaximum = 743.05m,
		IEnumerable<Asset>? assets = null,
		decimal annualInflationPercent = 2.6m,
		decimal annualReturnPercent = 5.0m,
		IEnumerable<LifeInsurance>? lifeInsurance = null,
		RetirementIncome? retirementIncome = null,
		IEnumerable<Contribution>? contributions = null,
		TaxPolicy? taxPolicy = null,
		Burndown? burndown = null,
		IEnumerable<Inheritance>? inheritance = null
	) {
		return new Plan(
			StartDate: startDate ?? new DateOnly( 2026, 7, 1 ),
			Members: members ?? CreateMembers(),
			CPPMaximum: cppMaximum,
			CPPCombinedSurvivorMaximum: cppCombinedSurvivorMaximum,
			OASMaximum: oasMaximum,
			Assets: assets ?? CreateAssets(),
			AnnualInflationPercent: annualInflationPercent,
			AnnualReturnPercent: annualReturnPercent,
			LifeInsurance: lifeInsurance ?? CreateLifeInsurance(),
			RetirementIncome: retirementIncome ?? new RetirementIncome(
				GoGo: 7000.0m,
				SlowGo: 6500.0m,
				SlowGoYears: 10,
				NoGo: 6000.0m,
				NoGoYears: 10
			),
			Contributions: contributions ?? CreateContributions(),
			TaxPolicy: taxPolicy ?? CreateTaxPolicy(),
			Burndown: burndown,
			Inheritance: inheritance
		);
	}

	public static TaxPolicy CreateTaxPolicy() {
		// Representative 2024 Canadian federal brackets and Ontario provincial brackets
		// (annual thresholds in nominal start-year dollars).
		return new TaxPolicy(
			FederalBrackets: [
				new TaxBracket( LowerBound: 0m, Rate: 15.0m ),
				new TaxBracket( LowerBound: 55_867m, Rate: 20.5m ),
				new TaxBracket( LowerBound: 111_733m, Rate: 26.0m ),
				new TaxBracket( LowerBound: 173_205m, Rate: 29.0m ),
				new TaxBracket( LowerBound: 246_752m, Rate: 33.0m )
			],
			ProvincialBrackets: [
				new TaxBracket( LowerBound: 0m, Rate: 5.05m ),
				new TaxBracket( LowerBound: 51_446m, Rate: 9.15m ),
				new TaxBracket( LowerBound: 102_894m, Rate: 11.16m ),
				new TaxBracket( LowerBound: 150_000m, Rate: 12.16m ),
				new TaxBracket( LowerBound: 220_000m, Rate: 13.16m )
			]
		);
	}

	public static Member[] CreateMembers() {
		return [
			new Member(
				Name: "Todd",
				BirthDate: new DateOnly( 1973, 12, 25 ),
				TargetAgeInYears: 85,
				RetirementAgeInYears: 60,
				CPPStartInYears: 70,
				CPPPercent: 80m
			),
			new Member(
				Name: "Tina",
				BirthDate: new DateOnly( 1977, 6, 20 ),
				TargetAgeInYears: 95,
				RetirementAgeInYears: 57,
				CPPStartInYears: 70,
				CPPPercent: 50m
			)
		];
	}

	public static Asset[] CreateAssets() {
		return [
			CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", 520_000.0m, 10_000m, 2_000m ),
			CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 30_000.0m, 10_000m, 2_000m ),
			CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0.0m, 109_000m, 6_000m ),
			CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0.0m, 109_000m, 6_000m )
		];
	}

	public static Asset CreateAsset(
		string name,
		AssetTaxStatus taxStatus,
		string member,
		decimal amount,
		decimal contributionBacklog = 0m,
		decimal annualContributionLimit = 0m,
		decimal returnPercentage = 5m,
		DateOnly? startDate = null
	) {
		return new Asset(
			Name: name,
			TaxStatus: taxStatus,
			Member: member,
			Amount: amount,
			ReturnPercentages: [
				new RangedValue(
					StartDate: DateOnly.MinValue,
					Value: returnPercentage
				)
			],
			StartDate: startDate ?? DateOnly.MinValue,
			ContributionBacklog: contributionBacklog,
			AnnualContributionLimit: annualContributionLimit
		);
	}

	private static LifeInsurance[] CreateLifeInsurance() {
		return [
			new LifeInsurance( "Todd", 250_000m ),
			new LifeInsurance( "Tina", 250_000m )
		];
	}

	private static Contribution[] CreateContributions() {
		return [
			new Contribution(
				Member: "Todd",
				Amount: 3200.0m,
				StartYear: 2026,
				Indexed: true
			),
			new Contribution(
				Member: "Tina",
				Amount: 3000.0m,
				StartYear: 2028,
				Indexed: true
			)
		];
	}
}
