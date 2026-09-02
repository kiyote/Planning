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

		decimal expected = ( 55_867m * 0.15m ) + ( ( 60_000m - 55_867m ) * 0.205m );
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

	[Test]
	public void CalculateBasicPersonalAmountCredit_ValuesTheAmountAtTheJurisdictionsLowestRate() {
		TaxCalculator calculator = new TaxCalculator();

		decimal credit = calculator.CalculateBasicPersonalAmountCredit( 15_705m, FederalBrackets, 1m );

		Assert.That( credit, Is.EqualTo( 15_705m * 0.15m ) );
	}

	[Test]
	public void CalculateBasicPersonalAmountCredit_ProvincialAmountAndBrackets_UseTheProvincialLowestRate() {
		// The provincial credit is a different amount valued at a different rate, which is why
		// the two jurisdictions are configured independently rather than sharing one figure.
		TaxCalculator calculator = new TaxCalculator();
		TaxBracket[] provincialBrackets = [new TaxBracket( LowerBound: 0m, Rate: 5.05m )];

		decimal credit = calculator.CalculateBasicPersonalAmountCredit( 12_399m, provincialBrackets, 1m );

		Assert.That( credit, Is.EqualTo( 12_399m * 0.0505m ) );
	}

	[Test]
	public void CalculateBasicPersonalAmountCredit_IsIndexed_SoItKeepsPaceWithTheBrackets() {
		TaxCalculator calculator = new TaxCalculator();

		decimal credit = calculator.CalculateBasicPersonalAmountCredit( 15_705m, FederalBrackets, 1.5m );

		Assert.That( credit, Is.EqualTo( 15_705m * 1.5m * 0.15m ) );
	}

	[Test]
	public void CalculateBasicPersonalAmountCredit_AmountOfZero_DisablesTheCredit() {
		TaxCalculator calculator = new TaxCalculator();

		Assert.That( calculator.CalculateBasicPersonalAmountCredit( 0m, FederalBrackets, 1m ), Is.Zero );
	}

	[Test]
	public void CalculateOntarioAgeAmountCredit_IneligibleMember_ReturnsZero() {
		TaxCalculator calculator = new TaxCalculator();

		Assert.That(
			calculator.CalculateOntarioAgeAmountCredit( CreatePolicy(), 30_000m, isEligible: false, 1m ),
			Is.Zero );
	}

	[Test]
	public void CalculateOntarioAgeAmountCredit_IncomeBelowThreshold_ValuesTheFullBaseAtTheProvincialRate() {
		// The provincial credit uses Ontario's own smaller base and is valued at the provincial
		// lowest rate, not the federal one.
		TaxCalculator calculator = new TaxCalculator();

		decimal credit = calculator.CalculateOntarioAgeAmountCredit( CreatePolicy(), 30_000m, isEligible: true, 1m );

		Assert.That( credit, Is.EqualTo( 5_810m * 0.0505m ) );
	}

	[Test]
	public void CalculateOntarioAgeAmountCredit_IncomeAboveThreshold_ReducesTheBaseByTheClawback() {
		TaxCalculator calculator = new TaxCalculator();

		decimal credit = calculator.CalculateOntarioAgeAmountCredit( CreatePolicy(), 50_000m, isEligible: true, 1m );

		decimal expectedAmount = 5_810m - ( ( 50_000m - 43_127m ) * 0.15m );
		Assert.That( credit, Is.EqualTo( expectedAmount * 0.0505m ) );
	}

	[Test]
	public void CalculateOntarioAgeAmountCredit_IncomeFullyClawsBackTheBase_ReturnsZero() {
		// The credit is non-refundable, so a fully eroded base yields nothing rather than a
		// negative amount that would increase provincial tax.
		TaxCalculator calculator = new TaxCalculator();

		Assert.That(
			calculator.CalculateOntarioAgeAmountCredit( CreatePolicy(), 500_000m, isEligible: true, 1m ),
			Is.Zero );
	}

	[Test]
	public void CalculateOntarioAgeAmountCredit_IsSmallerThanTheFederalCredit_BecauseOfTheLowerBaseAndRate() {
		// Guards against the provincial credit accidentally being wired to the federal amount
		// or federal brackets, which would silently overstate the relief.
		TaxCalculator calculator = new TaxCalculator();
		TaxPolicy policy = CreatePolicy();

		decimal federal = calculator.CalculateAgeAmountCredit( policy, 30_000m, isEligible: true, 1m );
		decimal ontario = calculator.CalculateOntarioAgeAmountCredit( policy, 30_000m, isEligible: true, 1m );

		Assert.That( ontario, Is.LessThan( federal ) );
	}

	[Test]
	public void CalculateOntarioAgeAmountCredit_IsIndexed_SoItKeepsPaceWithTheBrackets() {
		TaxCalculator calculator = new TaxCalculator();

		decimal credit = calculator.CalculateOntarioAgeAmountCredit( CreatePolicy(), 30_000m, isEligible: true, 2m );

		Assert.That( credit, Is.EqualTo( 5_810m * 2m * 0.0505m ) );
	}

	[Test]
	public void CalculateOntarioPensionIncomeCredit_NoEligibleIncome_ReturnsZero() {
		TaxCalculator calculator = new TaxCalculator();

		Assert.That( calculator.CalculateOntarioPensionIncomeCredit( CreatePolicy(), 0m, 1m ), Is.Zero );
	}

	[Test]
	public void CalculateOntarioPensionIncomeCredit_IncomeBelowTheAmount_ValuesOnlyTheIncomeReceived() {
		// The credit is capped at the pension income actually received, so a small pension
		// cannot claim the full amount.
		TaxCalculator calculator = new TaxCalculator();

		decimal credit = calculator.CalculateOntarioPensionIncomeCredit( CreatePolicy(), 1_000m, 1m );

		Assert.That( credit, Is.EqualTo( 1_000m * 0.0505m ) );
	}

	[Test]
	public void CalculateOntarioPensionIncomeCredit_IncomeAboveTheAmount_IsCappedAtTheOntarioAmount() {
		TaxCalculator calculator = new TaxCalculator();

		decimal credit = calculator.CalculateOntarioPensionIncomeCredit( CreatePolicy(), 50_000m, 1m );

		Assert.That( credit, Is.EqualTo( 1_641m * 0.0505m ) );
	}

	[Test]
	public void CalculateOntarioPensionIncomeCredit_IsIndexed_SoItKeepsPaceWithTheBrackets() {
		TaxCalculator calculator = new TaxCalculator();

		decimal credit = calculator.CalculateOntarioPensionIncomeCredit( CreatePolicy(), 50_000m, 2m );

		Assert.That( credit, Is.EqualTo( 1_641m * 2m * 0.0505m ) );
	}

	private static TaxPolicy CreatePolicy() {
		return new TaxPolicy(
			Year: 2024,
			FederalBrackets: FederalBrackets,
			ProvincialBrackets: [new TaxBracket( LowerBound: 0m, Rate: 5.05m )],
			AllowPensionSplitting: false,
			BasicPersonalAmount: 15_705m,
			ProvincialBasicPersonalAmount: 12_399m,
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
