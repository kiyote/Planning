using Planning.Model.Identifiers;

namespace Planning.Model.CompiledPlans;

public record CompiledContribution(
	int ContributionId,
	MemberId MemberId,
	decimal Amount
);
