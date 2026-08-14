using Planning.Model.Identifiers;

namespace Planning.Model.CalculatedPlans;

public record CalculatedPeriod(
	PeriodNumber PeriodNumber,
	DateOnly PeriodDate,
	IReadOnlyList<CalculatedAsset> StartingAssets,
	IReadOnlyList<CalculatedIncome> TaxableIncome,
	IReadOnlyList<CalculatedIncome> NonTaxableIncome,
	IReadOnlyList<CalculatedContribution> Contribution,
	IReadOnlyList<CalculatedWithdrawal> Withdrawals,
	IReadOnlyList<CalculatedAsset> EndingAssets,
	decimal TotalTaxableIncome,
	decimal TotalNonTaxableIncome,
	decimal TotalIncome,
	decimal DesiredRetirementIncome,
	decimal RetirementIncomeShortfall,
	decimal TotalAssets,
	decimal ActualRetirementIncome,
	decimal RequestedWithdrawal,
	decimal ActualWithdrawal,
	decimal UnfundedShortfall,
	bool PlanExhausted,
	IReadOnlyList<CalculatedTax> Taxes,
	decimal TotalTax,
	decimal TaxFundingWithdrawal,
	decimal UnfundedTax,
	decimal BurndownWithdrawal,
	decimal BurndownTax,
	decimal BurndownTransfer,
	decimal RrifMinimumWithdrawal = 0m,
	decimal RrifMinimumTransfer = 0m
);
