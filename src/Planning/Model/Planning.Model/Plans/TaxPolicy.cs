namespace Planning.Model.Plans;

/// <summary>
/// Configurable progressive income-tax brackets for a jurisdiction pair (federal and
/// provincial). Brackets are expressed in nominal start-year dollars; the calculator is
/// responsible for indexing thresholds by inflation over the projection horizon.
/// </summary>
/// <param name="FederalBrackets">Federal progressive tax brackets, ordered ascending by lower bound.</param>
/// <param name="ProvincialBrackets">Provincial progressive tax brackets, ordered ascending by lower bound.</param>
/// <param name="AllowPensionSplitting">
/// When enabled, up to 50% of a member's RRSP withdrawal income may be reallocated to their
/// spouse before progressive tax is computed, provided both members are age 65 or older at
/// year end. Only RRSP (Taxable-status) withdrawal income is eligible for splitting.
/// </param>
/// <param name="AgeAmountBase">
/// The federal Age Amount base credit amount (non-refundable), in nominal start-year dollars.
/// A member age <see cref="AgeAmountEligibilityAge"/> or older at year end is entitled to this
/// amount, reduced by <see cref="AgeAmountReductionRate"/> of net income above
/// <see cref="AgeAmountIncomeThreshold"/>. The resulting amount reduces federal tax at the
/// lowest federal bracket rate. The CRA 2024 value is $8,790.
/// </param>
/// <param name="AgeAmountIncomeThreshold">
/// The net-income threshold above which the Age Amount is reduced, in nominal start-year
/// dollars. The CRA 2024 value is $44,325.
/// </param>
/// <param name="AgeAmountReductionRate">
/// The percentage of net income above the threshold by which the Age Amount is reduced.
/// The CRA value is 15%.
/// </param>
/// <param name="AgeAmountEligibilityAge">
/// The age (in years, at year end) at which a member becomes eligible for the Age Amount.
/// Conventionally 65.
/// </param>
/// <param name="PensionIncomeAmount">
/// The federal Pension Income Amount base credit (non-refundable), in nominal start-year
/// dollars. A member with eligible pension income receives a credit valued at the lowest
/// federal bracket rate on the lesser of this amount and their eligible pension income.
/// Under the modelling assumption that a member's RRSP becomes a RRIF at retirement, their
/// RRSP (RRIF) withdrawal income after retirement qualifies as eligible pension income,
/// provided the member is at least <see cref="PensionIncomeEligibilityAge"/> at year end
/// (per CRA rules, RRIF income only qualifies from age 65). The CRA value is $2,000.
/// </param>
/// <param name="PensionIncomeEligibilityAge">
/// The age (in years, at year end) at which a member's RRIF income becomes eligible for the
/// Pension Income Amount. Conventionally 65.
/// </param>
/// <param name="RrifMinimums">
/// The mandatory minimum RRIF withdrawal factors by age, ordered ascending by age. A member
/// must withdraw at least the factor for their age at the start of the year, applied to the
/// account's January 1 balance. When empty, no minimum withdrawal is imposed.
/// </param>
/// <param name="OasClawbackThreshold">
/// The net income threshold above which the OAS recovery tax (clawback) applies, in nominal
/// start-year dollars. Unlike the Age Amount, this is a recovery tax rather than a credit: it
/// is added to federal tax rather than reducing it. The CRA value is $90,997.
/// Set to zero to disable the clawback.
/// </param>
/// <param name="OasClawbackRate">
/// The rate, as a percentage, at which OAS is recovered on net income above
/// <see cref="OasClawbackThreshold"/>. The recovered amount is capped at the OAS actually
/// received in the year, so a member can never repay more than they were paid. The CRA value
/// is 15%.
/// </param>
public record TaxPolicy(
	IEnumerable<TaxBracket> FederalBrackets,
	IEnumerable<TaxBracket> ProvincialBrackets,
	bool AllowPensionSplitting,
	decimal AgeAmountBase,
	decimal AgeAmountIncomeThreshold,
	decimal AgeAmountReductionRate,
	int AgeAmountEligibilityAge,
	decimal PensionIncomeAmount,
	int PensionIncomeEligibilityAge,
	IEnumerable<RrifMinimum>? RrifMinimums,
	decimal OasClawbackThreshold,
	decimal OasClawbackRate
) {
	public static readonly TaxPolicy None = new TaxPolicy(
		FederalBrackets: [],
		ProvincialBrackets: [],
		AllowPensionSplitting: false,
		AgeAmountBase: 0m,
		AgeAmountIncomeThreshold: 0m,
		AgeAmountReductionRate: 0m,
		AgeAmountEligibilityAge: 0,
		PensionIncomeAmount: 0m,
		PensionIncomeEligibilityAge: 0,
		RrifMinimums: [],
		OasClawbackThreshold: 0m,
		OasClawbackRate: 0m
	);
}
