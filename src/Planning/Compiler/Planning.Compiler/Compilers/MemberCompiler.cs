using Planning.Model;
using Planning.Model.CompiledPlans;
using Planning.Model.Plans;

namespace Planning.Compiler.Compilers;

internal sealed class MemberCompiler {

	public IReadOnlyList<CompiledMember> Compile(
		Plan plan
	) {
		if( !plan.Members.Any( m => m.RetirementAgeInYears.HasValue ) ) {
			throw new InvalidOperationException( "At least one household member must specify a retirement age." );
		}

		// Members without a retirement age retire alongside the household. The shared retirement
		// date is the earliest date among members who did specify a retirement age.
		DateOnly sharedRetirementStart = plan.Members
			.Where( m => m.RetirementAgeInYears.HasValue )
			.Min( m => m.BirthDate.AddYears( m.RetirementAgeInYears!.Value ).StartOfNextMonth() );

		List<CompiledMember> members = [];

		foreach( Member member in plan.Members ) {
			DateOnly retirementStart = member.RetirementAgeInYears.HasValue
				? member.BirthDate.AddYears( member.RetirementAgeInYears.Value ).StartOfNextMonth()
				: sharedRetirementStart;

			members.Add(
				new CompiledMember(
					MemberId: members.Count + 1,
					Name: member.Name,
					BirthDate: member.BirthDate,
					DeathDate: member.BirthDate.AddYears( member.TargetAgeInYears ).EndOfMonth(),
					RetirementDate: retirementStart,
					CPPStartDate: member.BirthDate.AddYears( member.CPPStartInYears ).StartOfNextMonth(),
					OASStartDate: member.BirthDate.AddYears( 65 ).StartOfNextMonth(),
					CPPPercent: member.CPPPercent
				)
			);
		}

		return members;
	}
}
