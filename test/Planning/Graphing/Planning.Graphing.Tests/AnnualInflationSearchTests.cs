using System.Text.Json;
using System.Text.Json.Serialization;

using Planning.Calculator;
using Planning.Compiler;
using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Plans;

namespace Planning.Graphing.Tests;

/// <summary>
/// Finds the highest annual inflation the plan can absorb while still funding every period,
/// evaluated at more than one assumed rate of return.
///
/// This is the mirror of <see cref="AnnualReturnSearchTests"/>: return and inflation push in
/// opposite directions, so the search here looks for an upper bound rather than a lower one.
/// What matters in combination is the real (inflation-adjusted) return, which is why the answer
/// moves so sharply with the return assumption.
///
/// Every other variable is held at whatever sample-plan.json currently says.
/// </summary>
public class AnnualInflationSearchTests {

	// The return assumptions the inflation ceiling is measured against.
	private static readonly decimal[] ReturnRates = [5.5m, 6.0m];

	// Bounds and precision for the binary search over the annual inflation percentage.
	private const decimal MinInflation = 0m;
	private const decimal MaxInflation = 15m;
	private const decimal Tolerance = 0.01m;

	private static readonly JsonSerializerOptions _serializerOptions = new JsonSerializerOptions {
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		RespectRequiredConstructorParameters = true,
		Converters = { new JsonStringEnumConverter() }
	};

	[Test]
	public void FindMaximumInflationWithoutShortfall() {
		Plan basePlan = LoadSamplePlan();

		TestContext.Out.WriteLine(
			$"Configured plan: AnnualReturnPercent={basePlan.AnnualReturnPercent:F2}%, " +
			$"AnnualInflationPercent={basePlan.AnnualInflationPercent:F2}%" );
		TestContext.Out.WriteLine( "" );

		foreach( decimal returnRate in ReturnRates ) {
			ReportCeilingFor( basePlan, returnRate );
			TestContext.Out.WriteLine( "" );
		}
	}

	[Test]
	public void ReportShortfallAcrossInflationRange() {
		// The single ceiling figure hides how quickly the plan deteriorates once it is crossed,
		// so the same range is tabulated at each return rate for comparison.
		Plan basePlan = LoadSamplePlan();

		foreach( decimal returnRate in ReturnRates ) {
			TestContext.Out.WriteLine(
				$"Inflation sweep at {returnRate:F2}% return (all else held at sample-plan.json):" );

			for( decimal inflation = 0m; inflation <= 5m; inflation += 0.5m ) {
				CalculatedPlan result = Calculate( basePlan, returnRate, inflation );
				InsufficientFundsSummary funds = result.InsufficientFunds;

				string marker = funds.HasShortfall ? "" : "  <== solvent";

				TestContext.Out.WriteLine(
					$"  {inflation,5:F2}%  Shortfall={funds.HasShortfall,-5}  " +
					$"Periods={funds.ShortfallPeriodCount,4}  " +
					$"Unfunded={funds.TotalUnfundedShortfall,14:N2}{marker}" );
			}

			TestContext.Out.WriteLine( "" );
		}

		Assert.Pass();
	}

	private static void ReportCeilingFor(
		Plan basePlan,
		decimal returnRate
	) {
		// Inflation runs the opposite way to return: the plan is solvent at the low end and
		// fails at the high end, so the search brackets the ceiling from below.
		if( HasShortfall( basePlan, returnRate, MinInflation ) ) {
			TestContext.Out.WriteLine(
				$"At {returnRate:F2}% return: no solvent inflation exists — the plan already " +
				$"fails at {MinInflation:F2}% inflation." );

			return;
		}

		decimal low = MinInflation;
		decimal high = MaxInflation;

		if( !HasShortfall( basePlan, returnRate, MaxInflation ) ) {
			TestContext.Out.WriteLine(
				$"At {returnRate:F2}% return: still solvent at {MaxInflation:F2}% inflation, " +
				$"so the ceiling lies above the search range." );

			return;
		}

		// Invariant: low is always solvent, high never is. They converge on the ceiling.
		while( high - low > Tolerance ) {
			decimal candidate = ( low + high ) / 2m;
			if( HasShortfall( basePlan, returnRate, candidate ) ) {
				high = candidate;
			} else {
				low = candidate;
			}
		}

		// Round down to the tolerance so the reported figure is one that actually clears.
		decimal maximumInflation = Math.Floor( low * 100m ) / 100m;

		CalculatedPlan result = Calculate( basePlan, returnRate, maximumInflation );

		TestContext.Out.WriteLine( $"At {returnRate:F2}% return:" );
		TestContext.Out.WriteLine(
			$"  Maximum AnnualInflationPercent with no shortfall: {maximumInflation:F2}%" );
		TestContext.Out.WriteLine(
			$"  Implied real return at that ceiling: {returnRate - maximumInflation:F2} points" );
		TestContext.Out.WriteLine(
			$"  HasShortfall: {result.InsufficientFunds.HasShortfall}" );
		TestContext.Out.WriteLine(
			$"  Nominal net estate: {result.EstateSummary.NetEstate:N2}" );
		TestContext.Out.WriteLine(
			$"  Net estate in plan-start dollars: {result.EstateSummary.NetEstateInPlanStartDollars:N2}" );
		TestContext.Out.WriteLine(
			$"  Headroom above configured {basePlan.AnnualInflationPercent:F2}%: " +
			$"{maximumInflation - basePlan.AnnualInflationPercent:F2} points" );

		Assert.That( result.InsufficientFunds.HasShortfall, Is.False,
			$"The reported ceiling at {returnRate:F2}% return should not produce a shortfall." );
	}

	private static bool HasShortfall(
		Plan basePlan,
		decimal annualReturnPercent,
		decimal annualInflationPercent
	) {
		return Calculate( basePlan, annualReturnPercent, annualInflationPercent )
			.InsufficientFunds.HasShortfall;
	}

	private static CalculatedPlan Calculate(
		Plan basePlan,
		decimal annualReturnPercent,
		decimal annualInflationPercent
	) {
		// Only the two rates under test differ from the loaded plan.
		Plan plan = basePlan with {
			AnnualReturnPercent = annualReturnPercent,
			AnnualInflationPercent = annualInflationPercent
		};

		CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );

		return new PlanCalculator().Calculate( plan, compiledPlan );
	}

	/// <summary>
	/// Loads the CLI's sample plan by walking up from the test binary to the repository root, so
	/// the sweep always reflects the file the user is actually editing.
	/// </summary>
	private static Plan LoadSamplePlan() {
		DirectoryInfo? directory = new DirectoryInfo( TestContext.CurrentContext.TestDirectory );

		while( directory is not null ) {
			string candidate = Path.Combine(
				directory.FullName, "src", "Planning", "Cli", "Planning.Cli", "sample-plan.json" );

			if( File.Exists( candidate ) ) {
				Plan? plan = JsonSerializer.Deserialize<Plan>(
					File.ReadAllText( candidate ), _serializerOptions );

				Assert.That( plan, Is.Not.Null, $"Failed to deserialize {candidate}." );

				return plan!;
			}

			directory = directory.Parent;
		}

		throw new FileNotFoundException(
			"Could not locate sample-plan.json by walking up from the test directory." );
	}
}
