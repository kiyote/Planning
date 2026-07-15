using Planning.Model;
using Planning.Model.Plans;
using Planning.Model.CompiledPlans;
using Planning.Model.CalculatedPlans;
using Planning.Model.Identifiers;
using Planning.Calculator.Calculators;

namespace Planning.Calculator;

public class PlanCalculator {

	private readonly IncomeCalculator _incomeCalculator = new();
	private readonly WithdrawalPolicy _withdrawalPolicy = new();
	private readonly AssetGrowthCalculator _assetGrowthCalculator = new();
	private readonly TaxCalculator _taxCalculator = new();

	public CalculatedPlan Calculate(
		Plan plan,
		CompiledPlan compiledPlan
	) {
		List<CalculatedPeriod> periods = [];
		List<CalculatedAsset> currentAssets = [.. compiledPlan.Assets.Select( a => new CalculatedAsset( a.AssetId, a.Amount ) )];

		int planStartYear = plan.StartDate.Year;

		// Per-member taxable base accumulated over the current calendar year.
		Dictionary<MemberId, decimal> yearlyTaxableByMember = [];

		// The RRSP-withdrawal portion of the taxable base, tracked separately because only
		// this income is eligible for pension splitting between spouses.
		Dictionary<MemberId, decimal> yearlySplittableByMember = [];

		foreach( CompiledPeriod period in compiledPlan.Periods) {
			List<CalculatedAsset> startingAssets = [.. currentAssets];

			IReadOnlyList<CalculatedIncome> taxableIncome = [.. _incomeCalculator.CalculateTaxableIncome( period, compiledPlan )];
			IReadOnlyList<CalculatedIncome> nonTaxableIncome = [.. _incomeCalculator.CalculateNonTaxableIncome( period, compiledPlan )];

			decimal totalTaxableIncome = taxableIncome.Sum( ti => ti.Amount );
			decimal totalNonTaxableIncome = nonTaxableIncome.Sum( nti => nti.Amount );
			decimal totalIncome = totalTaxableIncome + totalNonTaxableIncome;
			decimal desiredRetirementIncome = compiledPlan.RetirementIncome[period];
			decimal retirementIncomeShortfall = Math.Max( desiredRetirementIncome - totalIncome, 0 );

			IReadOnlyList<CalculatedContribution> contributions = [.. compiledPlan.Contribution[period].Select( c => new CalculatedContribution( c.AssetId, c.Amount ) )];

			IReadOnlyList<CalculatedWithdrawal> withdrawals = [.. _withdrawalPolicy.CalculateWithdrawals( period, compiledPlan, currentAssets, retirementIncomeShortfall )];

			List<CalculatedAsset> endingAssets = [];
			decimal totalAssets = 0.0m;
			foreach( CalculatedAsset asset in currentAssets ) {
				CalculatedAsset endingAsset = _assetGrowthCalculator.GrowAsset( plan, compiledPlan, asset, period.PeriodDate, withdrawals, contributions );
				endingAssets.Add( endingAsset );
			}

			decimal requestedWithdrawal = retirementIncomeShortfall;
			decimal actualWithdrawal = withdrawals.Sum( w => w.Amount );

			// Retirement income and benefit amounts are inflation-compounded decimals that carry
			// many fractional digits, so the requested-versus-actual comparison can leave a
			// sub-cent residual even when the plan is fully funded. Currency is only meaningful to
			// the cent, so resolve the unfunded amount at cent precision to avoid reporting phantom
			// shortfalls when assets clearly cover the requested withdrawal.
			decimal unfundedShortfall = Math.Round( Math.Max( requestedWithdrawal - actualWithdrawal, 0 ), 2, MidpointRounding.AwayFromZero );
			decimal actualRetirementIncome = totalIncome + actualWithdrawal;
			bool planExhausted = unfundedShortfall > 0m;

			// Income beyond what is needed to meet the desired retirement income (for example a
			// life insurance payout) is retained by depositing the surplus into a living member's
			// non-taxable (TFSA) account rather than being discarded.
			decimal surplusIncome = Math.Max( totalIncome - desiredRetirementIncome, 0 );
			if( surplusIncome > 0 ) {
				DepositSurplus( compiledPlan, endingAssets, period.PeriodDate, surplusIncome );
			}

			AccrueTaxableAmounts( compiledPlan, taxableIncome, withdrawals, yearlyTaxableByMember, yearlySplittableByMember );

			IReadOnlyList<CalculatedTax> taxes = [];
			decimal totalTax = 0m;
			decimal taxFundingWithdrawal = 0m;
			decimal unfundedTax = 0m;
			bool isYearEnd = period.PeriodDate.Month == 12;
			if( isYearEnd && yearlyTaxableByMember.Count > 0 ) {
				TaxSettlement settlement = SettleAnnualTax( plan, compiledPlan, yearlyTaxableByMember, yearlySplittableByMember, period.PeriodDate, planStartYear, plan.AnnualInflationPercent, endingAssets );
				taxes = settlement.Taxes;
				totalTax = taxes.Sum( t => t.TotalTax );
				taxFundingWithdrawal = settlement.TaxFundingWithdrawal;
				unfundedTax = settlement.UnfundedTax;

				// The taxable-account portion used to fund this year's tax bill becomes
				// taxable income in the following year for the account's owner.
				yearlyTaxableByMember = [];
				yearlySplittableByMember = [];
				foreach( KeyValuePair<MemberId, decimal> deferred in settlement.DeferredTaxableByMember ) {
					Add( yearlyTaxableByMember, deferred.Key, deferred.Value );
				}
			}

			totalAssets = endingAssets.Sum( a => a.Amount );

			CalculatedPeriod newPeriod = new CalculatedPeriod(
				period.PeriodNumber,
				period.PeriodDate,
				startingAssets,
				taxableIncome,
				nonTaxableIncome,
				contributions,
				withdrawals,
				endingAssets,
				totalTaxableIncome,
				totalNonTaxableIncome,
				totalIncome,
				desiredRetirementIncome,
				retirementIncomeShortfall,
				totalAssets,
				actualRetirementIncome,
				requestedWithdrawal,
				actualWithdrawal,
				unfundedShortfall,
				planExhausted,
				taxes,
				totalTax,
				taxFundingWithdrawal,
				unfundedTax
			);
			periods.Add( newPeriod );

			currentAssets = endingAssets;
		}

		CalculatedPlan calculatedPlan = new CalculatedPlan(
			periods,
			BuildInsufficientFundsSummary( periods ),
			BuildTaxSummary( periods ),
			BuildEvents( compiledPlan ),
			plan.RetirementIncome
		);
		return calculatedPlan;
	}

	/// <summary>
	/// Collects every graphable timeline event for the plan: each member's lifecycle events
	/// (retirement, CPP start, OAS start, and death) and the retirement-income phase
	/// transitions (Go-Go, Slow-Go, and No-Go), ordered by date.
	/// </summary>
	private static IReadOnlyList<PlanEvent> BuildEvents(
		CompiledPlan compiledPlan
	) {
		List<PlanEvent> events = [];

		foreach( CompiledMember member in compiledPlan.Members ) {
			events.Add( new PlanEvent( member.RetirementDate, $"{member.Name} retires", PlanEventKind.Lifecycle ) );
			events.Add( new PlanEvent( member.CPPStartDate, $"{member.Name} CPP", PlanEventKind.Lifecycle ) );
			events.Add( new PlanEvent( member.OASStartDate, $"{member.Name} OAS", PlanEventKind.Lifecycle ) );
			events.Add( new PlanEvent( member.DeathDate, $"{member.Name} dies", PlanEventKind.Lifecycle ) );
		}

		RetirementPhaseSchedule schedule = compiledPlan.RetirementPhaseSchedule;
		events.Add( new PlanEvent( schedule.GoGoStart, "Go-Go", PlanEventKind.RetirementPhase ) );
		events.Add( new PlanEvent( schedule.SlowGoStart, "Slow-Go", PlanEventKind.RetirementPhase ) );
		events.Add( new PlanEvent( schedule.NoGoStart, "No-Go", PlanEventKind.RetirementPhase ) );

		return [.. events.OrderBy( e => e.Date )];
	}

	private static void DepositSurplus(
		CompiledPlan compiledPlan,
		List<CalculatedAsset> endingAssets,
		DateOnly periodDate,
		decimal surplus
	) {
		// Prefer a living member's non-taxable (TFSA) account. Members are considered living
		// while the period falls on or before their death month.
		CompiledMember? livingMember = compiledPlan.Members
			.Where( m => periodDate <= m.DeathDate )
			.OrderBy( m => m.DeathDate )
			.FirstOrDefault();

		CompiledAsset? target = null;
		if( livingMember is not null ) {
			target = compiledPlan.Assets
				.FirstOrDefault( a => a.MemberId == livingMember.MemberId && a.TaxStatus == AssetTaxStatus.TaxExempt );
		}

		// Fall back to any non-taxable account, then to any account at all, so the surplus is
		// always retained somewhere in the household's assets.
		target ??= compiledPlan.Assets.FirstOrDefault( a => a.TaxStatus == AssetTaxStatus.TaxExempt );
		target ??= compiledPlan.Assets.FirstOrDefault();

		if( target is null ) {
			return;
		}

		int index = endingAssets.FindIndex( a => a.AssetId == target.AssetId );
		if( index < 0 ) {
			return;
		}

		endingAssets[index] = endingAssets[index] with { Amount = endingAssets[index].Amount + surplus };
	}

	private static void AccrueTaxableAmounts(
		CompiledPlan compiledPlan,
		IReadOnlyList<CalculatedIncome> taxableIncome,
		IReadOnlyList<CalculatedWithdrawal> withdrawals,
		Dictionary<MemberId, decimal> yearlyTaxableByMember,
		Dictionary<MemberId, decimal> yearlySplittableByMember
	) {
		foreach( CalculatedIncome income in taxableIncome ) {
			Add( yearlyTaxableByMember, income.MemberId, income.Amount );
		}

		foreach( CalculatedWithdrawal withdrawal in withdrawals ) {
			CompiledAsset asset = compiledPlan.Assets.Single( a => a.AssetId == withdrawal.AssetId );
			decimal taxable = withdrawal.Amount * TaxableInclusionRate( asset.TaxStatus );
			if( taxable != 0m ) {
				Add( yearlyTaxableByMember, asset.MemberId, taxable );

				// Only fully-taxable RRSP withdrawal income is eligible for pension splitting.
				if( asset.TaxStatus == AssetTaxStatus.Taxable ) {
					Add( yearlySplittableByMember, asset.MemberId, taxable );
				}
			}
		}
	}

	/// <summary>
	/// The fraction of a withdrawal that is added to the taxable base: 100% for FullTax,
	/// 50% for CapitalGains (capital gains inclusion rate), and 0% for TaxExempt.
	/// </summary>
	private static decimal TaxableInclusionRate( AssetTaxStatus status ) {
		return status switch {
			AssetTaxStatus.Taxable => 1m,
			AssetTaxStatus.CapitalGains => 0.5m,
			AssetTaxStatus.TaxExempt => 0m,
			_ => throw new ArgumentOutOfRangeException( nameof( status ), status, null )
		};
	}

	private static void Add(
		Dictionary<MemberId, decimal> accumulator,
		MemberId memberId,
		decimal amount
	) {
		accumulator[memberId] = accumulator.TryGetValue( memberId, out decimal existing )
			? existing + amount
			: amount;
	}

	/// <summary>
	/// Produces a copy of the per-member taxable base after (optionally) reallocating eligible
	/// RRSP income between two spouses to reduce combined tax. Splitting only applies when the
	/// tax policy allows it, both members are age 65 or older at year end, and each member has
	/// eligible RRSP income. Up to 50% of the higher earner's eligible income is transferred to
	/// the lower earner, capped at the amount needed to equalize their taxable bases.
	/// </summary>
	private Dictionary<MemberId, decimal> ApplyPensionSplitting(
		Plan plan,
		CompiledPlan compiledPlan,
		Dictionary<MemberId, decimal> yearlyTaxableByMember,
		Dictionary<MemberId, decimal> yearlySplittableByMember,
		DateOnly periodDate,
		decimal inflationIndex
	) {
		Dictionary<MemberId, decimal> result = new( yearlyTaxableByMember );

		List<CompiledMember> members = [.. compiledPlan.Members];
		if( !compiledPlan.TaxPolicy.AllowPensionSplitting || members.Count != 2 ) {
			return result;
		}

		CompiledMember first = members[0];
		CompiledMember second = members[1];

		// Both spouses must be alive and at least 65 at year end for splitting to apply.
		if( periodDate < first.BirthDate.AddYears( 65 ) || periodDate < second.BirthDate.AddYears( 65 ) ) {
			return result;
		}
		if( first.DeathDate <= periodDate || second.DeathDate <= periodDate ) {
			return result;
		}

		result.TryGetValue( first.MemberId, out decimal firstTaxable );
		result.TryGetValue( second.MemberId, out decimal secondTaxable );

		CompiledMember higher = firstTaxable >= secondTaxable ? first : second;
		CompiledMember lower = higher == first ? second : first;
		decimal higherTaxable = higher == first ? firstTaxable : secondTaxable;
		decimal lowerTaxable = higher == first ? secondTaxable : firstTaxable;

		yearlySplittableByMember.TryGetValue( higher.MemberId, out decimal higherEligible );
		if( higherEligible <= 0m ) {
			return result;
		}

		// Transfer at most 50% of the higher earner's eligible RRSP income, and never more than
		// what equalizes the two taxable bases.
		decimal maxTransfer = Math.Min( higherEligible * 0.5m, ( higherTaxable - lowerTaxable ) / 2m );
		if( maxTransfer <= 0m ) {
			return result;
		}

		result[higher.MemberId] = higherTaxable - maxTransfer;
		result[lower.MemberId] = lowerTaxable + maxTransfer;
		return result;
	}

	private TaxSettlement SettleAnnualTax(
		Plan plan,
		CompiledPlan compiledPlan,
		Dictionary<MemberId, decimal> yearlyTaxableByMember,
		Dictionary<MemberId, decimal> yearlySplittableByMember,
		DateOnly periodDate,
		int planStartYear,
		decimal annualInflationPercent,
		List<CalculatedAsset> endingAssets
	) {
		decimal inflationIndex = (decimal)Math.Pow(
			(double)(1m + annualInflationPercent / 100m),
			periodDate.Year - planStartYear );

		// Reallocate eligible RRSP income between spouses to minimize combined tax.
		Dictionary<MemberId, decimal> taxableByMember = ApplyPensionSplitting(
			plan, compiledPlan, yearlyTaxableByMember, yearlySplittableByMember, periodDate, inflationIndex );

		List<CalculatedTax> taxes = [];
		decimal totalFunded = 0m;
		decimal totalUnfunded = 0m;
		Dictionary<MemberId, decimal> deferredTaxableByMember = [];

		foreach( CompiledMember member in compiledPlan.Members ) {
			if( !taxableByMember.TryGetValue( member.MemberId, out decimal taxableAmount ) || taxableAmount <= 0m ) {
				continue;
			}

			decimal federalTax = _taxCalculator.CalculateTax( compiledPlan.TaxPolicy.FederalBrackets, taxableAmount, inflationIndex );
			decimal provincialTax = _taxCalculator.CalculateTax( compiledPlan.TaxPolicy.ProvincialBrackets, taxableAmount, inflationIndex );

			// Reduce federal tax by the non-refundable federal Age Amount credit for members who
			// are old enough at year end. The credit can only reduce federal tax to zero.
			int ageAtYearEnd = periodDate.Year - member.BirthDate.Year;
			if( periodDate < member.BirthDate.AddYears( ageAtYearEnd ) ) {
				ageAtYearEnd--;
			}
			bool ageAmountEligible = ageAtYearEnd >= compiledPlan.TaxPolicy.AgeAmountEligibilityAge;
			decimal ageAmountCredit = _taxCalculator.CalculateAgeAmountCredit(
				compiledPlan.TaxPolicy, taxableAmount, ageAmountEligible, inflationIndex );
			federalTax = Math.Max( 0m, federalTax - ageAmountCredit );

			// Reduce federal tax by the non-refundable federal Pension Income Amount credit. Under
			// the modelling assumption that a member's RRSP becomes a RRIF at retirement, their
			// RRSP (RRIF) withdrawal income after retirement is eligible pension income. Per CRA
			// rules, RRIF income only qualifies once the member is at least the eligibility age
			// (65) at year end. The pre-split RRSP-withdrawal amount is used so the credit
			// reflects the member's own eligible pension income rather than any income
			// reallocated to their spouse.
			if( member.RetirementDate <= periodDate
				&& ageAtYearEnd >= compiledPlan.TaxPolicy.PensionIncomeEligibilityAge
				&& yearlySplittableByMember.TryGetValue( member.MemberId, out decimal eligiblePensionIncome )
				&& eligiblePensionIncome > 0m ) {
				decimal pensionIncomeCredit = _taxCalculator.CalculatePensionIncomeCredit(
					compiledPlan.TaxPolicy, eligiblePensionIncome, inflationIndex );
				federalTax = Math.Max( 0m, federalTax - pensionIncomeCredit );
			}

			taxes.Add( new CalculatedTax( member.MemberId, taxableAmount, federalTax, provincialTax ) );

			// Fund the tax bill as an additional withdrawal from assets. The taxable-account
			// portion of that funding is deferred as taxable income into the following year.
			decimal funded = FundTaxFromAssets(
				compiledPlan,
				member.MemberId,
				federalTax + provincialTax,
				endingAssets,
				deferredTaxableByMember );

			totalFunded += funded;
			totalUnfunded += federalTax + provincialTax - funded;
		}

		// Currency is only meaningful to the cent; round the accumulated unfunded tax so sub-cent
		// residuals from inflation-compounded amounts are not reported as an unfunded tax bill.
		totalUnfunded = Math.Round( Math.Max( totalUnfunded, 0m ), 2, MidpointRounding.AwayFromZero );

		return new TaxSettlement( taxes, totalFunded, totalUnfunded, deferredTaxableByMember );
	}

	private static decimal FundTaxFromAssets(
		CompiledPlan compiledPlan,
		MemberId memberId,
		decimal taxOwed,
		List<CalculatedAsset> endingAssets,
		Dictionary<MemberId, decimal> deferredTaxableByMember
	) {
		if( taxOwed <= 0m ) {
			return 0m;
		}

		IReadOnlyDictionary<AssetId, CompiledAsset> assetsById = compiledPlan.Assets.ToDictionary( a => a.AssetId );

		// Funding order: prefer the member's own accounts, and within that draw from the least
		// tax-costly accounts first (TaxExempt, then CapitalGains, then FullTax).
		IEnumerable<CalculatedAsset> ordered = endingAssets
			.OrderBy( a => assetsById[a.AssetId].MemberId == memberId ? 0 : 1 )
			.ThenByDescending( a => assetsById[a.AssetId].TaxStatus );

		decimal remaining = taxOwed;
		foreach( CalculatedAsset asset in ordered ) {
			if( remaining <= 0m ) {
				break;
			}

			decimal deducted = Math.Min( asset.Amount, remaining );
			if( deducted <= 0m ) {
				continue;
			}

			int index = endingAssets.FindIndex( a => a.AssetId == asset.AssetId );
			endingAssets[index] = asset with { Amount = asset.Amount - deducted };
			remaining -= deducted;

			CompiledAsset compiledAsset = assetsById[asset.AssetId];
			decimal taxable = deducted * TaxableInclusionRate( compiledAsset.TaxStatus );
			if( taxable != 0m ) {
				Add( deferredTaxableByMember, compiledAsset.MemberId, taxable );
			}
		}

		return taxOwed - remaining;
	}

	private sealed record TaxSettlement(
		IReadOnlyList<CalculatedTax> Taxes,
		decimal TaxFundingWithdrawal,
		decimal UnfundedTax,
		Dictionary<MemberId, decimal> DeferredTaxableByMember
	);

	private static InsufficientFundsSummary BuildInsufficientFundsSummary(
		IReadOnlyList<CalculatedPeriod> periods
	) {
		CalculatedPeriod? firstShortfall = periods.FirstOrDefault( p => p.PlanExhausted );

		return new InsufficientFundsSummary(
			HasShortfall: firstShortfall is not null,
			FirstShortfallDate: firstShortfall?.PeriodDate,
			FirstShortfallPeriod: firstShortfall?.PeriodNumber,
			ShortfallPeriodCount: periods.Count( p => p.PlanExhausted ),
			TotalUnfundedShortfall: periods.Sum( p => p.UnfundedShortfall )
		);
	}

	private static TaxSummary BuildTaxSummary(
		IReadOnlyList<CalculatedPeriod> periods
	) {
		decimal totalFederal = periods.SelectMany( p => p.Taxes ).Sum( t => t.FederalTax );
		decimal totalProvincial = periods.SelectMany( p => p.Taxes ).Sum( t => t.ProvincialTax );

		return new TaxSummary(
			TotalFederalTax: totalFederal,
			TotalProvincialTax: totalProvincial,
			TotalTax: totalFederal + totalProvincial
		);
	}
}
