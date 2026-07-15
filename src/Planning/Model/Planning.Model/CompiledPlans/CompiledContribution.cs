using Planning.Model.Identifiers;

namespace Planning.Model.CompiledPlans;

public record CompiledContribution(
	int ContributionId,
	AssetId AssetId,
	decimal Amount
);
