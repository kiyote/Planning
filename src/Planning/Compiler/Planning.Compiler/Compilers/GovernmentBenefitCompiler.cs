using Planning.Model.CompiledPlans;
using Planning.Model.Plans;

namespace Planning.Compiler.Compilers;

internal sealed class GovernmentBenefitCompiler {

	private const decimal SurvivorPensionRate = 0.6m;
	private const decimal CombinedPensionReductionRate = 0.4m;
	internal const decimal EnhancedSurvivorRate = SurvivorPensionRate * ( 1m - CombinedPensionReductionRate );

	public IDictionary<CompiledPeriod, IDictionary<CompiledMember, decimal>> CompileCPP(
		Plan plan,
		IEnumerable<CompiledPeriod> periods,
		IEnumerable<CompiledMember> members
	) {
		IDictionary<CompiledPeriod, IDictionary<CompiledMember, decimal>> result = new Dictionary<CompiledPeriod, IDictionary<CompiledMember, decimal>>();
		decimal maximumCPP = plan.CPPMaximum;
		foreach( CompiledPeriod period in periods ) {

			result[period] = new Dictionary<CompiledMember, decimal>();
			foreach( CompiledMember member in members ) {
				if( member.CPPStartDate <= period.PeriodDate && member.DeathDate > period.PeriodDate ) {
					result[period][member] = maximumCPP * ( member.CPPPercent / 100 );
				} else {
					result[period][member] = 0.0m;
				}
			}

			if( period.PeriodDate.Month == 12 ) {
				maximumCPP *= 1 + ( plan.AnnualInflationPercent / 100 );
			}
		}

		return result;
	}

	/// <summary>
	/// Compiles survivor pensions and applies any resulting early-retirement adjustment
	/// to the supplied retirement-income schedule.
	/// </summary>
	public IDictionary<CompiledPeriod, IDictionary<CompiledMember, decimal>> CompileCPPSurvivor(
		Plan plan,
		IEnumerable<CompiledPeriod> periods,
		IEnumerable<CompiledMember> members,
		IDictionary<CompiledPeriod, IDictionary<CompiledMember, decimal>> memberCPP
	) {
		IDictionary<CompiledPeriod, IDictionary<CompiledMember, decimal>> result = new Dictionary<CompiledPeriod, IDictionary<CompiledMember, decimal>>();

		IReadOnlyDictionary<string, decimal> entitlementByName = plan.Members
			.ToDictionary( m => m.Name, m => m.CPPPercent / 100m );
		decimal maximumCPP = plan.CPPMaximum;
		// At full entitlement the base is capped, leaving only 60% * 60% of the
		// enhanced pension above the retirement maximum. Both maxima include enhancements.
		decimal enhancedMaximum = maximumCPP == 0m
			? 0m
			: ( plan.CPPCombinedSurvivorMaximum - maximumCPP ) / EnhancedSurvivorRate;
		decimal baseMaximum = maximumCPP - enhancedMaximum;

		foreach( CompiledPeriod period in periods ) {
			result[period] = members.ToDictionary( m => m, _ => 0m );
			foreach( CompiledMember survivor in members ) {
				CompiledMember deceased = members.Single( m => m != survivor );
				if( survivor.DeathDate <= period.PeriodDate || deceased.DeathDate > period.PeriodDate ) {
					continue;
				}

				decimal deceasedEntitlement = entitlementByName[deceased.Name];
				decimal fullSurvivorPension = maximumCPP * deceasedEntitlement * SurvivorPensionRate;
				if( survivor.CPPStartDate > period.PeriodDate ) {
					result[period][survivor] = fullSurvivorPension;
					continue;
				}

				decimal survivorEntitlement = entitlementByName[survivor.Name];
				decimal basePension = CalculateCombinedComponent(
					baseMaximum * deceasedEntitlement,
					baseMaximum * survivorEntitlement );
				basePension = Math.Min( basePension, baseMaximum * ( 1m - survivorEntitlement ) );
				// The same entitlement share is assumed for both additional components.
				// Their overlap reductions can therefore be combined, but they are not capped.
				decimal enhancedPension = CalculateCombinedComponent(
					enhancedMaximum * deceasedEntitlement,
					enhancedMaximum * survivorEntitlement );
				decimal survivorPension = basePension + enhancedPension;
				result[period][survivor] = survivorPension;

				// CPP Act 46(5): a reduction of the survivor pension also exempts that
				// amount of the retirement pension from an early-start reduction.
				decimal unadjustedRetirement = maximumCPP * survivorEntitlement;
				if( unadjustedRetirement > 0m ) {
					decimal earlyReduction = Math.Max(
						1m - memberCPP[period][survivor] / unadjustedRetirement, 0m );
					memberCPP[period][survivor] +=
						( fullSurvivorPension - survivorPension ) * earlyReduction;
				}
			}

			if( period.PeriodDate.Month == 12 ) {
				decimal inflation = 1m + plan.AnnualInflationPercent / 100m;
				maximumCPP *= inflation;
				baseMaximum *= inflation;
				enhancedMaximum *= inflation;
			}
		}

		return result;
	}

	private static decimal CalculateCombinedComponent(
		decimal deceasedPension,
		decimal survivorPension
	) {
		decimal fullSurvivorPension = deceasedPension * SurvivorPensionRate;
		return fullSurvivorPension - Math.Min(
			fullSurvivorPension * CombinedPensionReductionRate,
			survivorPension * CombinedPensionReductionRate );
	}

	public IDictionary<CompiledPeriod, IDictionary<CompiledMember, decimal>> CompileOAS(
		Plan plan,
		IEnumerable<CompiledPeriod> periods,
		IEnumerable<CompiledMember> members
	) {
		IDictionary<CompiledPeriod, IDictionary<CompiledMember, decimal>> result = new Dictionary<CompiledPeriod, IDictionary<CompiledMember, decimal>>();
		decimal maximumOAS = plan.OASMaximum;
		foreach( CompiledPeriod period in periods ) {
			result[period] = new Dictionary<CompiledMember, decimal>();
			foreach( CompiledMember member in members ) {
				if( member.OASStartDate <= period.PeriodDate && member.DeathDate > period.PeriodDate ) {
						decimal oasMultiplier = member.OASMultiplier;
						// 10% top-up at age 75
						if( member.BirthDate.AddYears( 75 ) <= period.PeriodDate ) {
							oasMultiplier *= 1.1m;
						}
						result[period][member] = maximumOAS * oasMultiplier;
				} else {
					result[period][member] = 0.0m;
				}
			}

			if( period.PeriodDate.Month == 12 ) {
				maximumOAS *= 1 + ( plan.AnnualInflationPercent / 100 );
			}
		}

		return result;
	}
}
