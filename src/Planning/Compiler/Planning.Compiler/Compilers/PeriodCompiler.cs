using Planning.Model;
using Planning.Model.CompiledPlans;
using Planning.Model.Plans;

namespace Planning.Compiler.Compilers;

internal sealed class PeriodCompiler {

	public IReadOnlyList<CompiledPeriod> Compile(
		Plan plan,
		IReadOnlyList<CompiledMember> members
	) {
		DateOnly planStart = plan.StartDate.StartOfMonth();
		DateOnly planEnd = members.Max( m => m.DeathDate );

		List<CompiledPeriod> periods = [];
		DateOnly current = planStart;
		while( current < planEnd ) {
			periods.Add(
				new CompiledPeriod(
					periods.Count + 1,
					current
				)
			);
			current = current.StartOfNextMonth();
		}

		return periods;
	}
}
