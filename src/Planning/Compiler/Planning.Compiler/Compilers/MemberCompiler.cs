using Planning.Model;
using Planning.Model.CompiledPlans;
using Planning.Model.Plans;

namespace Planning.Compiler.Compilers;

internal sealed class MemberCompiler {

	/// <summary>
	/// The age at which CPP is paid at its unadjusted rate. Starting earlier permanently reduces
	/// the pension and starting later permanently increases it.
	/// </summary>
	private const int CPPStandardStartAge = 65;

	/// <summary>
	/// The permanent reduction, per month, for each month CPP is taken before
	/// <see cref="CPPStandardStartAge"/>. Taking CPP at 60 therefore pays 64% of the age-65 amount.
	/// </summary>
	private const decimal CPPEarlyReductionPercentPerMonth = 0.6m;

	/// <summary>
	/// The permanent increase, per month, for each month CPP is deferred past
	/// <see cref="CPPStandardStartAge"/>. Deferring to 70 therefore pays 142% of the age-65 amount.
	/// </summary>
	private const decimal CPPDeferralIncreasePercentPerMonth = 0.7m;

	/// <summary>
	/// The age at which OAS is paid at its unadjusted rate. Unlike CPP, OAS has no early-start
	/// option, so deferring past this age only ever increases the amount.
	/// </summary>
	private const int OASStandardStartAge = 65;

	/// <summary>
	/// The permanent increase, per month, for each month OAS is deferred past
	/// <see cref="OASStandardStartAge"/>. Deferring to 70 therefore pays 136% of the age-65 amount.
	/// </summary>
	private const decimal OASDeferralIncreasePercentPerMonth = 0.6m;

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
					MemberId: new( members.Count + 1 ),
					Name: member.Name,
					BirthDate: member.BirthDate,
					DeathDate: member.BirthDate.AddYears( member.TargetAgeInYears ).EndOfMonth(),
					RetirementDate: retirementStart,
					CPPStartDate: member.BirthDate.AddYears( member.CPPStartInYears ).StartOfNextMonth(),
					OASStartDate: member.BirthDate.AddYears( member.OASStartInYears ).StartOfNextMonth(),
					CPPPercent: AdjustCPPForStartAge( member.CPPPercent, member.CPPStartInYears ),
					OASMultiplier: AdjustOASForStartAge( member.OASStartInYears )
				)
			);
		}

		return members;
	}

	/// <summary>
	/// Applies the CPP actuarial adjustment to a member's entitlement. The configured
	/// <see cref="Member.CPPPercent"/> is the share of the maximum pension the member has earned
	/// as at <see cref="CPPStandardStartAge"/>; taking the pension earlier or later than that age
	/// permanently scales it, so the effective percent carried on the compiled member already
	/// includes the adjustment.
	/// </summary>
	private static decimal AdjustCPPForStartAge(
		decimal cppPercent,
		int cppStartInYears
	) {
		int monthsFromStandard = ( cppStartInYears - CPPStandardStartAge ) * 12;

		decimal factor = monthsFromStandard < 0
			? 1m - ( -monthsFromStandard * CPPEarlyReductionPercentPerMonth / 100m )
			: 1m + ( monthsFromStandard * CPPDeferralIncreasePercentPerMonth / 100m );

		return cppPercent * factor;
	}

	/// <summary>
	/// Computes the permanent OAS deferral multiplier for a member's chosen start age. There is
	/// no early-start option for OAS, so <paramref name="oasStartInYears"/> is expected to be at
	/// or after <see cref="OASStandardStartAge"/>; deferring past it increases the multiplier
	/// above 1.0.
	/// </summary>
	private static decimal AdjustOASForStartAge(
		int oasStartInYears
	) {
		int monthsFromStandard = ( oasStartInYears - OASStandardStartAge ) * 12;

		return 1m + ( monthsFromStandard * OASDeferralIncreasePercentPerMonth / 100m );
	}
}
