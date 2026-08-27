using Planning.Model.Identifiers;

namespace Planning.Model.CompiledPlans;

/// <param name="CPPPercent">
/// The share of the maximum CPP pension the member actually receives, with the actuarial
/// adjustment for their chosen start age already applied.
/// </param>
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
