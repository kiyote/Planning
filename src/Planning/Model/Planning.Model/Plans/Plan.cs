namespace Planning.Model.Plans;

public record Plan(
	DateOnly StartDate,
	IEnumerable<Member> Members,
	decimal CPPMaximum,
	decimal CPPCombinedSurvivorMaximum,
	decimal OASMaximum,
	IEnumerable<Asset> Assets,
	decimal AnnualInflationPercent,
	decimal AnnualReturnPercent,
	IEnumerable<LifeInsurance> LifeInsurance,
	RetirementIncome RetirementIncome,
	IEnumerable<Contribution> Contributions,
	TaxPolicy TaxPolicy,
	Burndown? Burndown = null
) {

	/// <summary>
	/// The burndown strategy, or <c>null</c> when it is not in use. A configured burndown of zero
	/// years is normalized to <c>null</c> so that it is treated as disabled everywhere.
	/// </summary>
	public Burndown? Burndown { get; init; } = Burndown?.BurndownYears == 0 ? null : Burndown;

	/// <summary>
	/// The plan's assets, guaranteed to include an account of every <see cref="AssetTaxStatus"/>
	/// for every member. Any account a plan does not define is synthesized with a zero balance and
	/// no contribution room, so that transfers between members - such as the rollover to a
	/// surviving spouse - always have a destination of matching tax status and never need to fall
	/// back to an account that would change the tax treatment of the balance.
	/// </summary>
	public IEnumerable<Asset> Assets { get; init; } = AddMissingAssets( Members, Assets );

	private static IEnumerable<Asset> AddMissingAssets(
		IEnumerable<Member> members,
		IEnumerable<Asset> assets
	) {
		List<Asset> result = [.. assets];

		foreach( Member member in members ) {
			foreach( AssetTaxStatus status in Enum.GetValues<AssetTaxStatus>() ) {
				if( result.Any( a => a.Member == member.Name && a.TaxStatus == status ) ) {
					continue;
				}

				result.Add( new Asset(
					Name: status.ToString(),
					TaxStatus: status,
					Member: member.Name,
					Amount: 0m,
					ReturnPercentages: [],
					StartDate: default,
					ContributionBacklog: 0m,
					AnnualContributionLimit: 0m
				) );
			}
		}

		return result;
	}
}
