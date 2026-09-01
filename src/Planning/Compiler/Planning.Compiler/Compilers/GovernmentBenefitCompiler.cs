using Planning.Model.CompiledPlans;
using Planning.Model.Plans;

namespace Planning.Compiler.Compilers;

internal sealed class GovernmentBenefitCompiler {

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
				maximumCPP *= 1 + (plan.AnnualInflationPercent / 100);
			}
		}

		return result;
	}

	public IDictionary<CompiledPeriod, IDictionary<CompiledMember, decimal>> CompileCPPSurvivor(
		Plan plan,
		IEnumerable<CompiledPeriod> periods,
		IEnumerable<CompiledMember> members,
		IDictionary<CompiledPeriod, IDictionary<CompiledMember, decimal>> memberCPP
	) {
		IDictionary<CompiledPeriod, IDictionary<CompiledMember, decimal>> result = new Dictionary<CompiledPeriod, IDictionary<CompiledMember, decimal>>();

		decimal regularCPP = plan.CPPMaximum;
		decimal potentialBenefit = plan.CPPCombinedSurvivorMaximum;
		foreach( CompiledPeriod period in periods ) {
			result[period] = new Dictionary<CompiledMember, decimal>();
			foreach( CompiledMember member in members ) {
				if( member.DeathDate <= period.PeriodDate ) {

					CompiledMember survivor = members.Single( m => m != member );
					if( survivor.DeathDate > period.PeriodDate ) {
						decimal partnerBenefit = regularCPP * ( member.CPPPercent / 100 ) * 0.6m;
						decimal survivorCPP = memberCPP[period][survivor];
						decimal toppedUp = survivorCPP + partnerBenefit;
						toppedUp = Math.Min( toppedUp, potentialBenefit );
						decimal actualBenefit = Math.Max( 0, toppedUp - survivorCPP );

						result[period][survivor] = actualBenefit;
					}
					result[period][member] = 0.0m;

				} else {
					if( !result[period].ContainsKey( member ) ) {
						result[period][member] = 0.0m;
					}
				}
			}

			if( period.PeriodDate.Month == 12 ) {
				potentialBenefit *= 1 + (plan.AnnualInflationPercent / 100);
				regularCPP *= 1 + (plan.AnnualInflationPercent / 100);
			}
		}

		return result;
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
					decimal oasMultiplier = 1.0m;
					// 10% top-up at age 75
					if( member.BirthDate.AddYears( 75 ) <= period.PeriodDate ) {
						oasMultiplier = 1.1m;
					}
					result[period][member] = maximumOAS * oasMultiplier;
				} else {
					result[period][member] = 0.0m;
				}
			}

			if( period.PeriodDate.Month == 12 ) {
				maximumOAS *= 1 + (plan.AnnualInflationPercent / 100);
			}
		}

		return result;
	}
}
