namespace Planning.Model.CalculatedPlans;

/// <summary>
/// A plan-level roll-up of what the estate is actually worth once the final tax bill has been
/// settled. This is the figure to compare between strategies: comparing tax paid alone is
/// misleading, because a strategy can show a low tax bill simply by ending up with fewer
/// assets to be taxed on.
/// </summary>
/// <param name="GrossEstate">The total assets still held at the end of the projection, before the terminal tax bill.</param>
/// <param name="TerminalTax">The tax falling due on the final return, deemed payable out of the estate.</param>
/// <param name="FinalPeriodYear">The calendar year of the final projected period, used to discount to plan-start dollars.</param>
/// <param name="PlanStartYear">The calendar year the plan begins, which defines the value of a "today" dollar.</param>
/// <param name="AnnualInflationPercent">The annual inflation rate used to discount the estate to plan-start dollars.</param>
public record EstateSummary(
	decimal GrossEstate,
	decimal TerminalTax,
	int FinalPeriodYear,
	int PlanStartYear,
	decimal AnnualInflationPercent
) {

	/// <summary>
	/// The assets remaining after the terminal tax bill is paid: what the estate is genuinely
	/// worth to its beneficiaries.
	/// </summary>
	public decimal NetEstate => GrossEstate - TerminalTax;

	/// <summary>
	/// The <see cref="NetEstate"/> discounted back to plan-start dollars. A projection running
	/// for decades ends in heavily inflated dollars, so the nominal figure overstates the real
	/// purchasing power the estate represents.
	/// </summary>
	public decimal NetEstateInPlanStartDollars {
		get {
			int years = FinalPeriodYear - PlanStartYear;
			if( years <= 0 ) {
				return NetEstate;
			}

			decimal deflator = 1m;
			decimal rate = 1m + AnnualInflationPercent / 100m;
			for( int i = 0; i < years; i++ ) {
				deflator *= rate;
			}

			return deflator == 0m ? NetEstate : NetEstate / deflator;
		}
	}
}
