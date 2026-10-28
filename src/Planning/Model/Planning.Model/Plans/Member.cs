namespace Planning.Model.Plans;


/// <param name="CPPBasePercent">
/// The estimated share of the base CPP maximum at age 65, before start-age adjustment.
/// </param>
/// <param name="CPPFirstAdditionalPercent">
/// The estimated share of maximum first-additional accrual during eligible working months
/// from 2019 until retirement or CPP commencement, whichever comes first.
/// </param>
/// <param name="CPPSecondAdditionalPercent">
/// The estimated share of maximum second-additional accrual during eligible working months
/// from 2024 until retirement or CPP commencement, whichever comes first.
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
	decimal CPPBasePercent,
	int OASStartInYears,
	decimal CPPFirstAdditionalPercent,
	decimal CPPSecondAdditionalPercent
) {
	public static readonly Member None = new Member(
		Name: "",
		BirthDate: DateOnly.MinValue,
		TargetAgeInYears: 0,
		RetirementAgeInYears: null,
		CPPStartInYears: 0,
		CPPBasePercent: 0.0m,
		OASStartInYears: 65,
		CPPFirstAdditionalPercent: 0m,
		CPPSecondAdditionalPercent: 0m
	);
}
