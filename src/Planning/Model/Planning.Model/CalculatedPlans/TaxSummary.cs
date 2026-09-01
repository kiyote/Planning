namespace Planning.Model.CalculatedPlans;

/// <summary>
/// A plan-level roll-up of income tax across the whole projection.
/// </summary>
/// <param name="TotalFederalTax">Total federal income tax accrued across all years.</param>
/// <param name="TotalProvincialTax">Total provincial income tax accrued across all years.</param>
/// <param name="TotalTax">Total combined income tax accrued across all years.</param>
/// <param name="TerminalFederalTax">Federal tax on assets remaining at the death of the last surviving member.</param>
/// <param name="TerminalProvincialTax">Provincial tax on assets remaining at the death of the last surviving member.</param>
public record TaxSummary(
	decimal TotalFederalTax,
	decimal TotalProvincialTax,
	decimal TotalTax,
	decimal TerminalFederalTax,
	decimal TerminalProvincialTax
) {

	/// <summary>
	/// The combined tax falling due on the final return, when the estate is deemed to have
	/// disposed of everything still held at the death of the last surviving member.
	/// </summary>
	public decimal TerminalTax => TerminalFederalTax + TerminalProvincialTax;

	/// <summary>
	/// The lifetime tax cost of the plan: tax paid while alive plus the terminal bill. This is a
	/// better measure than <see cref="TotalTax"/> alone, since a strategy that defers tax shows a
	/// low <see cref="TotalTax"/> while leaving a large liability at death.
	/// <para>
	/// This is still not a sound basis for comparing strategies on its own. Tax paid scales with
	/// wealth, so a strategy can post a lower bill purely by ending up poorer. Compare
	/// <see cref="EstateSummary.NetEstate"/> instead, which measures what is actually left over.
	/// </para>
	/// </summary>
	public decimal TotalTaxIncludingTerminal => TotalTax + TerminalTax;
}
