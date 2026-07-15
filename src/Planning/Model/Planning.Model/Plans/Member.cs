namespace Planning.Model.Plans;


public record Member(
	string Name,
	DateOnly BirthDate,
	int TargetAgeInYears,
	int? RetirementAgeInYears,
	int CPPStartInYears,
	decimal CPPPercent
);
