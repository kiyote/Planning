using Planning.Model.Plans;

namespace Planning.Calculator.Calculators;

/// <summary>
/// Computes progressive federal and provincial income tax for a taxable amount.
/// Bracket thresholds are expressed in nominal start-year dollars and are indexed by
/// inflation for the year being calculated so that real tax burden stays comparable.
/// </summary>
internal sealed class TaxCalculator {

	/// <summary>
	/// Computes tax owed for a single jurisdiction's brackets on the supplied taxable amount.
	/// </summary>
	/// <param name="brackets">Progressive brackets in nominal start-year dollars.</param>
	/// <param name="taxableAmount">The taxable amount for the year.</param>
	/// <param name="inflationIndex">Multiplier applied to bracket thresholds to index them for the year (1.0 = no indexing).</param>
	public decimal CalculateTax(
		IEnumerable<TaxBracket> brackets,
		decimal taxableAmount,
		decimal inflationIndex
	) {
		if( taxableAmount <= 0m || brackets is null ) {
			return 0m;
		}

		List<TaxBracket> ordered = [.. brackets.OrderBy( b => b.LowerBound )];
		if( ordered.Count == 0 ) {
			return 0m;
		}

		decimal tax = 0m;

		for( int i = 0; i < ordered.Count; i++ ) {
			decimal lowerBound = ordered[i].LowerBound * inflationIndex;

			if( taxableAmount <= lowerBound ) {
				break;
			}

			decimal upperBound = i + 1 < ordered.Count
				? ordered[i + 1].LowerBound * inflationIndex
				: decimal.MaxValue;

			decimal amountInBracket = Math.Min( taxableAmount, upperBound ) - lowerBound;
			tax += amountInBracket * ( ordered[i].Rate / 100m );
		}

		return tax;
	}

	/// <summary>
	/// Computes the federal Age Amount non-refundable tax credit for a member.
	/// The base amount is reduced by the reduction rate applied to net income above the
	/// threshold, and the resulting amount is valued at the lowest federal bracket rate.
	/// Thresholds and the base amount are expressed in nominal start-year dollars and indexed
	/// by inflation for the year being calculated.
	/// </summary>
	/// <param name="policy">The tax policy carrying the Age Amount parameters and federal brackets.</param>
	/// <param name="netIncome">The member's net income for the year (their taxable base).</param>
	/// <param name="isEligible">Whether the member is old enough to claim the Age Amount at year end.</param>
	/// <param name="inflationIndex">Multiplier applied to the base amount and threshold to index them for the year.</param>
	/// <returns>The federal tax reduction (never negative) provided by the Age Amount credit.</returns>
	public decimal CalculateAgeAmountCredit(
		TaxPolicy policy,
		decimal netIncome,
		bool isEligible,
		decimal inflationIndex
	) {
		if( !isEligible || policy is null || policy.AgeAmountBase <= 0m ) {
			return 0m;
		}

		decimal baseAmount = policy.AgeAmountBase * inflationIndex;
		decimal threshold = policy.AgeAmountIncomeThreshold * inflationIndex;

		decimal reduction = netIncome > threshold
			? ( netIncome - threshold ) * ( policy.AgeAmountReductionRate / 100m )
			: 0m;

		decimal eligibleAmount = Math.Max( 0m, baseAmount - reduction );
		if( eligibleAmount <= 0m ) {
			return 0m;
		}

		decimal lowestRate = LowestBracketRate( policy.FederalBrackets );
		return eligibleAmount * ( lowestRate / 100m );
	}

	/// <summary>
	/// Computes the federal Pension Income Amount non-refundable tax credit for a member.
	/// The credit is valued at the lowest federal bracket rate on the lesser of the policy's
	/// pension income base amount and the member's eligible pension income. The base amount is
	/// expressed in nominal start-year dollars and indexed by inflation for the year.
	/// </summary>
	/// <param name="policy">The tax policy carrying the pension income amount and federal brackets.</param>
	/// <param name="eligiblePensionIncome">The member's eligible pension income for the year (e.g. RRIF withdrawals).</param>
	/// <param name="inflationIndex">Multiplier applied to the base amount to index it for the year.</param>
	/// <returns>The federal tax reduction (never negative) provided by the Pension Income Amount credit.</returns>
	public decimal CalculatePensionIncomeCredit(
		TaxPolicy policy,
		decimal eligiblePensionIncome,
		decimal inflationIndex
	) {
		if( policy is null || policy.PensionIncomeAmount <= 0m || eligiblePensionIncome <= 0m ) {
			return 0m;
		}

		decimal eligibleAmount = Math.Min( policy.PensionIncomeAmount * inflationIndex, eligiblePensionIncome );
		decimal lowestRate = LowestBracketRate( policy.FederalBrackets );
		return eligibleAmount * ( lowestRate / 100m );
	}

	private static decimal LowestBracketRate( IEnumerable<TaxBracket> brackets ) {
		if( brackets is null ) {
			return 0m;
		}

		TaxBracket? lowest = brackets
			.OrderBy( b => b.LowerBound )
			.FirstOrDefault();
		return lowest?.Rate ?? 0m;
	}
}
