using Planning.Model.Identifiers;

namespace Planning.Model.CalculatedPlans;

public record InsufficientFundsSummary(
	bool HasShortfall,
	DateOnly? FirstShortfallDate,
	PeriodNumber? FirstShortfallPeriod,
	int ShortfallPeriodCount,
	decimal TotalUnfundedShortfall
);
