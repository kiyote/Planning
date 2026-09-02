using Planning.Model.Plans;

namespace Planning.Calculator.Calculators;

/// <summary>
/// Computes progressive federal and provincial income tax for a taxable amount.
/// Bracket thresholds are expressed in nominal start-year dollars and are indexed by
/// inflation for the year being calculated so that real tax burden stays comparable.
/// </summary>
internal sealed class TaxCalculator {

	/// <summary>
	/// The basic provincial tax above which the first Ontario surtax rate applies, in 2024
	/// dollars. The 2024 value is $5,554.
	/// </summary>
	private const decimal OntarioSurtaxFirstThreshold = 5_554m;

	/// <summary>
	/// The basic provincial tax above which the second Ontario surtax rate applies in addition
	/// to the first, in 2024 dollars. The 2024 value is $7,108.
	/// </summary>
	private const decimal OntarioSurtaxSecondThreshold = 7_108m;

	private const decimal OntarioSurtaxFirstRate = 0.20m;
	private const decimal OntarioSurtaxSecondRate = 0.36m;

	/// <summary>
	/// Taxable income at or below which no Ontario Health Premium is payable.
	/// </summary>
	private const decimal OntarioHealthPremiumExemption = 20_000m;

	/// <summary>
	/// The Ontario Age Amount base, in 2024 dollars. Ontario runs its own Age Amount alongside
	/// the federal one, with a smaller base and a lower income threshold. The 2024 values are a
	/// $5,810 base reduced above $43,127.
	/// </summary>
	private const decimal OntarioAgeAmountBase = 5_810m;

	private const decimal OntarioAgeAmountIncomeThreshold = 43_127m;

	/// <summary>
	/// The Ontario Pension Income Amount, in 2024 dollars. Ontario's $1,641 amount is smaller
	/// than the federal $2,000.
	/// </summary>
	private const decimal OntarioPensionIncomeAmount = 1_641m;

	/// <summary>
	/// One band of the Ontario Health Premium. Each band contributes its own amount, phased in
	/// at <paramref name="Rate"/> on income above <paramref name="Threshold"/> and stopping once
	/// <paramref name="Cap"/> is reached. The bands accumulate, so the total premium is the sum
	/// of every band the member's income has entered.
	/// </summary>
	private sealed record OntarioHealthPremiumStep(
		decimal Threshold,
		decimal Rate,
		decimal Cap
	);

	/// <summary>
	/// The Ontario Health Premium bands, frozen at their 2004 values. Together they produce the
	/// published schedule: $300 by $25,000, $450 by $48,000, $600 by $72,000, $750 by $200,000,
	/// and $900 thereafter.
	/// </summary>
	private static readonly OntarioHealthPremiumStep[] OntarioHealthPremiumSteps = [
		new OntarioHealthPremiumStep( Threshold: 20_000m, Rate: 0.06m, Cap: 300m ),
		new OntarioHealthPremiumStep( Threshold: 36_000m, Rate: 0.06m, Cap: 150m ),
		new OntarioHealthPremiumStep( Threshold: 48_000m, Rate: 0.25m, Cap: 150m ),
		new OntarioHealthPremiumStep( Threshold: 72_000m, Rate: 0.25m, Cap: 150m ),
		new OntarioHealthPremiumStep( Threshold: 200_000m, Rate: 0.25m, Cap: 150m )
	];

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
	/// Computes the Basic Personal Amount non-refundable tax credit for a single jurisdiction.
	/// Every member is entitled to it regardless of age, income, or income type, which is what
	/// distinguishes it from the Age Amount and the Pension Income Amount. The amount is valued
	/// at that jurisdiction's lowest bracket rate, so the federal and provincial credits differ
	/// both in the amount claimed and in the rate it is valued at. The amount is expressed in
	/// nominal start-year dollars and indexed by inflation for the year being calculated.
	///
	/// The federal amount is reduced for members whose income reaches the top bracket. That
	/// phase-out is deliberately not modelled, so the credit is slightly overstated for the
	/// highest incomes.
	/// </summary>
	/// <param name="basicPersonalAmount">The jurisdiction's basic personal amount, in nominal start-year dollars.</param>
	/// <param name="brackets">That jurisdiction's brackets, whose lowest rate values the credit.</param>
	/// <param name="inflationIndex">Multiplier applied to the amount to index it for the year.</param>
	/// <returns>The tax reduction (never negative) provided by the Basic Personal Amount credit.</returns>
	public decimal CalculateBasicPersonalAmountCredit(
		decimal basicPersonalAmount,
		IEnumerable<TaxBracket> brackets,
		decimal inflationIndex
	) {
		if( basicPersonalAmount <= 0m ) {
			return 0m;
		}

		decimal lowestRate = LowestBracketRate( brackets );
		return basicPersonalAmount * inflationIndex * ( lowestRate / 100m );
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
		if( !isEligible
			|| policy is null
			|| policy.AgeAmountBase <= 0m
		) {
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
		if( policy is null
			|| policy.PensionIncomeAmount <= 0m
			|| eligiblePensionIncome <= 0m
		) {
			return 0m;
		}

		decimal eligibleAmount = Math.Min( policy.PensionIncomeAmount * inflationIndex, eligiblePensionIncome );
		decimal lowestRate = LowestBracketRate( policy.FederalBrackets );
		return eligibleAmount * ( lowestRate / 100m );
	}

	/// <summary>
	/// Computes the Ontario Age Amount non-refundable tax credit for a member.
	/// Ontario runs its own Age Amount in parallel with the federal one, so an eligible member
	/// claims both. It differs in two ways: the base amount and income threshold are smaller,
	/// and the resulting amount is valued at the lowest provincial rather than federal bracket
	/// rate. The reduction rate and eligibility age are shared with the federal rules and so are
	/// taken from the policy rather than hardcoded.
	/// </summary>
	/// <param name="policy">The tax policy carrying the shared reduction rate and the provincial brackets.</param>
	/// <param name="netIncome">The member's net income for the year (their taxable base).</param>
	/// <param name="isEligible">Whether the member is old enough to claim the Age Amount at year end.</param>
	/// <param name="inflationIndex">Multiplier applied to the base amount and threshold to index them for the year.</param>
	/// <returns>The provincial tax reduction (never negative) provided by the Ontario Age Amount credit.</returns>
	public decimal CalculateOntarioAgeAmountCredit(
		TaxPolicy policy,
		decimal netIncome,
		bool isEligible,
		decimal inflationIndex
	) {
		if( !isEligible
			|| policy is null
		) {
			return 0m;
		}

		decimal baseAmount = OntarioAgeAmountBase * inflationIndex;
		decimal threshold = OntarioAgeAmountIncomeThreshold * inflationIndex;

		decimal reduction = netIncome > threshold
			? ( netIncome - threshold ) * ( policy.AgeAmountReductionRate / 100m )
			: 0m;

		decimal eligibleAmount = Math.Max( 0m, baseAmount - reduction );
		if( eligibleAmount <= 0m ) {
			return 0m;
		}

		decimal lowestRate = LowestBracketRate( policy.ProvincialBrackets );
		return eligibleAmount * ( lowestRate / 100m );
	}

	/// <summary>
	/// Computes the Ontario Pension Income Amount non-refundable tax credit for a member.
	/// As with the Age Amount, this is claimed in addition to the federal credit, using a
	/// smaller base amount and valued at the lowest provincial bracket rate.
	/// </summary>
	/// <param name="policy">The tax policy carrying the provincial brackets.</param>
	/// <param name="eligiblePensionIncome">The member's eligible pension income for the year (e.g. RRIF withdrawals).</param>
	/// <param name="inflationIndex">Multiplier applied to the base amount to index it for the year.</param>
	/// <returns>The provincial tax reduction (never negative) provided by the Ontario Pension Income Amount credit.</returns>
	public decimal CalculateOntarioPensionIncomeCredit(
		TaxPolicy policy,
		decimal eligiblePensionIncome,
		decimal inflationIndex
	) {
		if( policy is null
			|| eligiblePensionIncome <= 0m
		) {
			return 0m;
		}

		decimal eligibleAmount = Math.Min( OntarioPensionIncomeAmount * inflationIndex, eligiblePensionIncome );
		decimal lowestRate = LowestBracketRate( policy.ProvincialBrackets );
		return eligibleAmount * ( lowestRate / 100m );
	}

	/// <summary>
	/// Computes the Ontario surtax, an additional provincial tax charged on provincial tax
	/// itself rather than on income. Two thresholds apply cumulatively: 20% of basic provincial
	/// tax above the first, plus a further 36% above the second, so income in the top range
	/// attracts both. It is calculated after provincial credits have been applied, because the
	/// base it charges is tax payable rather than tax before credits.
	///
	/// The thresholds are the Ontario 2024 values and are indexed by inflation for the year
	/// being calculated so they keep pace with the brackets.
	/// </summary>
	/// <param name="provincialTaxAfterCredits">Basic provincial tax remaining once credits are applied.</param>
	/// <param name="inflationIndex">Multiplier applied to the thresholds to index them for the year.</param>
	/// <returns>The additional provincial tax (never negative) owed as surtax.</returns>
	public decimal CalculateOntarioSurtax(
		decimal provincialTaxAfterCredits,
		decimal inflationIndex
	) {
		if( provincialTaxAfterCredits <= 0m ) {
			return 0m;
		}

		decimal firstThreshold = OntarioSurtaxFirstThreshold * inflationIndex;
		decimal secondThreshold = OntarioSurtaxSecondThreshold * inflationIndex;

		decimal surtax = 0m;

		if( provincialTaxAfterCredits > firstThreshold ) {
			surtax += ( provincialTaxAfterCredits - firstThreshold ) * OntarioSurtaxFirstRate;
		}

		if( provincialTaxAfterCredits > secondThreshold ) {
			surtax += ( provincialTaxAfterCredits - secondThreshold ) * OntarioSurtaxSecondRate;
		}

		return surtax;
	}

	/// <summary>
	/// Computes the Ontario Health Premium, a flat-rate levy charged on taxable income that is
	/// neither a credit nor part of the bracket system. It steps up through fixed amounts, each
	/// phased in at a rate on income above the step's threshold until the step's cap is reached,
	/// so the charge rises smoothly rather than jumping at each boundary.
	///
	/// The premium has been frozen at its 2004 values since introduction and is not indexed, so
	/// unlike every other amount here it takes no inflation index. In real terms it therefore
	/// shrinks over a long projection, which is the correct behaviour.
	/// </summary>
	/// <param name="taxableIncome">The member's taxable income for the year.</param>
	/// <returns>The Ontario Health Premium (never negative) owed for the year.</returns>
	public decimal CalculateOntarioHealthPremium(
		decimal taxableIncome
	) {
		if( taxableIncome <= OntarioHealthPremiumExemption ) {
			return 0m;
		}

		decimal premium = 0m;

		foreach( OntarioHealthPremiumStep step in OntarioHealthPremiumSteps ) {
			if( taxableIncome <= step.Threshold ) {
				break;
			}

			decimal phasedIn = ( taxableIncome - step.Threshold ) * step.Rate;
			premium += Math.Min( phasedIn, step.Cap );
		}

		return premium;
	}

	/// <summary>
	/// Computes the OAS recovery tax (clawback) for a member.
	/// Unlike the Age Amount and Pension Income Amount, this is not a credit: it is an additional
	/// federal tax charged on net income above the policy threshold. The recovered amount is
	/// capped at the OAS actually received in the year, because a member can never repay more
	/// OAS than they were paid. The threshold is expressed in nominal start-year dollars and is
	/// indexed by inflation for the year being calculated.
	/// </summary>
	/// <param name="policy">The tax policy carrying the clawback threshold and rate.</param>
	/// <param name="netIncome">The member's net income for the year (their taxable base).</param>
	/// <param name="oasReceived">The OAS income the member received during the year.</param>
	/// <param name="inflationIndex">Multiplier applied to the threshold to index it for the year.</param>
	/// <returns>The additional federal tax (never negative) owed as OAS recovery tax.</returns>
	public decimal CalculateOasClawback(
		TaxPolicy policy,
		decimal netIncome,
		decimal oasReceived,
		decimal inflationIndex
	) {
		if( policy is null
			|| policy.OasClawbackThreshold <= 0m
			|| policy.OasClawbackRate <= 0m
			|| oasReceived <= 0m
		) {
			return 0m;
		}

		decimal threshold = policy.OasClawbackThreshold * inflationIndex;
		if( netIncome <= threshold ) {
			return 0m;
		}

		decimal recovered = ( netIncome - threshold ) * ( policy.OasClawbackRate / 100m );
		return Math.Min( recovered, oasReceived );
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
