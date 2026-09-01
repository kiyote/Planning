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

				// The annuitant receives the funds, but a spousal contribution is funded by the
				// other member, so the contributor governs both the room and the stop date.
				CompiledMember destination = members.Single( m => m.Name == contribution.Member );
				CompiledMember contributor = members.Single( m => m.Name == contribution.Contributor );

				// Contributions are funded from employment income, so they run through the month
				// before the member retires and stop from the retirement month onward. Both dates
				// are the first of a month, so the comparison lands exactly on that boundary.
				if( period.PeriodDate.Year >= contribution.StartYear
					&& period.PeriodDate < contributor.RetirementDate
				) {
					if( contribution.Indexed ) {
						int elapsedYears = period.PeriodDate.Year - plan.StartDate.Year;
						double inflation = (double)(1 + (plan.AnnualInflationPercent / 100));

						contributionAmount = contribution.Amount * (decimal)Math.Pow( inflation, elapsedYears );
					} else {
						contributionAmount = contribution.Amount;
					}
				}
				CompiledContribution cc = new CompiledContribution(
					new( contributions.Count + 1 ),
					contributor.MemberId,
					destination.MemberId,
					contributionAmount
				);

				contributions.Add( cc );
			}

			result[period] = contributions;
		}
		return result;
	}
}
