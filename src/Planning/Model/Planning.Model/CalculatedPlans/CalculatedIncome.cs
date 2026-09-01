using Planning.Model.Identifiers;

namespace Planning.Model.CalculatedPlans;

public record CalculatedIncome(
	MemberId MemberId,
	string Name,
	decimal Amount
);
