using Planning.Model.Identifiers;

namespace Planning.Model.CalculatedPlans;

public record CalculatedWithdrawal(
	AssetId AssetId,
	decimal Amount
);
