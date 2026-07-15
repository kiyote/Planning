using Planning.Model.Identifiers;

namespace Planning.Model.CalculatedPlans;

public record CalculatedAsset(
	AssetId AssetId,
	decimal Amount
);
