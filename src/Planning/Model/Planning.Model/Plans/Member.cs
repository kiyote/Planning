namespace Planning.Model.Plans;


/// <param name="CPPPercent">
/// The share of the maximum CPP pension the member has earned as at age 65. The compiler applies
/// the actuarial adjustment for <paramref name="CPPStartInYears"/> on top of this, so it should
/// not be pre-adjusted for taking the pension early or late.
/// </param>
public record Member(
	string Name,
	DateOnly BirthDate,
	int TargetAgeInYears,
	int? RetirementAgeInYears,
	int CPPStartInYears,
	decimal CPPPercent
) {
	public static readonly Member None = new Member(
		Name: "",
		BirthDate: DateOnly.MinValue,
		TargetAgeInYears: 0,
		RetirementAgeInYears: null,
		CPPStartInYears: 0,
		CPPPercent: 0.0m
	);
}
