using Planning.Compiler;
using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Calculator.Tests;

/// <summary>
/// Covers the Basic Personal Amount, the credit every member claims regardless of age, income,
/// or income type. It is the only credit that reduces provincial tax as well as federal.
/// </summary>
[TestFixture]
public sealed class BasicPersonalAmountTests {

	private const string MemberTodd = "Todd";
	private const string MemberTina = "Tina";

	[Test]
	public void Calculate_IncomeEntirelyBelowTheBasicPersonalAmount_PaysNoTax() {
		// Income under the personal amount is fully sheltered in both jurisdictions, so a
		// household living on a small RRIF draw should owe nothing at all.
		CalculatedPlan calculatedPlan = Calculate( monthlyIncome: 800m, basicPersonalAmount: 15_705m );

		Assert.That(
			calculatedPlan.TaxSummary.TotalTax,
			Is.Zero,
			"Income below the personal amount must be fully sheltered." );
	}

	[Test]
	public void Calculate_BasicPersonalAmountDisabled_TaxesIncomeFromTheFirstDollar() {
		// Guards the wiring: with the credit switched off the same scenario must become taxable,
		// proving the zero above is the credit working rather than there being no income.
		CalculatedPlan calculatedPlan = Calculate( monthlyIncome: 800m, basicPersonalAmount: 0m );

		Assert.That( calculatedPlan.TaxSummary.TotalTax, Is.GreaterThan( 0m ) );
	}

	[Test]
	public void Calculate_BasicPersonalAmountIsClaimed_ReducesBothFederalAndProvincialTax() {
		// Unlike the Age Amount, which is federal only, the personal amount is claimed in both
		// jurisdictions. A high enough income keeps both bills positive so each can be compared.
		CalculatedPlan withCredit = Calculate( monthlyIncome: 8_000m, basicPersonalAmount: 15_705m );
		CalculatedPlan withoutCredit = Calculate( monthlyIncome: 8_000m, basicPersonalAmount: 0m );

		using( Assert.EnterMultipleScope() ) {
			Assert.That(
				withCredit.TaxSummary.TotalFederalTax,
				Is.LessThan( withoutCredit.TaxSummary.TotalFederalTax ) );
			Assert.That(
				withCredit.TaxSummary.TotalProvincialTax,
				Is.LessThan( withoutCredit.TaxSummary.TotalProvincialTax ),
				"The personal amount reduces provincial tax too, unlike the Age Amount." );
		}
	}

	[Test]
	public void Calculate_BasicPersonalAmountExceedsTheTaxOwed_DoesNotProduceARefund() {
		// The credit is non-refundable, so it can reduce tax to zero but never below it. An
		// implausibly large amount against a small income must not turn into negative tax.
		CalculatedPlan calculatedPlan = Calculate( monthlyIncome: 800m, basicPersonalAmount: 500_000m );

		using( Assert.EnterMultipleScope() ) {
			Assert.That( calculatedPlan.TaxSummary.TotalFederalTax, Is.Zero );
			Assert.That( calculatedPlan.TaxSummary.TotalProvincialTax, Is.Zero );
			Assert.That(
				calculatedPlan.Periods.SelectMany( p => p.Taxes ).Select( t => t.FederalTax ),
				Has.All.GreaterThanOrEqualTo( 0m ),
				"A non-refundable credit must never drive tax negative." );
		}
	}

	private static CalculatedPlan Calculate(
		decimal monthlyIncome,
		decimal basicPersonalAmount
	) {
		TaxPolicy policy = TestPlanFactory.CreateTaxPolicy() with {
			BasicPersonalAmount = basicPersonalAmount,
			ProvincialBasicPersonalAmount = basicPersonalAmount,
			// The other credits are switched off so the effect measured is the personal amount
			// alone rather than an interaction with the Age or Pension Income amounts.
			AgeAmountBase = 0m,
			PensionIncomeAmount = 0m,
			OasClawbackThreshold = 0m
		};

		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 1, 1 ),
			members: [
				// Both members retire immediately and die before 65, so neither ever collects
				// CPP or OAS. The RRIF draw is therefore the only income, which keeps the
				// comparison against the personal amount exact.
				new Member( MemberTodd, new DateOnly( 1966, 1, 1 ), 64, 60, 70, 80m ),
				new Member( MemberTina, new DateOnly( 1967, 1, 1 ), 63, 60, 70, 50m )
			],
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, MemberTodd, 2_000_000m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, MemberTodd, 0m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, MemberTodd, 0m, hasUnlimitedContributionRoom: true ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, MemberTina, 0m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, MemberTina, 0m, 0m, 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, MemberTina, 0m, hasUnlimitedContributionRoom: true )
			],
			// Inflation is disabled so the credit and the brackets stay in start-year dollars and
			// the comparison is not clouded by indexing.
			annualInflationPercent: 0m,
			annualReturnPercent: 0m,
			lifeInsurance: [],
			retirementIncome: new RetirementIncome(
				GoGo: monthlyIncome,
				SlowGo: monthlyIncome,
				SlowGoYears: 0,
				NoGo: monthlyIncome,
				NoGoYears: 0
			),
			contributions: [],
			taxPolicy: policy,
			burndown: null
		);

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		return new PlanCalculator().Calculate( plan, compiledPlan );
	}
}
