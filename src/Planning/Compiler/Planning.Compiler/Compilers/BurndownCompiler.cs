using Planning.Model.CompiledPlans;
using Planning.Model.Plans;

namespace Planning.Compiler.Compilers;

/// <summary>
/// Builds the burndown schedule: which taxable accounts are drawn down in which periods, and what
/// fraction of their balance is taken each time.
/// </summary>
internal sealed class BurndownCompiler {

	public CompiledBurndown Compile(
		Plan plan,
		IEnumerable<CompiledMember> members,
		IEnumerable<CompiledAsset> assets,
		IEnumerable<CompiledPeriod> periods
	) {
		int burndownYears = plan.Burndown.BurndownYears;

		// A plan without a burndown never draws its taxable accounts down beyond what is needed
		// to fund retirement income, so there is no schedule to build.
		if( burndownYears <= 0 ) {
			return CompiledBurndown.None;
		}

		// Every asset grows at the plan-wide rate, so a single return assumption drives every
		// account's amortization.
		decimal rate = plan.AnnualReturnPercent / 100m;

		Dictionary<CompiledPeriod, IEnumerable<CompiledBurndownWithdrawal>> schedule = [];

		foreach( CompiledPeriod period in periods ) {
			// The burndown runs once per calendar year, in December.
			if( period.PeriodDate.Month != 12 ) {
				continue;
			}

			List<CompiledBurndownWithdrawal> withdrawals = [];
			foreach( CompiledAsset asset in assets ) {
				if( asset.TaxStatus != AssetTaxStatus.Taxable ) {
					continue;
				}

				CompiledMember owner = members.Single( m => m.MemberId == asset.MemberId );

				// Each account's window opens when its owner retires and runs for the configured
				// number of years thereafter, so members retiring at different times burn down on
				// their own schedules.
				if( period.PeriodDate < owner.RetirementDate ) {
					continue;
				}

				int remainingYears = burndownYears - ( period.PeriodDate.Year - owner.RetirementDate.Year );
				if( remainingYears <= 0 ) {
					continue;
				}

				withdrawals.Add( new CompiledBurndownWithdrawal(
					asset.AssetId,
					asset.MemberId,
					AmortizationFactor( rate, remainingYears )
				) );
			}

			if( withdrawals.Count > 0 ) {
				schedule[period] = withdrawals;
			}
		}

		return new CompiledBurndown( burndownYears, schedule );
	}

	/// <summary>
	/// The fraction of a balance that a level annual payment must take to retire that balance over
	/// <paramref name="years"/> years while it continues to earn <paramref name="rate"/>.
	///
	/// This is the balance-independent half of the amortized payment: the payment itself is simply
	/// this factor multiplied by the balance at the time of the withdrawal.
	/// </summary>
	private static decimal AmortizationFactor(
		decimal rate,
		int years
	) {
		if( years <= 1 || rate <= 0m ) {
			return years <= 1 ? 1m : 1m / years;
		}

		double discountFactor = 1d - Math.Pow( 1d + (double)rate, -years );

		return rate / (decimal)discountFactor;
	}
}
