using Planning.Model;
using Planning.Model.CompiledPlans;
using Planning.Model.Plans;

namespace Planning.Compiler.Compilers;

internal sealed class CPPEntitlementCompiler {

	private const int MaximumContributionMonths = 480;
	private const decimal BaseReplacementRate = 0.25m;
	private const decimal FirstAdditionalReplacementRate = 0.0833m;
	private const decimal SecondAdditionalReplacementRate = 0.3333m;
	private const decimal FullyPhasedSecondEarningsBand = 0.14m;

	public CompiledCPPEntitlement Compile(
		Plan plan,
		Member member,
		DateOnly retirementDate,
		DateOnly cppStartDate,
		DateOnly deathDate
	) {
		decimal baseMaximum = EstimateBaseMaximum( plan );
		DateOnly end = retirementDate < cppStartDate ? retirementDate : cppStartDate;
		if( deathDate < end ) {
			end = deathDate.StartOfNextMonth();
		}
		DateOnly start = member.BirthDate.AddYears( 18 ).StartOfNextMonth();
		if( start.Year < 2019 ) {
			start = new DateOnly( 2019, 1, 1 );
		}

		List<decimal> firstWeights = [];
		List<decimal> secondWeights = [];
		for( DateOnly month = start; month < end; month = month.AddMonths( 1 ) ) {
			firstWeights.Add( month.Year switch {
				2019 => 0.15m,
				2020 => 0.30m,
				2021 => 0.50m,
				2022 => 0.75m,
				_ => 1m
			} );
			if( month.Year >= 2024 ) {
				secondWeights.Add( month.Year switch {
					2024 => 4_700m / 68_500m,
					2025 => 9_900m / 71_300m,
					2026 => 10_400m / 74_600m,
					_ => FullyPhasedSecondEarningsBand
				} );
			}
		}

		// Best 480 months, not the first 480: early phase-in months can be displaced.
		decimal firstAccrual = firstWeights.OrderDescending().Take( MaximumContributionMonths ).Sum() / MaximumContributionMonths;
		decimal secondAccrual = secondWeights.OrderDescending().Take( MaximumContributionMonths ).Sum() / MaximumContributionMonths;
		return new CompiledCPPEntitlement(
			baseMaximum * member.CPPBasePercent / 100m,
			baseMaximum * FirstAdditionalReplacementRate / BaseReplacementRate
				* firstAccrual * member.CPPFirstAdditionalPercent / 100m,
			baseMaximum * SecondAdditionalReplacementRate / BaseReplacementRate
				* secondAccrual * member.CPPSecondAdditionalPercent / 100m );
	}

	internal static decimal EstimateBaseMaximum(
		Plan plan
	) {
		return plan.CPPMaximum == 0m
			? 0m
			: plan.CPPMaximum - ( plan.CPPCombinedSurvivorMaximum - plan.CPPMaximum )
				/ GovernmentBenefitCompiler.EnhancedSurvivorRate;
	}
}
