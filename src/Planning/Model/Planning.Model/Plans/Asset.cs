namespace Planning.Model.Plans;

/// <param name="ContributionBacklog">
/// The unused contribution room carried into the plan. Ignored, and normalized to zero, when
/// <paramref name="HasUnlimitedContributionRoom"/> is set.
/// </param>
/// <param name="AnnualContributionLimit">
/// The amount the backlog will grow annually (ie - RRSP contribution room increases each year
/// based on income, TFSA contribution room increases each year by a fixed amount). Ignored,
/// and normalized to zero, when <paramref name="HasUnlimitedContributionRoom"/> is set.
/// </param>
/// <param name="HasUnlimitedContributionRoom">
/// Whether the account has no contribution cap at all, as is the case for a non-registered
/// account. Such an account absorbs any amount deposited into it without consuming room, and
/// accrues no annual room, so both amounts above are meaningless and are forced to zero.
/// </param>
/// <param name="CostBase">
/// The adjusted cost base of the opening balance: the portion of <paramref name="Amount"/>
/// that is already-taxed capital and so is not subject to capital-gains tax when realized.
/// Only a <see cref="AssetTaxStatus.CapitalGains"/> account may carry a non-zero value; a
/// registered account's withdrawal is taxed as income, or not at all, so a cost base has no
/// meaning there and is rejected by validation rather than silently ignored.
/// </param>
public record Asset(
	string Name,
	AssetTaxStatus TaxStatus,
	string Member,
	decimal Amount,
	decimal ContributionBacklog,
	decimal AnnualContributionLimit,
	bool HasUnlimitedContributionRoom,
	decimal CostBase
) {

	/// <summary>
	/// Normalized so that an uncapped account can never also claim a finite backlog, which
	/// would be a contradictory state that callers would have to guess their way through.
	/// </summary>
	public decimal ContributionBacklog { get; init; } = HasUnlimitedContributionRoom ? 0m : ContributionBacklog;

	/// <inheritdoc cref="ContributionBacklog"/>
	public decimal AnnualContributionLimit { get; init; } = HasUnlimitedContributionRoom ? 0m : AnnualContributionLimit;

	public static readonly Asset None = new Asset( "", AssetTaxStatus.Unknown, "", 0.0m, 0.0m, 0.0m, false, 0.0m );
}
