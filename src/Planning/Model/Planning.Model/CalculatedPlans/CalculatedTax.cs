using Planning.Model.Identifiers;

namespace Planning.Model.CalculatedPlans;

/// <summary>
/// The income tax accrued by a single member for a calendar year, settled in December.
/// </summary>
/// <param name="MemberId">The member the tax applies to.</param>
/// <param name="TaxableAmount">The member's total taxable base for the year (taxable income plus taxable-account withdrawals).</param>
/// <param name="FederalTax">Federal income tax owed on the taxable amount.</param>
/// <param name="ProvincialTax">Provincial income tax owed on the taxable amount.</param>
public record CalculatedTax(
	MemberId MemberId,
	decimal TaxableAmount,
	decimal FederalTax,
	decimal ProvincialTax
) {

	/// <summary>The combined federal and provincial tax owed.</summary>
	public decimal TotalTax => FederalTax + ProvincialTax;
}
