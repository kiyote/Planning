using Planning.Model.Identifiers;
using Planning.Model.Plans;

namespace Planning.Model.CompiledPlans;

public record CompiledAsset(
	AssetId AssetId,
	string Name,
	AssetTaxStatus TaxStatus,
	MemberId MemberId,
	decimal Amount,
	decimal ContributionBacklog,
	decimal AnnualContributionLimit
);
