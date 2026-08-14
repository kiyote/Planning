namespace Planning.Model.Plans;

/// <summary>
/// The mandatory minimum withdrawal factor for a RRIF at a given age. Once an RRSP is
/// converted to a RRIF, the holder must withdraw at least this percentage of the account's
/// fair market value as at January 1 of each year, regardless of whether the income is
/// needed. This is what makes RRIF minimums matter to a projection: they force taxable
/// income out of the account even when a strategy would prefer to defer it.
/// </summary>
/// <param name="Age">The holder's age at the start of the year (on January 1).</param>
/// <param name="Percent">
/// The minimum percentage of the January 1 balance that must be withdrawn during the year,
/// expressed as a percentage (for example, 5.28 for 5.28%).
/// </param>
public record RrifMinimum(
	int Age,
	decimal Percent
);
