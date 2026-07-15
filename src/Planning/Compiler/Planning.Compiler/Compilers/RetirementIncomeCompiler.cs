using Planning.Model;
using Planning.Model.CompiledPlans;
using Planning.Model.Plans;

namespace Planning.Compiler.Compilers;

internal sealed class RetirementIncomeCompiler {

	public IDictionary<CompiledPeriod, decimal> Compile(
		Plan plan,
		IEnumerable<CompiledMember> members,
		IEnumerable<CompiledPeriod> periods
	) {
		return Compile( plan, members, periods, out _ );
	}

	public IDictionary<CompiledPeriod, decimal> Compile(
		Plan plan,
		IEnumerable<CompiledMember> members,
		IEnumerable<CompiledPeriod> periods,
		out RetirementPhaseSchedule schedule
	) {
		DateOnly noGoEnd = periods.Last().PeriodDate.EndOfMonth();
		DateOnly noGoStart = noGoEnd.AddYears( -plan.RetirementIncome.NoGoYears ).StartOfMonth();
		DateOnly slowGoEnd = noGoStart.EndOfPreviousMonth();
		DateOnly slowGoStart = slowGoEnd.AddYears( -plan.RetirementIncome.SlowGoYears ).StartOfMonth();
		DateOnly goGoEnd = slowGoStart.EndOfPreviousMonth();
		DateOnly goGoStart = members.Min( m => m.RetirementDate ).StartOfMonth();

		schedule = new RetirementPhaseSchedule( goGoStart, slowGoStart, noGoStart );

		IDictionary<CompiledPeriod, decimal> result = new Dictionary<CompiledPeriod, decimal>();
		decimal goGoAmount = plan.RetirementIncome.GoGo;
		decimal slowGoAmount = plan.RetirementIncome.SlowGo;
		decimal noGoAmount = plan.RetirementIncome.NoGo;
		foreach( CompiledPeriod period in periods ) {
			if( period.PeriodDate >= noGoStart ) {
				result[period] = noGoAmount;
			} else if( period.PeriodDate >= slowGoStart ) {
				result[period] = slowGoAmount;
			} else if( period.PeriodDate >= goGoStart ) {
				result[period] = goGoAmount;
			} else {
				result[period] = 0.0m;
			}

			if( period.PeriodDate.Month == 12 ) {
				goGoAmount *= 1 + plan.AnnualInflationPercent / 100;
				slowGoAmount *= 1 + plan.AnnualInflationPercent / 100;
				noGoAmount *= 1 + plan.AnnualInflationPercent / 100;
			}
		}
		return result;
	}
}
