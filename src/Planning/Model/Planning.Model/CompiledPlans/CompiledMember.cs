using Planning.Model.Identifiers;

namespace Planning.Model.CompiledPlans;

/// <param name="CPPEntitlement">
/// Separate unadjusted CPP components, retained for the survivor calculation.
/// </param>
/// <param name="CPPAdjustmentFactor">
/// The early/late-start multiplier applied to retirement CPP, not the deceased's survivor entitlement.
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
	CompiledCPPEntitlement CPPEntitlement,
	decimal CPPAdjustmentFactor,
	decimal OASMultiplier
);
