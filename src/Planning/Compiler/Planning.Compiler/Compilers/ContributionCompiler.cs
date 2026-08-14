using Planning.Model.CompiledPlans;
using Planning.Model.Plans;

namespace Planning.Compiler.Compilers;

internal sealed class ContributionCompiler {

	public IDictionary<CompiledPeriod, IEnumerable<CompiledContribution>> Compile(
		Plan plan,
		IEnumerable<CompiledMember> members,
		IEnumerable<CompiledPeriod> periods
	) {
		IDictionary<CompiledPeriod, IEnumerable<CompiledContribution>> result = new Dictionary<CompiledPeriod, IEnumerable<CompiledContribution>>();
		foreach( CompiledPeriod period in periods ) {
			List<CompiledContribution> contributions = [];
			foreach( Contribution contribution in plan.Contributions ) {
				decimal contributionAmount = 0.0m;
				CompiledMember member = members.Single( m => m.Name == contribution.Member );

				if( period.PeriodDate.Year >= contribution.StartYear
					&& period.PeriodDate.Year < member.RetirementDate.Year
				) {
					if( contribution.Indexed ) {
						int elapsedYears = period.PeriodDate.Year - plan.StartDate.Year;
						double inflation = (double)(1 + plan.AnnualInflationPercent / 100);

						contributionAmount = contribution.Amount * (decimal)Math.Pow( inflation, elapsedYears );
					} else {
						contributionAmount = contribution.Amount;
					}
				}
				CompiledContribution cc = new CompiledContribution(
					contributions.Count + 1,
					member.MemberId,
					contributionAmount
				);

				contributions.Add( cc );
			}

			result[period] = contributions;
		}
		return result;
	}
}
