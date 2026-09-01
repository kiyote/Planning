using Planning.Compiler.Compilers;
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

		IReadOnlyList<CompiledMember> members = [.. new MemberCompiler().Compile( plan )];
		IReadOnlyList<CompiledPeriod> periods = [.. new PeriodCompiler().Compile( plan, members )];
		IReadOnlyList<CompiledAsset> assets = [.. new AssetCompiler().Compile( plan, members )];
		IDictionary<CompiledPeriod, IEnumerable<CompiledIncome>> scheduledIncome = new ScheduledIncomeCompiler().Compile( plan, periods, members );
		DesiredIncomeCompiler desiredIncomeCompiler = new DesiredIncomeCompiler();
		RetirementPhaseSchedule retirementPhaseSchedule = desiredIncomeCompiler.CompileSchedule( plan, members, periods );
		IDictionary<CompiledPeriod, decimal> desiredIncome = desiredIncomeCompiler.Compile( plan, periods, retirementPhaseSchedule );
		IDictionary<CompiledPeriod, IEnumerable<CompiledContribution>> contributions = new ContributionCompiler().Compile( plan, members, periods );
		CompiledBurndown burndown = new BurndownCompiler().Compile( plan, members, assets, periods );

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
