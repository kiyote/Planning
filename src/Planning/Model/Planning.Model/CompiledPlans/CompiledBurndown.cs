using Planning.Model.Identifiers;

namespace Planning.Model.CompiledPlans;

/// <summary>
/// A scheduled burndown draw against a single taxable account in a single period.
///
/// The gross withdrawal is <see cref="AmortizationFactor"/> multiplied by the account's balance
/// at the time the period is calculated. The factor depends only on the plan's rate of return and
/// the number of years left in the owner's burndown window, both of which are known when the plan
/// is compiled; only the balance is a runtime value.
/// </summary>
public record CompiledBurndownWithdrawal(
	AssetId AssetId,
	MemberId MemberId,
	decimal AmortizationFactor
);

/// <summary>
/// The precompiled burndown schedule: for each period in which a burndown occurs, the fraction of
/// each eligible taxable account that is to be withdrawn.
///
/// Periods in which nothing is drawn down are absent from <see cref="Schedule"/>, so a missing key
/// means "no burndown this period".
/// </summary>
public record CompiledBurndown(
	int BurndownYears,
	IDictionary<CompiledPeriod, IEnumerable<CompiledBurndownWithdrawal>> Schedule
) {
	public static readonly CompiledBurndown None = new CompiledBurndown(
		0,
		new Dictionary<CompiledPeriod, IEnumerable<CompiledBurndownWithdrawal>>()
	);
}
