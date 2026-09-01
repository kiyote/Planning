using Planning.Model;
using Planning.Model.CompiledPlans;
using Planning.Model.Plans;

namespace Planning.Compiler.Compilers;

/// <summary>
/// Resolves the plan's go-go/slow-go/no-go phases into the income the plan aims to deliver in each
/// period, inflated forward each December.
/// </summary>
internal sealed class DesiredIncomeCompiler {

	/// <summary>
	/// Resolves the lifestyle phase boundaries. The phases are laid out backwards from the end
	/// of the plan: No-Go occupies the final years, Slow-Go the span before it, and Go-Go runs
	/// from the household's earliest retirement date up to whatever is left.
	/// </summary>
	public RetirementPhaseSchedule CompileSchedule(
		Plan plan,
		IEnumerable<CompiledMember> members,
		IEnumerable<CompiledPeriod> periods
	) {
		DateOnly noGoEnd = periods.Last().PeriodDate.EndOfMonth();
		DateOnly noGoStart = noGoEnd.AddYears( -plan.RetirementIncome.NoGoYears ).StartOfMonth();
		DateOnly slowGoEnd = noGoStart.EndOfPreviousMonth();
		DateOnly slowGoStart = slowGoEnd.AddYears( -plan.RetirementIncome.SlowGoYears ).StartOfMonth();
		DateOnly goGoStart = members.Min( m => m.RetirementDate ).StartOfMonth();

		return new RetirementPhaseSchedule( goGoStart, slowGoStart, noGoStart );
	}

	/// <summary>
	/// Assigns each period the income its phase calls for, inflating every amount each December
	/// so that a later phase is expressed in the dollars of the year it is actually reached.
	/// </summary>
	/// <remarks>
	/// The phase amounts are inflated on every period, including those before retirement where
	/// the desired income is zero. That keeps a phase's purchasing power tied to the calendar
	/// rather than to when the household happens to retire.
	/// </remarks>
	public IDictionary<CompiledPeriod, decimal> Compile(
		Plan plan,
		IEnumerable<CompiledPeriod> periods,
		RetirementPhaseSchedule schedule
	) {
		IDictionary<CompiledPeriod, decimal> result = new Dictionary<CompiledPeriod, decimal>();
		decimal goGoAmount = plan.RetirementIncome.GoGo;
		decimal slowGoAmount = plan.RetirementIncome.SlowGo;
		decimal noGoAmount = plan.RetirementIncome.NoGo;
		foreach( CompiledPeriod period in periods ) {
			if( period.PeriodDate >= schedule.NoGoStart ) {
				result[period] = noGoAmount;
			} else if( period.PeriodDate >= schedule.SlowGoStart ) {
				result[period] = slowGoAmount;
			} else if( period.PeriodDate >= schedule.GoGoStart ) {
				result[period] = goGoAmount;
			} else {
				result[period] = 0.0m;
			}

			if( period.PeriodDate.Month == 12 ) {
				goGoAmount *= 1 + ( plan.AnnualInflationPercent / 100 );
				slowGoAmount *= 1 + ( plan.AnnualInflationPercent / 100 );
				noGoAmount *= 1 + ( plan.AnnualInflationPercent / 100 );
			}
		}
		return result;
	}
}
