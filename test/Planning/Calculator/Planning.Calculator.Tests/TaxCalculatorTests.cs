using Planning.Calculator.Calculators;
using Planning.Model.Plans;

namespace Planning.Calculator.Tests;

public class TaxCalculatorTests {

	private static readonly TaxBracket[] FederalBrackets = [
		new TaxBracket( LowerBound: 0m, Rate: 15.0m ),
		new TaxBracket( LowerBound: 55_867m, Rate: 20.5m ),
		new TaxBracket( LowerBound: 111_733m, Rate: 26.0m )
	];

	[Test]
	public void CalculateTax_ZeroOrNegativeAmount_ReturnsZero() {
		TaxCalculator calculator = new TaxCalculator();

		Assert.Multiple( () => {
			Assert.That( calculator.CalculateTax( FederalBrackets, 0m, 1m ), Is.Zero );
			Assert.That( calculator.CalculateTax( FederalBrackets, -100m, 1m ), Is.Zero );
		} );
	}

	[Test]
	public void CalculateTax_WithinLowestBracket_AppliesLowestRate() {
		TaxCalculator calculator = new TaxCalculator();

		decimal tax = calculator.CalculateTax( FederalBrackets, 10_000m, 1m );

		Assert.That( tax, Is.EqualTo( 10_000m * 0.15m ) );
	}

	[Test]
	public void CalculateTax_SpanningTwoBrackets_AppliesProgressiveRates() {
		TaxCalculator calculator = new TaxCalculator();

		decimal tax = calculator.CalculateTax( FederalBrackets, 60_000m, 1m );

		decimal expected = (55_867m * 0.15m) + (( 60_000m - 55_867m ) * 0.205m);
		Assert.That( tax, Is.EqualTo( expected ) );
	}

	[Test]
	public void CalculateTax_InflationIndex_ScalesBracketThresholds() {
		TaxCalculator calculator = new TaxCalculator();

		// With a 10% index the lowest-bracket ceiling rises, keeping the whole amount at 15%.
		decimal tax = calculator.CalculateTax( FederalBrackets, 60_000m, 1.1m );

		Assert.That( tax, Is.EqualTo( 60_000m * 0.15m ) );
	}

	[Test]
	public void CalculateAgeAmountCredit_NotEligible_ReturnsZero() {
		TaxCalculator calculator = new TaxCalculator();
		TaxPolicy policy = CreatePolicy();

		decimal credit = calculator.CalculateAgeAmountCredit( policy, netIncome: 20_000m, isEligible: false, inflationIndex: 1m );

		Assert.That( credit, Is.Zero );
	}

	[Test]
	public void CalculateAgeAmountCredit_IncomeBelowThreshold_ValuesFullBaseAtLowestRate() {
		TaxCalculator calculator = new TaxCalculator();
		TaxPolicy policy = CreatePolicy();

		decimal credit = calculator.CalculateAgeAmountCredit( policy, netIncome: 20_000m, isEligible: true, inflationIndex: 1m );

		// Income below the threshold: full base valued at the lowest federal rate (15%).
		Assert.That( credit, Is.EqualTo( policy.AgeAmountBase * 0.15m ) );
	}

	[Test]
	public void CalculateAgeAmountCredit_IncomeAboveThreshold_ReducesBaseByClawback() {
		TaxCalculator calculator = new TaxCalculator();
		TaxPolicy policy = CreatePolicy();

		decimal netIncome = 60_000m;
		decimal credit = calculator.CalculateAgeAmountCredit( policy, netIncome, isEligible: true, inflationIndex: 1m );

		decimal reduction = ( netIncome - policy.AgeAmountIncomeThreshold ) * ( policy.AgeAmountReductionRate / 100m );
		decimal expected = ( policy.AgeAmountBase - reduction ) * 0.15m;
		Assert.That( credit, Is.EqualTo( expected ) );
	}

	[Test]
	public void CalculateAgeAmountCredit_IncomeFullyClawsBackBase_ReturnsZero() {
		TaxCalculator calculator = new TaxCalculator();
		TaxPolicy policy = CreatePolicy();

		// Income high enough that the 15% clawback wipes out the entire base amount.
		decimal netIncome = 200_000m;
		decimal credit = calculator.CalculateAgeAmountCredit( policy, netIncome, isEligible: true, inflationIndex: 1m );

		Assert.That( credit, Is.Zero );
	}

	[Test]
	public void CalculatePensionIncomeCredit_NoEligibleIncome_ReturnsZero() {
		TaxCalculator calculator = new TaxCalculator();
		TaxPolicy policy = CreatePolicy();

		decimal credit = calculator.CalculatePensionIncomeCredit( policy, eligiblePensionIncome: 0m, inflationIndex: 1m );

		Assert.That( credit, Is.Zero );
	}

	[Test]
	public void CalculatePensionIncomeCredit_IncomeAboveBase_CapsAtBaseAmount() {
		TaxCalculator calculator = new TaxCalculator();
		TaxPolicy policy = CreatePolicy();

		decimal credit = calculator.CalculatePensionIncomeCredit( policy, eligiblePensionIncome: 50_000m, inflationIndex: 1m );

		// Capped at the base amount, valued at the lowest federal rate (15%).
		Assert.That( credit, Is.EqualTo( policy.PensionIncomeAmount * 0.15m ) );
	}

	[Test]
	public void CalculatePensionIncomeCredit_IncomeBelowBase_ValuesActualIncome() {
		TaxCalculator calculator = new TaxCalculator();
		TaxPolicy policy = CreatePolicy();

		decimal eligible = 1_200m;
		decimal credit = calculator.CalculatePensionIncomeCredit( policy, eligible, inflationIndex: 1m );

		Assert.That( credit, Is.EqualTo( eligible * 0.15m ) );
	}

	[Test]
	public void CalculatePensionIncomeCredit_InflationIndex_ScalesBaseAmount() {
		TaxCalculator calculator = new TaxCalculator();
		TaxPolicy policy = CreatePolicy();

		// With a 10% index the base rises to 2,200; income of 2,100 stays below it.
		decimal credit = calculator.CalculatePensionIncomeCredit( policy, eligiblePensionIncome: 2_100m, inflationIndex: 1.1m );

		Assert.That( credit, Is.EqualTo( 2_100m * 0.15m ) );
	}

	private static TaxPolicy CreatePolicy() {
		return new TaxPolicy(
			Year: 2024,
			FederalBrackets: FederalBrackets,
			ProvincialBrackets: [ new TaxBracket( LowerBound: 0m, Rate: 5.05m ) ],
			AllowPensionSplitting: false,
			AgeAmountBase: 8_790m,
			AgeAmountIncomeThreshold: 44_325m,
			AgeAmountReductionRate: 15m,
			AgeAmountEligibilityAge: 65,
			PensionIncomeAmount: 2_000m,
			PensionIncomeEligibilityAge: 65,
			RrifMinimums: null,
			OasClawbackThreshold: 90_997m,
			OasClawbackRate: 15m
		);
	}
}
