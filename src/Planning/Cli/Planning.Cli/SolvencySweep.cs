using Planning.Calculator;
using Planning.Compiler;
using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Plans;

namespace Planning.Cli;

/// <summary>
/// Finds the edges of a plan's solvency along whichever axis is being swept.
///
/// <see cref="RunAnnualPercents"/> searches the rate assumptions: the lowest rate of return the
/// plan can survive, and the highest inflation it can absorb at each of several assumed returns.
/// Return and inflation push in opposite directions, so one search looks for a lower bound and
/// the other for an upper bound. What actually governs the outcome is the gap between them --
/// the real return -- which is why both figures move together when the plan changes.
///
/// <see cref="RunRetirementIncome"/> instead holds the rates fixed and searches for the largest
/// retirement income the plan can sustain.
///
/// <see cref="RunRetirementAges"/> holds the rates and the income fixed and searches for the
/// earliest retirement the plan can still fund.
///
/// In both cases every variable other than the one being searched is left at whatever the plan
/// file says, so editing something like a member's TargetAgeInYears and re-running reports the
/// new edges.
/// </summary>
internal static class SolvencySweep {

	private const decimal MinReturn = 0m;
	private const decimal MaxReturn = 25m;
	private const decimal MinInflation = 0m;
	private const decimal MaxInflation = 25m;
	private const decimal MinGoGo = 0m;
	private const decimal MaxGoGo = 100_000m;

	public static void RunAnnualPercents(
		Plan plan,
		IReadOnlyList<decimal> returnRates,
		decimal tolerance,
		TextWriter output
	) {
		WritePlanSummary( plan, output );

		ReportMinimumReturn( plan, tolerance, output );
		output.WriteLine();

		foreach( decimal returnRate in returnRates ) {
			ReportMaximumInflation( plan, returnRate, tolerance, output );
			output.WriteLine();
		}
	}

	/// <summary>
	/// Searches for the largest sustainable retirement income, holding the rate assumptions at
	/// whatever the plan file says. The three spending phases move together: SlowGo and NoGo are
	/// derived from GoGo by the supplied ratios, so a single value is searched rather than three
	/// independent ones.
	/// </summary>
	public static void RunRetirementIncome(
		Plan plan,
		decimal slowGoRatio,
		decimal noGoRatio,
		decimal tolerance,
		TextWriter output
	) {
		WritePlanSummary( plan, output );

		output.WriteLine(
			$"Highest sustainable GoGo income at {plan.AnnualReturnPercent:F2}% return / " +
			$"{plan.AnnualInflationPercent:F2}% inflation:" );
		output.WriteLine(
			$"  Holding SlowGo at {slowGoRatio:P0} of GoGo and NoGo at {noGoRatio:P0} of GoGo." );

		if( HasShortfallAtIncome( plan, MinGoGo, slowGoRatio, noGoRatio ) ) {
			output.WriteLine(
				$"  Short even at a GoGo of {MinGoGo:N2} -- the plan cannot fund itself at any " +
				"income level." );
			return;
		}

		if( !HasShortfallAtIncome( plan, MaxGoGo, slowGoRatio, noGoRatio ) ) {
			output.WriteLine(
				$"  Still solvent at a GoGo of {MaxGoGo:N2} -- the ceiling lies above the " +
				"search range." );
			return;
		}

		// Invariant: low always clears, high always fails. They converge on the ceiling.
		decimal low = MinGoGo;
		decimal high = MaxGoGo;

		while( high - low > tolerance ) {
			decimal candidate = ( low + high ) / 2m;
			if( HasShortfallAtIncome( plan, candidate, slowGoRatio, noGoRatio ) ) {
				high = candidate;
			} else {
				low = candidate;
			}
		}

		// Round down to the tolerance so the reported figure is one that actually clears.
		decimal maximumGoGo = Math.Floor( low / tolerance ) * tolerance;

		RetirementIncome income = ScaleIncome( plan, maximumGoGo, slowGoRatio, noGoRatio );
		CalculatedPlan result = CalculateAtIncome( plan, income );

		output.WriteLine( $"  Maximum GoGo:  {income.GoGo:N2} per month" );
		output.WriteLine(
			$"  Implied SlowGo: {income.SlowGo:N2} per month for {income.SlowGoYears} year(s)" );
		output.WriteLine(
			$"  Implied NoGo:   {income.NoGo:N2} per month for {income.NoGoYears} year(s)" );
		output.WriteLine(
			$"  Change from the configured GoGo of {plan.RetirementIncome.GoGo:N2}: " +
			$"{income.GoGo - plan.RetirementIncome.GoGo:N2} per month" );
		output.WriteLine(
			$"  At that income: net estate {result.EstateSummary.NetEstate:N2} " +
			$"({result.EstateSummary.NetEstateInPlanStartDollars:N2} in plan-start dollars)" );
	}

	/// <summary>
	/// Searches for the earliest retirement the plan can still fund, holding inflation, return
	/// and retirement income at whatever the plan file says. Only members that already declare a
	/// retirement age are moved; a member with no retirement age never retires, so there is
	/// nothing to search for them. When both members declare one they move in lockstep -- the
	/// same number of years is added to each -- so the household keeps the relative stagger it
	/// was configured with.
	/// </summary>
	public static void RunRetirementAges(
		Plan plan,
		TextWriter output
	) {
		WritePlanSummary( plan, output );

		Member[] members = [.. plan.Members];
		Member[] retiring = [.. members.Where( m => m.RetirementAgeInYears.HasValue )];

		output.WriteLine(
			$"Earliest solvent retirement at {plan.AnnualReturnPercent:F2}% return / " +
			$"{plan.AnnualInflationPercent:F2}% inflation:" );

		if( retiring.Length == 0 ) {
			output.WriteLine( "  No member declares a retirement age -- there is nothing to search." );
			return;
		}

		if( retiring.Length == 1 ) {
			output.WriteLine(
				$"  Only {retiring[0].Name} retires; the other member's age is left alone." );
		} else {
			output.WriteLine(
				"  Retirement ages move in lockstep, preserving the configured stagger of " +
				$"{DescribeStagger( retiring )}." );
		}

		// A member cannot retire before the plan starts, and must still reach their target age
		// after retiring, so the shift is bounded by whichever member binds first.
		int minimumShift = retiring.Max( m => AgeAt( m, plan.StartDate ) - m.RetirementAgeInYears!.Value );
		int maximumShift = retiring.Min( m => m.TargetAgeInYears - 1 - m.RetirementAgeInYears!.Value );

		if( minimumShift > maximumShift ) {
			output.WriteLine( "  No retirement age satisfies every member -- nothing to search." );
			return;
		}

		// Ages are whole years and the range is a few decades at most, so walk it from the
		// earliest candidate upward rather than assuming later retirement is always safer.
		for( int shift = minimumShift; shift <= maximumShift; shift++ ) {
			Plan candidate = ShiftRetirementAges( plan, shift );

			if( HasShortfallInPlan( candidate ) ) {
				continue;
			}

			CalculatedPlan result = CalculateFor( candidate );

			foreach( Member member in candidate.Members.Where( m => m.RetirementAgeInYears.HasValue ) ) {
				Member configured = members.First( m => m.Name == member.Name );
				output.WriteLine(
					$"  {member.Name}: retires at {member.RetirementAgeInYears}, " +
					$"{Describe( member.RetirementAgeInYears!.Value - configured.RetirementAgeInYears!.Value )} " +
					$"the configured {configured.RetirementAgeInYears}" );
			}

			output.WriteLine(
				$"  At those ages: net estate {result.EstateSummary.NetEstate:N2} " +
				$"({result.EstateSummary.NetEstateInPlanStartDollars:N2} in plan-start dollars)" );
			return;
		}

		output.WriteLine(
			"  Short at every retirement age in the range -- no retirement date funds this plan." );
	}

	private static string Describe(
		int yearsFromConfigured
	) {
		if( yearsFromConfigured == 0 ) {
			return "matching";
		}

		int years = Math.Abs( yearsFromConfigured );
		string direction = yearsFromConfigured < 0 ? "earlier than" : "later than";

		return $"{years} year(s) {direction}";
	}

	private static string DescribeStagger(
		IReadOnlyList<Member> retiring
	) {
		return string.Join(
			", ",
			retiring.Select( m => $"{m.Name} at {m.RetirementAgeInYears}" ) );
	}

	/// <summary>
	/// Adds the same number of years to every declared retirement age. Members without one are
	/// left untouched so the sweep never invents a retirement they did not ask for.
	/// </summary>
	private static Plan ShiftRetirementAges(
		Plan plan,
		int shift
	) {
		return plan with {
			Members = [.. plan.Members.Select( m => m.RetirementAgeInYears.HasValue
				? m with { RetirementAgeInYears = m.RetirementAgeInYears.Value + shift }
				: m )]
		};
	}

	private static int AgeAt(
		Member member,
		DateOnly date
	) {
		int age = date.Year - member.BirthDate.Year;

		if( member.BirthDate.AddYears( age ) > date ) {
			age--;
		}

		return age;
	}

	private static void WritePlanSummary(
		Plan plan,
		TextWriter output
	) {
		output.WriteLine( "As configured:" );
		output.WriteLine( $"  AnnualReturnPercent:    {plan.AnnualReturnPercent:F2}%" );
		output.WriteLine( $"  AnnualInflationPercent: {plan.AnnualInflationPercent:F2}%" );
		output.WriteLine(
			$"  RetirementIncome:       GoGo {plan.RetirementIncome.GoGo:N2}, " +
			$"SlowGo {plan.RetirementIncome.SlowGo:N2}, NoGo {plan.RetirementIncome.NoGo:N2}" );

		foreach( Member member in plan.Members ) {
			output.WriteLine(
				$"  {member.Name}: born {member.BirthDate:yyyy-MM-dd}, " +
				$"target age {member.TargetAgeInYears}" );
		}

		CalculatedPlan baseline = Calculate(
			plan, plan.AnnualReturnPercent, plan.AnnualInflationPercent );

		InsufficientFundsSummary funds = baseline.InsufficientFunds;

		if( funds.HasShortfall ) {
			output.WriteLine(
				$"  Result: SHORTFALL -- {funds.ShortfallPeriodCount} period(s), " +
				$"{funds.TotalUnfundedShortfall:N2} unfunded" );
		} else {
			output.WriteLine(
				$"  Result: solvent -- net estate {baseline.EstateSummary.NetEstate:N2} " +
				$"({baseline.EstateSummary.NetEstateInPlanStartDollars:N2} in plan-start dollars)" );
		}

		output.WriteLine();
	}

	/// <summary>
	/// Searches for the lowest return that still funds every period, holding inflation at the
	/// plan's configured value.
	/// </summary>
	private static void ReportMinimumReturn(
		Plan plan,
		decimal tolerance,
		TextWriter output
	) {
		decimal inflation = plan.AnnualInflationPercent;

		output.WriteLine( $"Lowest AnnualReturnPercent at {inflation:F2}% inflation:" );

		if( !HasShortfall( plan, MinReturn, inflation ) ) {
			output.WriteLine(
				$"  Solvent even at {MinReturn:F2}% return -- the plan does not depend on growth." );
			return;
		}

		if( HasShortfall( plan, MaxReturn, inflation ) ) {
			output.WriteLine(
				$"  Still short at {MaxReturn:F2}% return -- no return within the search range " +
				"funds this plan." );
			return;
		}

		// Invariant: low always fails, high always clears. They converge on the boundary.
		decimal low = MinReturn;
		decimal high = MaxReturn;

		while( high - low > tolerance ) {
			decimal candidate = ( low + high ) / 2m;
			if( HasShortfall( plan, candidate, inflation ) ) {
				low = candidate;
			} else {
				high = candidate;
			}
		}

		// Round up to the tolerance so the reported figure is one that actually clears.
		decimal minimumReturn = Math.Ceiling( high * 100m ) / 100m;

		CalculatedPlan result = Calculate( plan, minimumReturn, inflation );

		output.WriteLine( $"  Minimum: {minimumReturn:F2}%" );
		output.WriteLine(
			$"  Margin below the configured {plan.AnnualReturnPercent:F2}%: " +
			$"{plan.AnnualReturnPercent - minimumReturn:F2} points" );
		WriteOutcome( result, minimumReturn, inflation, output );
	}

	/// <summary>
	/// Searches for the highest inflation that still funds every period at a given return.
	/// </summary>
	private static void ReportMaximumInflation(
		Plan plan,
		decimal returnRate,
		decimal tolerance,
		TextWriter output
	) {
		output.WriteLine( $"Highest AnnualInflationPercent at {returnRate:F2}% return:" );

		if( HasShortfall( plan, returnRate, MinInflation ) ) {
			output.WriteLine(
				$"  Short even at {MinInflation:F2}% inflation -- this return cannot fund the " +
				"plan at any inflation." );
			return;
		}

		if( !HasShortfall( plan, returnRate, MaxInflation ) ) {
			output.WriteLine(
				$"  Still solvent at {MaxInflation:F2}% inflation -- the ceiling lies above the " +
				"search range." );
			return;
		}

		// Invariant: low always clears, high always fails. Mirror of the return search.
		decimal low = MinInflation;
		decimal high = MaxInflation;

		while( high - low > tolerance ) {
			decimal candidate = ( low + high ) / 2m;
			if( HasShortfall( plan, returnRate, candidate ) ) {
				high = candidate;
			} else {
				low = candidate;
			}
		}

		// Round down to the tolerance so the reported figure is one that actually clears.
		decimal maximumInflation = Math.Floor( low * 100m ) / 100m;

		CalculatedPlan result = Calculate( plan, returnRate, maximumInflation );

		output.WriteLine( $"  Maximum: {maximumInflation:F2}%" );
		output.WriteLine(
			$"  Implied real return at that ceiling: {returnRate - maximumInflation:F2} points" );
		output.WriteLine(
			$"  Headroom above the configured {plan.AnnualInflationPercent:F2}%: " +
			$"{maximumInflation - plan.AnnualInflationPercent:F2} points" );
		WriteOutcome( result, returnRate, maximumInflation, output );
	}

	/// <summary>
	/// Reports the estate at a boundary, always restating the rates it belongs to so a
	/// break-even figure is never mistaken for the configured plan's result.
	/// </summary>
	private static void WriteOutcome(
		CalculatedPlan result,
		decimal returnRate,
		decimal inflationRate,
		TextWriter output
	) {
		output.WriteLine(
			$"  At {returnRate:F2}% return / {inflationRate:F2}% inflation: " +
			$"net estate {result.EstateSummary.NetEstate:N2} " +
			$"({result.EstateSummary.NetEstateInPlanStartDollars:N2} in plan-start dollars)" );
	}

	private static bool HasShortfall(
		Plan plan,
		decimal annualReturnPercent,
		decimal annualInflationPercent
	) {
		return Calculate( plan, annualReturnPercent, annualInflationPercent )
			.InsufficientFunds.HasShortfall;
	}

	private static bool HasShortfallAtIncome(
		Plan plan,
		decimal goGo,
		decimal slowGoRatio,
		decimal noGoRatio
	) {
		return CalculateAtIncome( plan, ScaleIncome( plan, goGo, slowGoRatio, noGoRatio ) )
			.InsufficientFunds.HasShortfall;
	}

	/// <summary>
	/// Derives the three spending phases from a single GoGo value. The phase durations are left
	/// untouched, so only the amounts move.
	/// </summary>
	private static RetirementIncome ScaleIncome(
		Plan plan,
		decimal goGo,
		decimal slowGoRatio,
		decimal noGoRatio
	) {
		return plan.RetirementIncome with {
			GoGo = goGo,
			SlowGo = goGo * slowGoRatio,
			NoGo = goGo * noGoRatio
		};
	}

	private static CalculatedPlan CalculateAtIncome(
		Plan basePlan,
		RetirementIncome retirementIncome
	) {
		Plan plan = basePlan with { RetirementIncome = retirementIncome };

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		return new PlanCalculator().Calculate( plan, compiledPlan );
	}

	private static CalculatedPlan Calculate(
		Plan basePlan,
		decimal annualReturnPercent,
		decimal annualInflationPercent
	) {
		Plan plan = basePlan with {
			AnnualReturnPercent = annualReturnPercent,
			AnnualInflationPercent = annualInflationPercent
		};

		return CalculateFor( plan );
	}

	private static bool HasShortfallInPlan(
		Plan plan
	) {
		return CalculateFor( plan ).InsufficientFunds.HasShortfall;
	}

	private static CalculatedPlan CalculateFor(
		Plan plan
	) {
		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		return new PlanCalculator().Calculate( plan, compiledPlan );
	}
}
