namespace Planning.Model.Plans;

/// <summary>
/// Configures the taxable-account burndown strategy. Over <paramref name="BurndownYears"/>
/// calendar years, starting at the plan start date, the taxable accounts are drawn down on an
/// amortized schedule so that they reach zero at the end of the window. The withdrawals are made
/// in excess of the retirement income need, and the after-tax remainder is transferred into the
/// member's tax-exempt accounts and then their capital-gains accounts.
///
/// The strategy is optional; a plan without a burndown simply never draws its taxable accounts
/// down beyond what is needed to fund retirement income.
/// </summary>
public record Burndown(
	int BurndownYears
) {
	public static readonly Burndown None = new Burndown( 0 );
}
