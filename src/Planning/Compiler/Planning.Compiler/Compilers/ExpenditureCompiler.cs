using Planning.Model.CompiledPlans;
using Planning.Model.Plans;

namespace Planning.Compiler.Compilers;

internal sealed class ExpenditureCompiler {

	public IReadOnlyList<CompiledExpenditure> Compile(
		Plan plan,
		IReadOnlyList<CompiledPeriod> periods,
		RetirementPhaseSchedule schedule
	) {
		List<CompiledExpenditure> result = [];

		foreach( CompiledPeriod period in periods ) {
			if( period.PeriodDate.Month != 1
				|| period.PeriodDate < plan.StartDate
				|| period.PeriodDate < schedule.GoGoStart
			) {
				continue;
			}

			foreach( Expenditure expenditure in plan.Expenditures ) {
				int elapsedYears = period.PeriodDate.Year - expenditure.StartYear;
				if( expenditure.Amount == 0m || elapsedYears < 0 ) {
					continue;
				}

				bool isDue = expenditure.Repeats
					? elapsedYears % expenditure.IntervalYears!.Value == 0
					: elapsedYears == 0;
				if( !isDue ) {
					continue;
				}

				// Amount is in StartYear dollars, including when that year predates the plan.
				decimal amount = expenditure.Indexed
					? expenditure.Amount * (decimal)Math.Pow(
						(double)( 1m + ( plan.AnnualInflationPercent / 100m ) ), elapsedYears )
					: expenditure.Amount;

				result.Add( new CompiledExpenditure( period, expenditure.Name, amount ) );
			}
		}

		return result;
	}
}
