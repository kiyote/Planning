using Planning.Model.CompiledPlans;
using Planning.Model.Plans;

namespace Planning.Compiler;

public class PlanCompiler {

	public CompiledPlan Compile(
		Plan plan
	) {
		PlanValidationResult validation = new PlanValidator().Validate( plan );
		if( !validation.IsValid ) {
			throw new PlanValidationException( validation.Errors );
		}

		IReadOnlyList<CompiledMember> members = [.. new Compilers.MemberCompiler().Compile( plan )];
		IReadOnlyList<CompiledPeriod> periods = [.. new Compilers.PeriodCompiler().Compile( plan, members )];
		IReadOnlyList<CompiledAsset> assets = [.. new Compilers.AssetCompiler().Compile( plan, members )];
		IDictionary<CompiledPeriod, IEnumerable<CompiledIncome>> scheduledIncome = new Compilers.ScheduledIncomeCompiler().Compile( plan, periods, members );
		Compilers.DesiredIncomeCompiler desiredIncomeCompiler = new Compilers.DesiredIncomeCompiler();
		RetirementPhaseSchedule retirementPhaseSchedule = desiredIncomeCompiler.CompileSchedule( plan, members, periods );
		IDictionary<CompiledPeriod, decimal> desiredIncome = desiredIncomeCompiler.Compile( plan, periods, retirementPhaseSchedule );
		IDictionary<CompiledPeriod, IEnumerable<CompiledContribution>> contributions = new Compilers.ContributionCompiler().Compile( plan, members, periods );
		CompiledBurndown burndown = new Compilers.BurndownCompiler().Compile( plan, members, assets, periods );

		return new CompiledPlan(
			periods,
			members,
			assets,
			scheduledIncome,
			desiredIncome,
			contributions,
			plan.TaxPolicy,
			retirementPhaseSchedule,
			burndown
		);
	}
}
