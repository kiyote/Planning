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
		decimal inflationIndex = 1m;
		foreach( CompiledPeriod period in periods ) {

			result[period] = new Dictionary<CompiledMember, decimal>();
			foreach( CompiledMember member in members ) {
				if( member.CPPStartDate <= period.PeriodDate && member.DeathDate > period.PeriodDate ) {
					result[period][member] = member.CPPEntitlement.TotalMonthlyAt65
						* member.CPPAdjustmentFactor * inflationIndex;
				} else {
					result[period][member] = 0.0m;
				}
			}

			if( period.PeriodDate.Month == 12 ) {
				inflationIndex *= 1 + ( plan.AnnualInflationPercent / 100 );
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

		decimal baseMaximum = CPPEntitlementCompiler.EstimateBaseMaximum( plan );
		decimal inflationIndex = 1m;

		foreach( CompiledPeriod period in periods ) {
			result[period] = members.ToDictionary( m => m, _ => 0m );
			foreach( CompiledMember survivor in members ) {
				CompiledMember deceased = members.Single( m => m != survivor );
				if( survivor.DeathDate <= period.PeriodDate || deceased.DeathDate > period.PeriodDate ) {
					continue;
				}

				CompiledCPPEntitlement deceasedCPP = deceased.CPPEntitlement;
				CompiledCPPEntitlement survivorCPP = survivor.CPPEntitlement;
				decimal fullSurvivorPension = deceasedCPP.TotalMonthlyAt65 * SurvivorPensionRate;
				if( survivor.CPPStartDate > period.PeriodDate ) {
					result[period][survivor] = fullSurvivorPension * inflationIndex;
					continue;
				}

				decimal basePension = CalculateCombinedComponent(
					deceasedCPP.BaseMonthlyAt65, survivorCPP.BaseMonthlyAt65 );
				basePension = Math.Min( basePension, Math.Max( baseMaximum - survivorCPP.BaseMonthlyAt65, 0m ) );
				decimal firstPension = CalculateCombinedComponent(
					deceasedCPP.FirstAdditionalMonthlyAt65, survivorCPP.FirstAdditionalMonthlyAt65 );
				decimal secondPension = CalculateCombinedComponent(
					deceasedCPP.SecondAdditionalMonthlyAt65, survivorCPP.SecondAdditionalMonthlyAt65 );
				decimal survivorPension = basePension + firstPension + secondPension;
				result[period][survivor] = survivorPension * inflationIndex;

				// CPP Act 46(5): a reduction of the survivor pension also exempts that
				// amount of the retirement pension from an early-start reduction.
				memberCPP[period][survivor] += ( fullSurvivorPension - survivorPension )
					* Math.Max( 1m - survivor.CPPAdjustmentFactor, 0m ) * inflationIndex;
			}

			if( period.PeriodDate.Month == 12 ) {
				decimal inflation = 1m + plan.AnnualInflationPercent / 100m;
				inflationIndex *= inflation;
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
