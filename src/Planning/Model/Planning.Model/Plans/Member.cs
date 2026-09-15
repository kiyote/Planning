namespace Planning.Model.Plans;


/// <param name="CPPPercent">
/// The share of the maximum CPP pension the member has earned as at age 65. The compiler applies
/// the actuarial adjustment for <paramref name="CPPStartInYears"/> on top of this, so it should
/// not be pre-adjusted for taking the pension early or late.
/// </param>
/// <param name="OASStartInYears">
/// The age at which the member begins receiving OAS. Unlike CPP, OAS has no early-start option;
/// the valid range is 65 (the standard age) through 70. The compiler applies a permanent 0.6%
/// increase per month deferred past 65 on top of the configured maximum OAS amount.
/// </param>
public record Member(
	string Name,
	DateOnly BirthDate,
	int TargetAgeInYears,
	int? RetirementAgeInYears,
	int CPPStartInYears,
	decimal CPPPercent,
	int OASStartInYears
) {
	public static readonly Member None = new Member(
		Name: "",
		BirthDate: DateOnly.MinValue,
		TargetAgeInYears: 0,
		RetirementAgeInYears: null,
		CPPStartInYears: 0,
		CPPPercent: 0.0m,
		OASStartInYears: 65
	);
}
