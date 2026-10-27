using Planning.Compiler;
using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Identifiers;
using Planning.Model.Plans;
using Planning.TestSupport;

namespace Planning.Calculator.Tests;

[TestFixture]
public sealed class PensionSplittingTests {

	private static readonly MemberId PensionerId = new( 1 );
	private static readonly MemberId SpouseId = new( 2 );
	private static readonly DateOnly LateBirthday = new( 1961, 12, 31 );

	private static readonly TaxPolicy FlatPolicy = TaxPolicy.None with {
		Year = 2026,
		AllowPensionSplitting = true,
		FederalBrackets = [new TaxBracket( 0m, 10m )],
		AgeAmountEligibilityAge = 65,
		PensionIncomeEligibilityAge = 65
	};

	private static readonly TaxPolicy ProgressivePolicy = FlatPolicy with {
		FederalBrackets = [new TaxBracket( 0m, 10m ), new TaxBracket( 50_000m, 30m )]
	};

	[Test]
	public void Calculate_PensionerTurns65AfterDecemberFirst_SplitsWithAYoungerSpouse() {
		CalculatedPeriod period = CalculateSettlement(
			pensionerBirthDate: LateBirthday,
			registeredWithdrawal: 100_000m,
			taxPolicy: ProgressivePolicy,
			registeredBalance: 200_000m );

		using( Assert.EnterMultipleScope() ) {
			Assert.That( TaxableAmount( period, PensionerId ), Is.EqualTo( 50_000m ) );
			Assert.That( TaxableAmount( period, SpouseId ), Is.EqualTo( 50_000m ) );
			Assert.That( period.TotalTax, Is.EqualTo( 11_200m ) );
		}
	}

	[Test]
	public void Calculate_PensionerIsUnder65AtYearEnd_DoesNotSplit() {
		CalculatedPeriod period = CalculateSettlement(
			pensionerBirthDate: new DateOnly( 1962, 12, 31 ),
			registeredWithdrawal: 100_000m,
			taxPolicy: ProgressivePolicy,
			registeredBalance: 200_000m );

		using( Assert.EnterMultipleScope() ) {
			Assert.That( TaxableAmount( period, PensionerId ), Is.EqualTo( 100_000m ) );
			Assert.That( TaxableAmount( period, SpouseId ), Is.Zero );
		}
	}

	[Test]
	public void Calculate_SplittingDisabled_DoesNotReallocateIncome() {
		CalculatedPeriod period = CalculateSettlement(
			pensionerBirthDate: new DateOnly( 1960, 1, 1 ),
			registeredWithdrawal: 100_000m,
			taxPolicy: ProgressivePolicy with { AllowPensionSplitting = false },
			registeredBalance: 200_000m );

		Assert.That( TaxableAmount( period, PensionerId ), Is.EqualTo( 100_000m ) );
		Assert.That( TaxableAmount( period, SpouseId ), Is.Zero );
	}

	[Test]
	public void Calculate_OtherIncomeExceedsEligibleWithdrawals_TransfersAtMostHalfTheWithdrawals() {
		CalculatedPeriod period = CalculateSettlement(
			pensionerBirthDate: LateBirthday,
			registeredWithdrawal: 24_000m,
			taxPolicy: ProgressivePolicy,
			registeredBalance: 200_000m,
			otherIncome: 96_000m );

		using( Assert.EnterMultipleScope() ) {
			Assert.That( TaxableAmount( period, PensionerId ), Is.EqualTo( 108_000m ) );
			Assert.That( TaxableAmount( period, SpouseId ), Is.EqualTo( 12_000m ) );
		}
	}

	[Test]
	public void Calculate_SpouseAlreadyHasIncome_TransfersOnlyEnoughToEqualize() {
		CalculatedPeriod period = CalculateSettlement(
			pensionerBirthDate: LateBirthday,
			registeredWithdrawal: 100_000m,
			taxPolicy: ProgressivePolicy,
			registeredBalance: 200_000m,
			spouseIncome: 40_000m );

		Assert.That( TaxableAmount( period, PensionerId ), Is.EqualTo( 70_000m ) );
		Assert.That( TaxableAmount( period, SpouseId ), Is.EqualTo( 70_000m ) );
	}

	[Test]
	public void Calculate_NoRegisteredWithdrawals_DoesNotSplitOtherIncome() {
		CalculatedPeriod period = CalculateSettlement(
			pensionerBirthDate: LateBirthday,
			registeredWithdrawal: 0m,
			taxPolicy: ProgressivePolicy,
			registeredBalance: 200_000m,
			otherIncome: 100_000m );

		Assert.That( TaxableAmount( period, PensionerId ), Is.EqualTo( 100_000m ) );
		Assert.That( TaxableAmount( period, SpouseId ), Is.Zero );
	}

	[TestCase( 48_000, 5_250 )]
	[TestCase( 60_000, 6_600 )]
	public void Calculate_SplitRaisesOrDoesNotChangeCombinedTax_KeepsTheOriginalAllocation(
		decimal registeredWithdrawal,
		decimal expectedTax
	) {
		// Equalization adds $30 of Health Premium at $48,000 and saves nothing at $60,000.
		CalculatedPeriod period = CalculateSettlement(
			pensionerBirthDate: new DateOnly( 1960, 1, 1 ),
			registeredWithdrawal: registeredWithdrawal,
			taxPolicy: FlatPolicy,
			registeredBalance: 200_000m );

		using( Assert.EnterMultipleScope() ) {
			Assert.That( TaxableAmount( period, PensionerId ), Is.EqualTo( registeredWithdrawal ) );
			Assert.That( TaxableAmount( period, SpouseId ), Is.Zero );
			Assert.That( period.TotalTax, Is.EqualTo( expectedTax ) );
		}
	}

	[Test]
	public void Calculate_SplitAvoidsOasClawback_IncludesRecoveryTaxInTheComparison() {
		CalculatedPeriod period = CalculateSettlement(
			pensionerBirthDate: LateBirthday,
			registeredWithdrawal: 47_000m,
			taxPolicy: FlatPolicy with { OasClawbackThreshold = 40_000m, OasClawbackRate = 15m },
			registeredBalance: 200_000m,
			oasIncome: 1_000m );

		using( Assert.EnterMultipleScope() ) {
			Assert.That( TaxableAmount( period, PensionerId ), Is.EqualTo( 24_500m ) );
			Assert.That( TaxableAmount( period, SpouseId ), Is.EqualTo( 23_500m ) );
			Assert.That( period.TotalTax, Is.EqualTo( 5_280m ) );
		}
	}

	[Test]
	public void Calculate_IndexedBracketsRemoveTheTaxSaving_RejectsTheSplit() {
		CalculatedPeriod period = CalculateSettlement(
			pensionerBirthDate: new DateOnly( 1960, 1, 1 ),
			registeredWithdrawal: 96_000m,
			taxPolicy: ProgressivePolicy with { Year = 2025 },
			registeredBalance: 200_000m,
			annualInflationPercent: 100m );

		using( Assert.EnterMultipleScope() ) {
			Assert.That( TaxableAmount( period, PensionerId ), Is.EqualTo( 96_000m ) );
			Assert.That( TaxableAmount( period, SpouseId ), Is.Zero );
			Assert.That( period.TotalTax, Is.EqualTo( 10_350m ) );
		}
	}

	[TestCase( 10_000, 0, 2_000 )]
	[TestCase( 0, 2_000, 2_800 )]
	public void Calculate_PensionerTurns65AfterDecemberFirst_ReceivesYearEndCredits(
		decimal ageAmount,
		decimal pensionAmount,
		decimal expectedFederalTax
	) {
		CalculatedPeriod period = CalculateSettlement(
			pensionerBirthDate: LateBirthday,
			registeredWithdrawal: 30_000m,
			taxPolicy: FlatPolicy with {
				AllowPensionSplitting = false,
				AgeAmountBase = ageAmount,
				AgeAmountIncomeThreshold = 100_000m,
				PensionIncomeAmount = pensionAmount
			},
			registeredBalance: 200_000m );

		Assert.That( period.Taxes.Sum( t => t.FederalTax ), Is.EqualTo( expectedFederalTax ) );
	}

	[Test]
	public void Calculate_BurndownChangesTheSplittingDecision_RetainsOnlyTheCombinedMarginalTax() {
		CalculatedPeriod period = CalculateSettlement(
			pensionerBirthDate: new DateOnly( 1960, 1, 1 ),
			registeredWithdrawal: 72_000m,
			taxPolicy: FlatPolicy with {
				FederalBrackets = [new TaxBracket( 0m, 10m ), new TaxBracket( 90_000m, 30m )]
			},
			registeredBalance: 100_000m,
			burndown: new Burndown( BurndownYears: 7 ) );

		// Without burndown, splitting saves nothing on $72,000. With burndown, the $100,000
		// base is split, reducing the donor's tax while increasing the spouse's tax.
		using( Assert.EnterMultipleScope() ) {
			Assert.That( period.BurndownWithdrawal, Is.EqualTo( 28_000m ) );
			Assert.That( period.TotalTax, Is.EqualTo( 11_200m ) );
			Assert.That( period.BurndownTax, Is.EqualTo( 3_400m ) );
			Assert.That( period.BurndownTransfer, Is.EqualTo( 24_600m ).Within( 0.01m ) );
			Assert.That( period.TaxFundingWithdrawal, Is.EqualTo( 7_800m ) );
			Assert.That( period.UnfundedTax, Is.Zero );
		}
	}

	private static decimal TaxableAmount(
		CalculatedPeriod period,
		MemberId memberId
	) {
		return period.Taxes.Where( t => t.MemberId == memberId ).Sum( t => t.TaxableAmount );
	}

	private static CalculatedPeriod CalculateSettlement(
		DateOnly pensionerBirthDate,
		decimal registeredWithdrawal,
		TaxPolicy taxPolicy,
		decimal registeredBalance,
		decimal otherIncome = 0m,
		decimal spouseIncome = 0m,
		decimal oasIncome = 0m,
		decimal annualInflationPercent = 0m,
		Burndown? burndown = null
	) {
		decimal desiredIncome = registeredWithdrawal + otherIncome + spouseIncome + oasIncome;
		Plan plan = TestPlanFactory.Create(
			startDate: new DateOnly( 2026, 12, 1 ),
			members: [
				new Member( "Todd", pensionerBirthDate, 90, 60, 70, 0m, 70 ),
				new Member( "Tina", new DateOnly( 1970, 1, 1 ), 90, 55, 70, 0m, 70 )
			],
			cppMaximum: 0m,
			oasMaximum: 0m,
			assets: [
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Todd", registeredBalance ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Todd", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Todd", 0m, hasUnlimitedContributionRoom: true ),
				TestPlanFactory.CreateAsset( "RRSP", AssetTaxStatus.Taxable, "Tina", 0m ),
				TestPlanFactory.CreateAsset( "TFSA", AssetTaxStatus.TaxExempt, "Tina", 0m ),
				TestPlanFactory.CreateAsset( "Non-Reg", AssetTaxStatus.CapitalGains, "Tina", 0m, hasUnlimitedContributionRoom: true )
			],
			annualInflationPercent: annualInflationPercent,
			annualReturnPercent: 0m,
			lifeInsurance: [],
			retirementIncome: new RetirementIncome( desiredIncome, desiredIncome, 0, desiredIncome, 0 ),
			contributions: [],
			taxPolicy: taxPolicy,
			burndown: burndown );

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
		CompiledPeriod period = compiledPlan.Periods.First();
		compiledPlan = compiledPlan with { Periods = [period] };
		compiledPlan.ScheduledIncome[period] = [
			new CompiledIncome( "Other", PensionerId, otherIncome, Taxable: true ),
			new CompiledIncome( "Other", SpouseId, spouseIncome, Taxable: true ),
			new CompiledIncome( "OAS", PensionerId, oasIncome, Taxable: true )
		];

		return new PlanCalculator().Calculate( plan, compiledPlan ).Periods.Single();
	}
}
