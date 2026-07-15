namespace Planning.Model;

public static class ExtensionMethods {
	public static DateOnly EndOfMonth(
		this DateOnly period
	) {
		return new DateOnly( period.Year, period.Month, 1 ).AddMonths( 1 ).AddDays( -1 );
	}

	public static DateOnly EndOfPreviousMonth(
		this DateOnly period
	) {
		return new DateOnly( period.Year, period.Month, 1 ).AddDays( -1 );
	}

	public static DateOnly StartOfMonth(
		this DateOnly period
	) {
		return new DateOnly( period.Year, period.Month, 1 );
	}

	public static DateOnly StartOfNextMonth(
		this DateOnly period
	) {
		return new DateOnly( period.Year, period.Month, 1 ).AddMonths( 1 );

	}

	public static RangedValue? GetActive(
		this IEnumerable<RangedValue> rangedValues,
		DateOnly date
	) {
		return rangedValues
			.Where(rv => rv.StartDate <= date)
			.OrderByDescending(rv => rv.StartDate)
			.FirstOrDefault();
	}

	/// <summary>
	/// Formats a monetary value for display and reporting.
	/// </summary>
	/// <remarks>
	/// Monetary precision policy:
	/// <list type="bullet">
	/// <item>All monetary amounts are stored and computed as <see cref="decimal"/> to avoid
	/// binary floating-point representation errors.</item>
	/// <item>Full precision is retained throughout the calculation pipeline; rounding is applied
	/// only at the presentation boundary (here) so intermediate results are not lossy.</item>
	/// <item>Values are rounded to 2 decimal places (cents) using
	/// <see cref="MidpointRounding.ToEven"/> (banker's rounding) to minimize cumulative bias.</item>
	/// <item>Formatting uses <see cref="System.Globalization.CultureInfo.InvariantCulture"/> so
	/// output is deterministic across machine cultures.</item>
	/// </list>
	/// </remarks>
	public static string FormatRounded(
		this decimal value
	) {
		decimal rounded = Math.Round( value, 2, MidpointRounding.ToEven );
		string result = rounded.ToString( "F2", System.Globalization.CultureInfo.InvariantCulture );
		return result;
	}

}
