using Planning.Model.CompiledPlans;
using Planning.Model.Plans;

namespace Planning.Compiler.Compilers;

internal sealed class IncomeCompiler {

	private readonly GovernmentBenefitCompiler _benefitCompiler = new GovernmentBenefitCompiler();

	public IDictionary<CompiledPeriod, IEnumerable<CompiledIncome>> Compile(
		Plan plan,
		IEnumerable<CompiledPeriod> periods,
		IEnumerable<CompiledMember> members
	) {
		IDictionary<CompiledPeriod, IDictionary<CompiledMember, decimal>> memberCPP = _benefitCompiler.CompileCPP( plan, periods, members );
		IDictionary<CompiledPeriod, IDictionary<CompiledMember, decimal>> memberOAS = _benefitCompiler.CompileOAS( plan, periods, members );
		IDictionary<CompiledPeriod, IDictionary<CompiledMember, decimal>> memberCPPSurvivor = _benefitCompiler.CompileCPPSurvivor( plan, periods, members, memberCPP );

		IDictionary<CompiledPeriod, IEnumerable<CompiledIncome>> result = new Dictionary<CompiledPeriod, IEnumerable<CompiledIncome>>();

		foreach (CompiledPeriod period in periods) {
			List<CompiledIncome> incomes = [];

			foreach (CompiledMember member in members) {
				CompiledIncome income = new CompiledIncome(
					"CPP",
					member.MemberId,
					memberCPP[period][member],
					true
				);
				incomes.Add( income );

				income = new CompiledIncome(
					"OAS",
					member.MemberId,
					memberOAS[period][member],
					true
				);
				incomes.Add( income );

				income = new CompiledIncome(
					"CPP Survivor",
					member.MemberId,
					memberCPPSurvivor[period][member],
					true
				);
				incomes.Add( income );

				foreach (LifeInsurance lifeInsurance in plan.LifeInsurance.Where( li => li.MemberName == member.Name ) ) {
					decimal insuranceAmount = 0.0m;
					if( period.PeriodDate.Year == member.DeathDate.Year
						&& period.PeriodDate.Month == member.DeathDate.Month
					) {
						insuranceAmount = lifeInsurance.Amount;
					}
					income = new CompiledIncome(
						$"{lifeInsurance.MemberName} Life Insurance",
						member.MemberId,
						insuranceAmount,
						false
					);
					incomes.Add( income );

				}
			}

			result[period] = incomes;
		}

		return result;
	}
}
