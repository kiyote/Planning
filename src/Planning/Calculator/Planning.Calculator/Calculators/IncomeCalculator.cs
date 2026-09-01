using Planning.Model.CompiledPlans;
using Planning.Model.CalculatedPlans;

namespace Planning.Calculator.Calculators;

internal sealed class IncomeCalculator {

	public IEnumerable<CalculatedIncome> CalculateTaxableIncome(
		CompiledPeriod period,
		CompiledPlan plan
	) {
		return Classify( period, plan, taxable: true );
	}

	public IEnumerable<CalculatedIncome> CalculateNonTaxableIncome(
		CompiledPeriod period,
		CompiledPlan plan
	) {
		return Classify( period, plan, taxable: false );
	}

	private static IEnumerable<CalculatedIncome> Classify(
		CompiledPeriod period,
		CompiledPlan plan,
		bool taxable
	) {
		List<CalculatedIncome> result = [];

		foreach( CompiledIncome income in plan.ScheduledIncome[period] ) {

			if( income.Taxable != taxable ) {
				continue;
			}

			CompiledMember member = plan.Members.First( m => m.MemberId == income.MemberId );
			CalculatedIncome calculatedIncome = new CalculatedIncome(
				member.MemberId,
				income.Name,
				income.Amount
			);
			result.Add( calculatedIncome );
		}
		return result;
	}
}
