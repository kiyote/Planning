namespace Planning.Model.CompiledPlans;

/// <summary>
/// Unadjusted monthly CPP components in plan-start dollars, before early/late-start adjustment.
/// </summary>
public record CompiledCPPEntitlement(
	decimal BaseMonthlyAt65,
	decimal FirstAdditionalMonthlyAt65,
	decimal SecondAdditionalMonthlyAt65
) {
	public decimal TotalMonthlyAt65 =>
		BaseMonthlyAt65 + FirstAdditionalMonthlyAt65 + SecondAdditionalMonthlyAt65;
}
