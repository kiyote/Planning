using Planning.Model.Identifiers;

namespace Planning.Model.CompiledPlans;

public record CompiledMember(
	MemberId MemberId,
	string Name,
	DateOnly BirthDate,
	DateOnly DeathDate,
	DateOnly RetirementDate,
	DateOnly CPPStartDate,
	DateOnly OASStartDate,
	decimal CPPPercent
);
