using Planning.Model.Identifiers;

namespace Planning.Model.CalculatedPlans;

public record CalculatedContribution(
	AssetId AssetId,
	decimal Amount
);
