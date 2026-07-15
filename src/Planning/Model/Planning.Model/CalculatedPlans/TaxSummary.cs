namespace Planning.Model.CalculatedPlans;

/// <summary>
/// A plan-level roll-up of income tax across the whole projection.
/// </summary>
/// <param name="TotalFederalTax">Total federal income tax accrued across all years.</param>
/// <param name="TotalProvincialTax">Total provincial income tax accrued across all years.</param>
/// <param name="TotalTax">Total combined income tax accrued across all years.</param>
public record TaxSummary(
	decimal TotalFederalTax,
	decimal TotalProvincialTax,
	decimal TotalTax
);
