using Planning.Model.Identifiers;

namespace Planning.Model.CompiledPlans;

public record CompiledIncome(
	string Name,
	MemberId MemberId,
	decimal Amount,
	bool Taxable
);
