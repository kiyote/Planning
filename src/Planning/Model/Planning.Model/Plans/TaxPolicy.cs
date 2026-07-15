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
/// lowest federal bracket rate. Defaults to the CRA 2024 value ($8,790).
/// </param>
/// <param name="AgeAmountIncomeThreshold">
/// The net-income threshold above which the Age Amount is reduced, in nominal start-year
/// dollars. Defaults to the CRA 2024 value ($44,325).
/// </param>
/// <param name="AgeAmountReductionRate">
/// The percentage of net income above the threshold by which the Age Amount is reduced.
/// Defaults to the CRA value (15%).
/// </param>
/// <param name="AgeAmountEligibilityAge">
/// The age (in years, at year end) at which a member becomes eligible for the Age Amount.
/// Defaults to 65.
/// </param>
/// <param name="PensionIncomeAmount">
/// The federal Pension Income Amount base credit (non-refundable), in nominal start-year
/// dollars. A member with eligible pension income receives a credit valued at the lowest
/// federal bracket rate on the lesser of this amount and their eligible pension income.
/// Under the modelling assumption that a member's RRSP becomes a RRIF at retirement, their
/// RRSP (RRIF) withdrawal income after retirement qualifies as eligible pension income,
/// provided the member is at least <see cref="PensionIncomeEligibilityAge"/> at year end
/// (per CRA rules, RRIF income only qualifies from age 65). Defaults to the CRA value ($2,000).
/// </param>
/// <param name="PensionIncomeEligibilityAge">
/// The age (in years, at year end) at which a member's RRIF income becomes eligible for the
/// Pension Income Amount. Defaults to 65.
/// </param>
public record TaxPolicy(
	IEnumerable<TaxBracket> FederalBrackets,
	IEnumerable<TaxBracket> ProvincialBrackets,
	bool AllowPensionSplitting = false,
	decimal AgeAmountBase = 8_790m,
	decimal AgeAmountIncomeThreshold = 44_325m,
	decimal AgeAmountReductionRate = 15m,
	int AgeAmountEligibilityAge = 65,
	decimal PensionIncomeAmount = 2_000m,
	int PensionIncomeEligibilityAge = 65
);
