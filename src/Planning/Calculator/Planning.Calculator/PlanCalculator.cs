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
	private readonly ContributionPolicy _contributionPolicy = new();
	private readonly BurndownPolicy _burndownPolicy = new();
	private readonly RrifMinimumPolicy _rrifMinimumPolicy = new();

	/// <summary>
	/// Sentinel indicating an account has no contribution cap; it absorbs any amount without
	/// consuming room.
	/// </summary>
	private const decimal UnlimitedContributionRoom = ShelterAllocator.Unlimited;

	/// <summary>
	/// The income name the compiler assigns to Old Age Security. Used to isolate OAS from the
	/// rest of the taxable base so the recovery tax can be capped at the amount received.
	/// </summary>
	private const string OasIncomeName = "OAS";

	private readonly AssetGrowthCalculator _assetGrowthCalculator = new();
	private readonly TaxCalculator _taxCalculator = new();

	public CalculatedPlan Calculate(
		Plan plan,
		CompiledPlan compiledPlan
	) {
		List<CalculatedPeriod> periods = [];
		List<CalculatedAsset> currentAssets = [.. compiledPlan.Assets.Select( a => new CalculatedAsset( a.AssetId, a.Amount, a.ContributionBacklog, a.TaxStatus ) )];

		int planStartYear = plan.StartDate.Year;

		// Per-member taxable base accumulated over the current calendar year.
		Dictionary<MemberId, decimal> yearlyTaxableByMember = [];

		// The RRSP-withdrawal portion of the taxable base, tracked separately because only
		// this income is eligible for pension splitting between spouses.
		Dictionary<MemberId, decimal> yearlySplittableByMember = [];

		// The OAS portion of the taxable base, tracked separately because the OAS recovery tax
		// (clawback) can never recover more than the OAS actually received in the year.
		Dictionary<MemberId, decimal> yearlyOasByMember = [];

		// The RRIF minimum is a percentage of the January 1 balance, measured against what the
		// account has already given up during the year, so both are tracked per calendar year.
		Dictionary<AssetId, decimal> yearStartBalances = [];
		Dictionary<AssetId, decimal> withdrawnThisYear = [];
		int currentTrackingYear = 0;

		// Spousal contributions are attributed back to the contributing spouse on withdrawal for
		// a rolling three-year window, so the ledger spans the whole projection.
		SpousalAttributionLedger spousalAttribution = new SpousalAttributionLedger();

		foreach( CompiledPeriod period in compiledPlan.Periods ) {
			List<CalculatedAsset> startingAssets = [.. currentAssets];

			// A new calendar year resets the RRIF measurement window: the opening balances become
			// the base for this year's minimum and the withdrawal tally starts again.
			if( period.PeriodDate.Year != currentTrackingYear ) {
				currentTrackingYear = period.PeriodDate.Year;
				yearStartBalances = currentAssets.ToDictionary( a => a.AssetId, a => a.Amount );
				withdrawnThisYear = [];
				spousalAttribution.Prune( currentTrackingYear );
			}

			IReadOnlyList<CalculatedIncome> taxableIncome = [.. _incomeCalculator.CalculateTaxableIncome( period, compiledPlan )];
			IReadOnlyList<CalculatedIncome> nonTaxableIncome = [.. _incomeCalculator.CalculateNonTaxableIncome( period, compiledPlan )];

			decimal totalTaxableIncome = taxableIncome.Sum( ti => ti.Amount );
			decimal totalNonTaxableIncome = nonTaxableIncome.Sum( nti => nti.Amount );
			decimal totalIncome = totalTaxableIncome + totalNonTaxableIncome;
			decimal desiredRetirementIncome = compiledPlan.RetirementIncome[period];
			decimal retirementIncomeShortfall = Math.Max( desiredRetirementIncome - totalIncome, 0 );

			// Accrue this year's contribution room and allocate the period's contributions against it,
			// spilling into the member's next most tax-efficient account when room runs out.
			bool isFirstPeriod = periods.Count == 0;
			ContributionAllocation allocation = _contributionPolicy.AllocateContributions( compiledPlan, currentAssets, period.PeriodDate, isFirstPeriod, compiledPlan.Contribution[period] );
			IReadOnlyList<CalculatedContribution> contributions = allocation.Contributions;
			currentAssets = [.. allocation.Assets];

			// Spousal contributions stay attributable to the contributor for the year they are
			// made and the two that follow, so they are recorded as they are applied.
			foreach( SpousalDeposit deposit in allocation.SpousalDeposits ) {
				spousalAttribution.RecordContribution(
					deposit.DestinationMemberId,
					deposit.ContributorMemberId,
					period.PeriodDate.Year,
					deposit.Amount );
			}

			IReadOnlyList<CalculatedWithdrawal> withdrawals = [.. _withdrawalPolicy.CalculateWithdrawals( period, compiledPlan, currentAssets, retirementIncomeShortfall )];

			foreach( CalculatedWithdrawal withdrawal in withdrawals ) {
				if( withdrawal.Amount > 0m ) {
					withdrawnThisYear[withdrawal.AssetId] =
						withdrawnThisYear.GetValueOrDefault( withdrawal.AssetId ) + withdrawal.Amount;
				}
			}

			List<CalculatedAsset> endingAssets = [];
			decimal totalAssets = 0.0m;
			foreach( CalculatedAsset asset in currentAssets ) {
				CalculatedAsset endingAsset = _assetGrowthCalculator.GrowAsset( plan, compiledPlan, asset, period.PeriodDate, withdrawals, contributions );
				endingAssets.Add( endingAsset );
			}

			decimal requestedWithdrawal = retirementIncomeShortfall;
			decimal actualWithdrawal = withdrawals.Sum( w => w.Amount );

			// Round to the cent in order to prevent sub-cent residuals from inflation-compounded amounts being reported as a shortfall
			decimal unfundedShortfall = Math.Round( Math.Max( requestedWithdrawal - actualWithdrawal, 0 ), 2, MidpointRounding.AwayFromZero );
			decimal actualRetirementIncome = totalIncome + actualWithdrawal;
			bool planExhausted = unfundedShortfall > 0m;

			decimal surplusIncome = Math.Max( totalIncome - desiredRetirementIncome, 0 );
			if( surplusIncome > 0 ) {
				DepositSurplus( compiledPlan, endingAssets, period.PeriodDate, surplusIncome );
			}

			// A deceased member's balances pass to the surviving spouse, after which all income
			// and tax fall on the survivor alone.
			RollOverAssetsOnDeath( compiledPlan, endingAssets, period.PeriodDate );

			AccrueTaxableAmounts( compiledPlan, taxableIncome, withdrawals, currentAssets, yearlyTaxableByMember, yearlySplittableByMember, yearlyOasByMember, spousalAttribution, period.PeriodDate.Year );

			IReadOnlyList<CalculatedTax> taxes = [];
			decimal totalTax = 0m;
			decimal taxFundingWithdrawal = 0m;
			decimal unfundedTax = 0m;
			decimal burndownWithdrawal = 0m;
			decimal burndownTax = 0m;
			decimal burndownTransfer = 0m;
			decimal rrifMinimumWithdrawal = 0m;
			decimal rrifMinimumTransfer = 0m;

			// Tax already covered by proceeds retained from the burndown withdrawal, so that the
			// annual settlement does not fund the same liability from assets a second time.
			Dictionary<MemberId, decimal> preFundedTaxByMember = [];

			bool isYearEnd = period.PeriodDate.Month == 12;

			if( isYearEnd ) {
				// The mandatory minimum is settled before the burndown, because it is compulsory
				// and the burndown is discretionary. The forced amount is income the plan did not
				// ask for, so it is moved into shelter rather than spent.
				RrifMinimumWithdrawals rrif = _rrifMinimumPolicy.CalculateWithdrawals(
					compiledPlan, endingAssets, period.PeriodDate, yearStartBalances, withdrawnThisYear );

				if( rrif.Total > 0m ) {
					// The forced withdrawal is fully taxable income to the account's owner, so it
					// must be accrued before the year's tax is settled below.
					AccrueTaxableAmounts( compiledPlan, [], rrif.Withdrawals, endingAssets, yearlyTaxableByMember, yearlySplittableByMember, yearlyOasByMember, spousalAttribution, period.PeriodDate.Year );

					rrifMinimumTransfer = _rrifMinimumPolicy.ApplyWithdrawals(
						compiledPlan, endingAssets, rrif.Withdrawals, period.PeriodDate );
					rrifMinimumWithdrawal = rrif.Total;

					foreach( CalculatedWithdrawal withdrawal in rrif.Withdrawals ) {
						withdrawnThisYear[withdrawal.AssetId] =
							withdrawnThisYear.GetValueOrDefault( withdrawal.AssetId ) + withdrawal.Amount;
					}
				}
			}

			if( isYearEnd ) {
				// The burndown draws the taxable accounts down on their amortized schedule, in
				// excess of the retirement-income withdrawals already taken. The tax it triggers
				// is retained from the proceeds and only the remainder is reinvested.
				BurndownWithdrawals burndown = _burndownPolicy.CalculateWithdrawals(
					plan, compiledPlan, endingAssets, period.PeriodDate );

				if( burndown.Total > 0m ) {
					decimal inflationIndex = InflationIndex( plan.AnnualInflationPercent, period.PeriodDate, planStartYear );

					Dictionary<MemberId, decimal> taxableWithoutBurndown = new( yearlyTaxableByMember );
					Dictionary<MemberId, decimal> splittableWithoutBurndown = new( yearlySplittableByMember );

					AccrueTaxableAmounts( compiledPlan, [], burndown.Withdrawals, endingAssets, yearlyTaxableByMember, yearlySplittableByMember, yearlyOasByMember, spousalAttribution, period.PeriodDate.Year );

					Dictionary<MemberId, decimal> burndownTaxByMember = CalculateBurndownTax(
						plan, compiledPlan,
						yearlyTaxableByMember, yearlySplittableByMember,
						taxableWithoutBurndown, splittableWithoutBurndown,
						yearlyOasByMember,
						period.PeriodDate, inflationIndex );

					burndownWithdrawal = burndown.Total;
					burndownTax = burndownTaxByMember.Values.Sum();

					// Every taxable account is drawn down on the same schedule, so the tax cost can
					// be spread proportionally across the proceeds.
					decimal netProportion = Math.Max( 0m, 1m - burndownTax / burndownWithdrawal );
					burndownTransfer = _burndownPolicy.ApplyTransfers(
						compiledPlan, endingAssets, burndown.Withdrawals, netProportion, period.PeriodDate );

					preFundedTaxByMember = burndownTaxByMember;
				}
			}

			if( isYearEnd && yearlyTaxableByMember.Count > 0 ) {
				TaxSettlement settlement = SettleAnnualTax( plan, compiledPlan, yearlyTaxableByMember, yearlySplittableByMember, yearlyOasByMember, period.PeriodDate, planStartYear, plan.AnnualInflationPercent, endingAssets, preFundedTaxByMember );
				taxes = settlement.Taxes;
				totalTax = taxes.Sum( t => t.TotalTax );
				taxFundingWithdrawal = settlement.TaxFundingWithdrawal;
				unfundedTax = settlement.UnfundedTax;

				// The taxable-account portion used to fund this year's tax bill becomes
				// taxable income in the following year for the account's owner.
				yearlyTaxableByMember = [];
				yearlySplittableByMember = [];
				yearlyOasByMember = [];
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
				unfundedTax,
				burndownWithdrawal,
				burndownTax,
				burndownTransfer,
				rrifMinimumWithdrawal,
				rrifMinimumTransfer
			);
			periods.Add( newPeriod );

			currentAssets = endingAssets;
		}

		MemberTax terminalTax = CalculateTerminalTax( plan, compiledPlan, periods, planStartYear );

		CalculatedPlan calculatedPlan = new CalculatedPlan(
			periods,
			BuildInsufficientFundsSummary( periods ),
			BuildTaxSummary( periods, terminalTax ),
			BuildEstateSummary( plan, periods, terminalTax, planStartYear ),
			BuildEvents( plan, compiledPlan ),
			plan.RetirementIncome
		);
		return calculatedPlan;
	}

	/// <summary>
	/// Calculates the tax falling due on the final return of the last member to die. On death
	/// with no surviving spouse, the estate is deemed to have disposed of everything still
	/// held: registered (Taxable) balances are included in income in full, non-registered
	/// (CapitalGains) balances realize their accrued gain at the capital-gains inclusion rate,
	/// and TFSA (TaxExempt) balances pass tax-free. The result is charged at the same brackets,
	/// inflation indexing and credits used for annual settlement.
	/// </summary>
	private MemberTax CalculateTerminalTax(
		Plan plan,
		CompiledPlan compiledPlan,
		IReadOnlyList<CalculatedPeriod> periods,
		int planStartYear
	) {
		if( periods.Count == 0 ) {
			return new MemberTax( 0m, 0m );
		}

		CalculatedPeriod finalPeriod = periods[^1];
		DateOnly deathDate = compiledPlan.Members.Max( m => m.DeathDate );
		decimal inflationIndex = InflationIndex(
			plan.AnnualInflationPercent, finalPeriod.PeriodDate, planStartYear );

		IReadOnlyDictionary<AssetId, CompiledAsset> assetsById = compiledPlan.Assets.ToDictionary( a => a.AssetId );

		// Attribute each remaining balance's deemed income to the member who owns the account.
		// The estate is deemed to dispose of the whole balance, so the full amount is realized.
		// Spousal attribution deliberately does not apply here: the rule ceases on death, so a
		// deemed disposition always falls on the annuitant regardless of who contributed.
		Dictionary<MemberId, decimal> deemedIncomeByMember = [];
		foreach( CalculatedAsset asset in finalPeriod.EndingAssets ) {
			CompiledAsset compiledAsset = assetsById[asset.AssetId];
			decimal deemedIncome = Realize( asset, asset.Amount ).TaxableIncome;
			if( deemedIncome > 0m ) {
				Add( deemedIncomeByMember, compiledAsset.MemberId, deemedIncome );
			}
		}

		decimal federalTax = 0m;
		decimal provincialTax = 0m;

		foreach( CompiledMember member in compiledPlan.Members ) {
			if( !deemedIncomeByMember.TryGetValue( member.MemberId, out decimal deemedIncome ) || deemedIncome <= 0m ) {
				continue;
			}

			// A deemed disposition is not eligible pension income, so no splittable income is
			// carried in and the Pension Income Amount does not apply to the terminal bill.
			// No OAS is carried in either: the deemed disposition is settled at death and the
			// year's OAS clawback is already assessed in the normal annual settlement.
			MemberTax memberTax = ComputeMemberTax(
				compiledPlan, member, deemedIncome, [], [], deathDate, inflationIndex );

			federalTax += memberTax.FederalTax;
			provincialTax += memberTax.ProvincialTax;
		}

		return new MemberTax( federalTax, provincialTax );
	}

	/// <summary>
	/// Collects every graphable timeline event for the plan: each member's lifecycle events
	/// (retirement, CPP start, OAS start, inheritance receipt, and death) and the
	/// retirement-income phase transitions (Go-Go, Slow-Go, and No-Go), ordered by date.
	/// </summary>
	private static IReadOnlyList<PlanEvent> BuildEvents(
		Plan plan,
		CompiledPlan compiledPlan
	) {
		List<PlanEvent> events = [];

		foreach( CompiledMember member in compiledPlan.Members ) {
			events.Add( new PlanEvent( member.RetirementDate, $"{member.Name} retires", PlanEventKind.Lifecycle ) );
			events.Add( new PlanEvent( member.CPPStartDate, $"{member.Name} CPP", PlanEventKind.Lifecycle ) );
			events.Add( new PlanEvent( member.OASStartDate, $"{member.Name} OAS", PlanEventKind.Lifecycle ) );
			events.Add( new PlanEvent( member.DeathDate, $"{member.Name} dies", PlanEventKind.Lifecycle ) );

			// An inheritance is received once, in the month the member reaches the stated age,
			// and only if the member lives to see it. One of no value is not worth marking.
			foreach( Inheritance inheritance in plan.Inheritance.Where( i => i.Member == member.Name && i.Amount > 0m ) ) {
				DateOnly receiptDate = member.BirthDate.AddYears( inheritance.AgeReceived );
				if( receiptDate <= member.DeathDate ) {
					events.Add( new PlanEvent( receiptDate, $"{member.Name} inheritance", PlanEventKind.Lifecycle ) );
				}
			}
		}

		RetirementPhaseSchedule schedule = compiledPlan.RetirementPhaseSchedule;
		events.Add( new PlanEvent( schedule.GoGoStart, "Go-Go", PlanEventKind.RetirementPhase ) );
		events.Add( new PlanEvent( schedule.SlowGoStart, "Slow-Go", PlanEventKind.RetirementPhase ) );
		events.Add( new PlanEvent( schedule.NoGoStart, "No-Go", PlanEventKind.RetirementPhase ) );

		return [.. events.OrderBy( e => e.Date )];
	}

	/// <summary>
	/// Rolls a deceased member's assets over to the surviving spouse. Balances move into the
	/// survivor's account of the same tax status, preserving the tax treatment of the balance,
	/// and the deceased member's accounts are left at zero so that no further growth, withdrawals
	/// or tax accrue against them. Validation guarantees every member holds an account of each
	/// tax status, so a destination always exists.
	/// </summary>
	private static void RollOverAssetsOnDeath(
		CompiledPlan compiledPlan,
		List<CalculatedAsset> endingAssets,
		DateOnly periodDate
	) {
		IReadOnlyDictionary<AssetId, CompiledAsset> assetsById = compiledPlan.Assets.ToDictionary( a => a.AssetId );

		foreach( CompiledMember deceased in compiledPlan.Members.Where( m => periodDate >= m.DeathDate ) ) {
			// Unused contribution room is personal and is never inherited: it is extinguished on
			// death rather than passing to the survivor. This runs regardless of whether a
			// survivor exists, and regardless of whether the account still holds a balance.
			for( int i = 0; i < endingAssets.Count; i++ ) {
				CalculatedAsset asset = endingAssets[i];

				if( assetsById[asset.AssetId].MemberId != deceased.MemberId ) {
					continue;
				}

				if( asset.ContributionBacklog != UnlimitedContributionRoom && asset.ContributionBacklog != 0m ) {
					endingAssets[i] = asset with { ContributionBacklog = 0m };
				}
			}

			CompiledMember? survivor = compiledPlan.Members
				.Where( m => m.MemberId != deceased.MemberId && m.DeathDate > periodDate )
				.OrderByDescending( m => m.DeathDate )
				.FirstOrDefault();

			if( survivor is null ) {
				continue;
			}

			for( int i = 0; i < endingAssets.Count; i++ ) {
				CalculatedAsset asset = endingAssets[i];

				if( assetsById[asset.AssetId].MemberId != deceased.MemberId || asset.Amount <= 0m ) {
					continue;
				}

				CompiledAsset target = compiledPlan.Assets
					.First( a => a.MemberId == survivor.MemberId && a.TaxStatus == asset.TaxStatus );

				int targetIndex = endingAssets.FindIndex( a => a.AssetId == target.AssetId );

				// The surviving spouse inherits the cost base along with the balance, so the
				// accrued gain rolls over untaxed rather than being realized on death.
				endingAssets[targetIndex] = endingAssets[targetIndex] with {
					Amount = endingAssets[targetIndex].Amount + asset.Amount,
					CostBase = endingAssets[targetIndex].CostBase + asset.CostBase
				};
				endingAssets[i] = asset with { Amount = 0m, CostBase = 0m };
			}
		}
	}

	private static void DepositSurplus(
		CompiledPlan compiledPlan,
		List<CalculatedAsset> endingAssets,
		DateOnly periodDate,
		decimal surplus
	) {
		// Surplus income belongs to the household rather than to one member, so there is no
		// preferred owner; it simply fills whatever sheltered room is available.
		decimal deposited = ShelterAllocator.Deposit(
			compiledPlan, endingAssets, periodDate, surplus, preferredMemberId: null );

		decimal remaining = surplus - deposited;
		if( remaining <= 0m ) {
			return;
		}

		// No sheltered account had room. The surplus is still the household's money, so it is
		// retained in any remaining account rather than being discarded.
		int fallbackIndex = endingAssets.FindIndex( a => a.Amount >= 0m );
		if( fallbackIndex < 0 ) {
			return;
		}

		endingAssets[fallbackIndex] = endingAssets[fallbackIndex] with {
			Amount = endingAssets[fallbackIndex].Amount + remaining,
			CostBase = endingAssets[fallbackIndex].CostBase + remaining
		};
	}

	/// <summary>
	/// Accumulates the year's taxable base. Withdrawals are measured against
	/// <paramref name="assetsBeforeWithdrawal"/>, because the taxable gain depends on the
	/// balance and cost base as they stood before the withdrawal was taken.
	///
	/// Registered withdrawals are run through <paramref name="spousalAttribution"/> first, since
	/// a withdrawal from a spousal plan is taxed to the contributing spouse to the extent of the
	/// contributions still inside the attribution window.
	/// </summary>
	private static void AccrueTaxableAmounts(
		CompiledPlan compiledPlan,
		IReadOnlyList<CalculatedIncome> taxableIncome,
		IReadOnlyList<CalculatedWithdrawal> withdrawals,
		IReadOnlyList<CalculatedAsset> assetsBeforeWithdrawal,
		Dictionary<MemberId, decimal> yearlyTaxableByMember,
		Dictionary<MemberId, decimal> yearlySplittableByMember,
		Dictionary<MemberId, decimal> yearlyOasByMember,
		SpousalAttributionLedger spousalAttribution,
		int withdrawalYear
	) {
		foreach( CalculatedIncome income in taxableIncome ) {
			Add( yearlyTaxableByMember, income.MemberId, income.Amount );

			// The clawback is capped at the OAS actually received, so it must be tracked apart
			// from the rest of the taxable base.
			if( income.Name == OasIncomeName ) {
				Add( yearlyOasByMember, income.MemberId, income.Amount );
			}
		}

		foreach( CalculatedWithdrawal withdrawal in withdrawals ) {
			CompiledAsset asset = compiledPlan.Assets.Single( a => a.AssetId == withdrawal.AssetId );
			CalculatedAsset snapshot = assetsBeforeWithdrawal.Single( a => a.AssetId == withdrawal.AssetId );

			decimal taxable = Realize( snapshot, withdrawal.Amount ).TaxableIncome;
			if( taxable != 0m ) {
				// Attribution applies only to registered plans; other accounts are always taxed
				// to their owner.
				IReadOnlyList<AttributedIncome> attributed = asset.TaxStatus == AssetTaxStatus.Taxable
					? spousalAttribution.Attribute( asset.MemberId, withdrawalYear, taxable )
					: [new AttributedIncome( asset.MemberId, taxable )];

				foreach( AttributedIncome share in attributed ) {
					Add( yearlyTaxableByMember, share.MemberId, share.Amount );

					// Only fully-taxable RRSP withdrawal income is eligible for pension splitting,
					// and it splits from whoever is actually taxed on it.
					if( asset.TaxStatus == AssetTaxStatus.Taxable ) {
						Add( yearlySplittableByMember, share.MemberId, share.Amount );
					}
				}
			}
		}
	}

	/// <summary>
	/// The taxable income realized by withdrawing <paramref name="withdrawn"/> from
	/// <paramref name="asset"/>, together with the cost base that withdrawal consumes.
	///
	/// RRSP (Taxable) withdrawals are fully taxable and carry no cost base. TFSA (TaxExempt)
	/// withdrawals are never taxable. Non-registered (CapitalGains) withdrawals realize a
	/// proportional share of the accrued gain: taking a fraction of the balance realizes that
	/// same fraction of the gain and consumes that same fraction of the cost base. Only half of
	/// the realized gain is included in income, per the capital-gains inclusion rate.
	/// </summary>
	private static RealizedAmounts Realize(
		CalculatedAsset asset,
		decimal withdrawn
	) {
		if( withdrawn <= 0m ) {
			return new RealizedAmounts( 0m, 0m );
		}

		switch( asset.TaxStatus ) {
			case AssetTaxStatus.Taxable:
				return new RealizedAmounts( withdrawn, 0m );

			case AssetTaxStatus.TaxExempt:
				return new RealizedAmounts( 0m, 0m );

			case AssetTaxStatus.CapitalGains:
				// Guard the degenerate case: nothing can be realized from an empty account.
				if( asset.Amount <= 0m ) {
					return new RealizedAmounts( 0m, 0m );
				}

				// A withdrawal can never exceed the balance, so the fraction is capped at one.
				decimal fraction = Math.Min( 1m, withdrawn / asset.Amount );
				decimal realizedGain = asset.AccruedGain * fraction;
				decimal costBaseConsumed = asset.CostBase * fraction;

				return new RealizedAmounts( realizedGain * CapitalGainsInclusionRate, costBaseConsumed );

			default:
				throw new ArgumentOutOfRangeException( nameof( asset ), asset.TaxStatus, null );
		}
	}

	/// <summary>
	/// The portion of a realized capital gain that is included in taxable income.
	/// </summary>
	private const decimal CapitalGainsInclusionRate = 0.5m;

	/// <summary>
	/// The taxable income produced by a withdrawal and the cost base it consumes.
	/// </summary>
	private sealed record RealizedAmounts(
		decimal TaxableIncome,
		decimal CostBaseConsumed
	);

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
		if( periodDate < first.BirthDate.AddYears( 65 )
			|| periodDate < second.BirthDate.AddYears( 65 )
		) {
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
		Dictionary<MemberId, decimal> yearlyOasByMember,
		DateOnly periodDate,
		int planStartYear,
		decimal annualInflationPercent,
		List<CalculatedAsset> endingAssets,
		IReadOnlyDictionary<MemberId, decimal> preFundedTaxByMember
	) {
		decimal inflationIndex = InflationIndex( annualInflationPercent, periodDate, planStartYear );

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

			MemberTax memberTax = ComputeMemberTax(
				compiledPlan, member, taxableAmount, yearlySplittableByMember, yearlyOasByMember, periodDate, inflationIndex );

			taxes.Add( new CalculatedTax( member.MemberId, taxableAmount, memberTax.FederalTax, memberTax.ProvincialTax ) );

			// The burndown withdrawal already retained enough of its proceeds to cover the tax it
			// caused, so only the remainder needs to be funded from assets here.
			decimal preFunded = Math.Min(
				preFundedTaxByMember.GetValueOrDefault( member.MemberId ),
				memberTax.TotalTax );

			// Fund the tax bill as an additional withdrawal from assets. The taxable-account
			// portion of that funding is deferred as taxable income into the following year.
			// (Since in actuality the taxes are paid in April, we accept this fudging)
			decimal funded = FundTaxFromAssets(
				compiledPlan,
				member.MemberId,
				memberTax.TotalTax - preFunded,
				endingAssets,
				deferredTaxableByMember
			);

			totalFunded += funded;
			totalUnfunded += memberTax.TotalTax - preFunded - funded;
		}

		totalUnfunded = Math.Round( Math.Max( totalUnfunded, 0m ), 2, MidpointRounding.AwayFromZero );

		return new TaxSettlement( taxes, totalFunded, totalUnfunded, deferredTaxableByMember );
	}

	private static decimal InflationIndex(
		decimal annualInflationPercent,
		DateOnly periodDate,
		int planStartYear
	) {
		return (decimal)Math.Pow(
			(double)( 1m + annualInflationPercent / 100m ),
			periodDate.Year - planStartYear );
	}

	private sealed record MemberTax(
		decimal FederalTax,
		decimal ProvincialTax
	) {
		public decimal TotalTax => FederalTax + ProvincialTax;
	}

	/// <summary>
	/// Calculates a single member's federal and provincial tax on their (post-splitting) taxable
	/// base, after applying the Age Amount and Pension Income Amount non-refundable credits.
	/// </summary>
	private MemberTax ComputeMemberTax(
		CompiledPlan compiledPlan,
		CompiledMember member,
		decimal taxableAmount,
		Dictionary<MemberId, decimal> yearlySplittableByMember,
		Dictionary<MemberId, decimal> yearlyOasByMember,
		DateOnly periodDate,
		decimal inflationIndex
	) {
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
			&& eligiblePensionIncome > 0m
		) {
			decimal pensionIncomeCredit = _taxCalculator.CalculatePensionIncomeCredit(
				compiledPlan.TaxPolicy, eligiblePensionIncome, inflationIndex );
			federalTax = Math.Max( 0m, federalTax - pensionIncomeCredit );
		}

		// The OAS recovery tax is an additional federal tax rather than a credit, so it is added
		// after the credits above (which can only reduce tax to zero) have been applied.
		decimal oasReceived = yearlyOasByMember.GetValueOrDefault( member.MemberId );
		if( oasReceived > 0m ) {
			federalTax += _taxCalculator.CalculateOasClawback(
				compiledPlan.TaxPolicy, taxableAmount, oasReceived, inflationIndex );
		}

		return new MemberTax( federalTax, provincialTax );
	}

	/// <summary>
	/// Calculates the per-member tax attributable to the burndown withdrawal as the difference
	/// between the year's full tax bill including the burndown income and the tax bill that would
	/// have been owed without it. The full tax calculator is used on both bases so that bracket
	/// progression, credits, and pension splitting are all reflected in the marginal cost.
	/// </summary>
	private Dictionary<MemberId, decimal> CalculateBurndownTax(
		Plan plan,
		CompiledPlan compiledPlan,
		Dictionary<MemberId, decimal> taxableWithBurndown,
		Dictionary<MemberId, decimal> splittableWithBurndown,
		Dictionary<MemberId, decimal> taxableWithoutBurndown,
		Dictionary<MemberId, decimal> splittableWithoutBurndown,
		Dictionary<MemberId, decimal> yearlyOasByMember,
		DateOnly periodDate,
		decimal inflationIndex
	) {
		Dictionary<MemberId, decimal> withBurndown = TotalTaxByMember(
			plan, compiledPlan, taxableWithBurndown, splittableWithBurndown, yearlyOasByMember, periodDate, inflationIndex );
		Dictionary<MemberId, decimal> withoutBurndown = TotalTaxByMember(
			plan, compiledPlan, taxableWithoutBurndown, splittableWithoutBurndown, yearlyOasByMember, periodDate, inflationIndex );

		Dictionary<MemberId, decimal> delta = [];
		foreach( CompiledMember member in compiledPlan.Members ) {
			decimal difference = withBurndown.GetValueOrDefault( member.MemberId )
				- withoutBurndown.GetValueOrDefault( member.MemberId );

			if( difference > 0m ) {
				delta[member.MemberId] = difference;
			}
		}

		return delta;
	}

	private Dictionary<MemberId, decimal> TotalTaxByMember(
		Plan plan,
		CompiledPlan compiledPlan,
		Dictionary<MemberId, decimal> yearlyTaxableByMember,
		Dictionary<MemberId, decimal> yearlySplittableByMember,
		Dictionary<MemberId, decimal> yearlyOasByMember,
		DateOnly periodDate,
		decimal inflationIndex
	) {
		Dictionary<MemberId, decimal> taxableByMember = ApplyPensionSplitting(
			plan, compiledPlan, yearlyTaxableByMember, yearlySplittableByMember, periodDate, inflationIndex );

		Dictionary<MemberId, decimal> result = [];
		foreach( CompiledMember member in compiledPlan.Members ) {
			if( !taxableByMember.TryGetValue( member.MemberId, out decimal taxableAmount ) || taxableAmount <= 0m ) {
				continue;
			}

			result[member.MemberId] = ComputeMemberTax(
				compiledPlan, member, taxableAmount, yearlySplittableByMember, yearlyOasByMember, periodDate, inflationIndex ).TotalTax;
		}

		return result;
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

		// Funding order: prefer the member's own accounts, and within that match the withdrawal
		// policy's order (Taxable, then CapitalGains, then TaxExempt) so that paying tax also
		// advances the RRSP burndown and leaves tax-free room intact.
		IEnumerable<CalculatedAsset> ordered = endingAssets
			.OrderBy( a => assetsById[a.AssetId].MemberId == memberId ? 0 : 1 )
			.ThenBy( a => assetsById[a.AssetId].TaxStatus );

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

			RealizedAmounts realized = Realize( asset, deducted );

			endingAssets[index] = asset with {
				Amount = asset.Amount - deducted,
				CostBase = asset.CostBase - realized.CostBaseConsumed
			};
			remaining -= deducted;

			CompiledAsset compiledAsset = assetsById[asset.AssetId];
			if( realized.TaxableIncome != 0m ) {
				Add( deferredTaxableByMember, compiledAsset.MemberId, realized.TaxableIncome );
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
		IReadOnlyList<CalculatedPeriod> periods,
		MemberTax terminalTax
	) {
		decimal totalFederal = periods.SelectMany( p => p.Taxes ).Sum( t => t.FederalTax );
		decimal totalProvincial = periods.SelectMany( p => p.Taxes ).Sum( t => t.ProvincialTax );

		return new TaxSummary(
			TotalFederalTax: totalFederal,
			TotalProvincialTax: totalProvincial,
			TotalTax: totalFederal + totalProvincial,
			TerminalFederalTax: terminalTax.FederalTax,
			TerminalProvincialTax: terminalTax.ProvincialTax
		);
	}

	/// <summary>
	/// Builds the estate roll-up. The gross estate is taken from the same final-period ending
	/// balances the terminal tax is assessed against, so subtracting that tax yields the amount
	/// genuinely passing to beneficiaries rather than double-counting a liability already
	/// deducted elsewhere.
	/// </summary>
	private static EstateSummary BuildEstateSummary(
		Plan plan,
		IReadOnlyList<CalculatedPeriod> periods,
		MemberTax terminalTax,
		int planStartYear
	) {
		decimal grossEstate = periods.Count == 0
			? 0m
			: periods[^1].EndingAssets.Sum( a => a.Amount );

		int finalPeriodYear = periods.Count == 0
			? planStartYear
			: periods[^1].PeriodDate.Year;

		return new EstateSummary(
			GrossEstate: grossEstate,
			TerminalTax: terminalTax.FederalTax + terminalTax.ProvincialTax,
			FinalPeriodYear: finalPeriodYear,
			PlanStartYear: planStartYear,
			AnnualInflationPercent: plan.AnnualInflationPercent
		);
	}
}
