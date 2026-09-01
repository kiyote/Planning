using Planning.Model.CompiledPlans;
using Planning.Model.Plans;

namespace Planning.Compiler.Compilers;

/// <summary>
/// Builds the per-period, per-member schedule of income arriving from outside the plan's assets.
/// </summary>
internal sealed class ScheduledIncomeCompiler {

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

		foreach( CompiledPeriod period in periods ) {
			List<CompiledIncome> incomes = [];

			foreach( CompiledMember member in members ) {
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

				foreach( LifeInsurance lifeInsurance in plan.LifeInsurance.Where( li => li.Member == member.Name ) ) {
					decimal insuranceAmount = 0.0m;
					if( period.PeriodDate.Year == member.DeathDate.Year
						&& period.PeriodDate.Month == member.DeathDate.Month
					) {
						insuranceAmount = lifeInsurance.Amount;
					}
					income = new CompiledIncome(
						$"{lifeInsurance.Member} Life Insurance",
						member.MemberId,
						insuranceAmount,
						false
					);
					incomes.Add( income );

				}

				// An inheritance of no value is treated as though it were not configured at all,
				// so it contributes no income column.
				foreach( Inheritance inheritance in plan.Inheritance.Where( i => i.Member == member.Name && i.Amount > 0m ) ) {
					decimal inheritanceAmount = 0.0m;
					DateOnly receiptDate = member.BirthDate.AddYears( inheritance.AgeReceived );

					// The inheritance arrives once, in the month the member reaches the stated age,
					// and only while that member is alive to receive it.
					if( period.PeriodDate.Year == receiptDate.Year
						&& period.PeriodDate.Month == receiptDate.Month
						&& receiptDate <= member.DeathDate
					) {
						int elapsedYears = receiptDate.Year - plan.StartDate.Year;
						double inflation = (double)( 1 + ( plan.AnnualInflationPercent / 100 ) );

						inheritanceAmount = inheritance.Amount * (decimal)Math.Pow( inflation, elapsedYears );
					}

					income = new CompiledIncome(
						$"{inheritance.Member} Inheritance",
						member.MemberId,
						inheritanceAmount,
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
