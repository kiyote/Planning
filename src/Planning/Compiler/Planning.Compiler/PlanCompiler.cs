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
		IEnumerable<CompiledAsset> assets = new Compilers.AssetCompiler().Compile( plan, members );
		IDictionary<CompiledPeriod, IEnumerable<CompiledIncome>> income = new Compilers.IncomeCompiler().Compile( plan, periods, members );
		IDictionary<CompiledPeriod, decimal> retirementIncome = new Compilers.RetirementIncomeCompiler().Compile( plan, members, periods, out RetirementPhaseSchedule retirementPhaseSchedule );
		IDictionary<CompiledPeriod, IEnumerable<CompiledContribution>> contributions = new Compilers.ContributionCompiler().Compile( plan, members, periods );

		return new CompiledPlan(
			periods,
			members,
			assets,
			income,
			retirementIncome,
			contributions,
			plan.TaxPolicy,
			retirementPhaseSchedule,
			plan.Burndown
		);
	}
}
