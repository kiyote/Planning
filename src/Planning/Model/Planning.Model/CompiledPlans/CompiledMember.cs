using Planning.Model.Identifiers;

namespace Planning.Model.CompiledPlans;

/// <param name="CPPPercent">
/// The share of the maximum CPP pension the member actually receives, with the actuarial
/// adjustment for their chosen start age already applied.
/// </param>
/// <param name="OASMultiplier">
/// The permanent multiplier applied to the maximum OAS amount for deferring OAS past age 65,
/// with 1.0 meaning no deferral bonus.
/// </param>
public record CompiledMember(
	MemberId MemberId,
	string Name,
	DateOnly BirthDate,
	DateOnly DeathDate,
	DateOnly RetirementDate,
	DateOnly CPPStartDate,
	DateOnly OASStartDate,
	decimal CPPPercent,
	decimal OASMultiplier
);
