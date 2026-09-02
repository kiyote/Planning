using System.Text.Json;
using System.Text.Json.Serialization;

using Planning.Calculator;
using Planning.Compiler;
using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Plans;

namespace Planning.Graphing.Tests;

/// <summary>
/// Finds the lowest annual return the plan can tolerate while still funding every period.
///
/// Unlike the other searches here, this one loads sample-plan.json rather than rebuilding the
/// plan in code, so every other variable is held at whatever the file currently says and cannot
/// drift out of sync with it. Only <see cref="Plan.AnnualReturnPercent"/> is varied.
///
/// The result answers "how bad can markets be before this plan breaks", which is the return-side
/// equivalent of the maximum-income search in RetirementIncomeSearchTests.
/// </summary>
public class AnnualReturnSearchTests {

	// Bounds and precision for the binary search over the annual return percentage.
	private const decimal MinReturn = 0m;
	private const decimal MaxReturn = 20m;
	private const decimal Tolerance = 0.01m;

	private static readonly JsonSerializerOptions _serializerOptions = new JsonSerializerOptions {
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		RespectRequiredConstructorParameters = true,
		Converters = { new JsonStringEnumConverter() }
	};

	[Test]
	public void FindMinimumReturnWithoutShortfall() {
		Plan basePlan = LoadSamplePlan();

		// Shortfall is monotonic in return: a higher return can only ever fund more, never less.
		// That is what makes a binary search valid here rather than an exhaustive scan.
		if( HasShortfall( basePlan, MaxReturn ) ) {
			Assert.Fail(
				$"Even a {MaxReturn}% return leaves a shortfall, so no solvent return exists to find." );
		}

		decimal low = MinReturn;
		decimal high = MaxReturn;

		// Invariant: low always has a shortfall (or is the floor), high never does. The answer is
		// the boundary they converge on.
		bool floorIsSolvent = !HasShortfall( basePlan, MinReturn );

		if( !floorIsSolvent ) {
			while( high - low > Tolerance ) {
				decimal candidate = ( low + high ) / 2m;
				if( HasShortfall( basePlan, candidate ) ) {
					low = candidate;
				} else {
					high = candidate;
				}
			}
		} else {
			high = MinReturn;
		}

		// Round up to the tolerance so the reported figure is one that actually clears, rather
		// than a boundary value that might still fail by rounding.
		decimal minimumReturn = Math.Ceiling( high * 100m ) / 100m;

		CalculatedPlan result = Calculate( basePlan, minimumReturn );
		CalculatedPlan configured = Calculate( basePlan, basePlan.AnnualReturnPercent );

		TestContext.Out.WriteLine(
			$"Configured AnnualReturnPercent: {basePlan.AnnualReturnPercent:F2}%" );
		TestContext.Out.WriteLine(
			$"  HasShortfall at configured rate: {configured.InsufficientFunds.HasShortfall}" );
		TestContext.Out.WriteLine(
			$"  Unfunded shortfall at configured rate: {configured.InsufficientFunds.TotalUnfundedShortfall:N2}" );
		TestContext.Out.WriteLine( "" );
		TestContext.Out.WriteLine(
			$"Minimum AnnualReturnPercent with no shortfall: {minimumReturn:F2}%" );
		TestContext.Out.WriteLine(
			$"  HasShortfall: {result.InsufficientFunds.HasShortfall}" );
		TestContext.Out.WriteLine(
			$"  Final total assets: {result.Periods[^1].TotalAssets:N2}" );
		TestContext.Out.WriteLine(
			$"  Margin below configured rate: {basePlan.AnnualReturnPercent - minimumReturn:F2} points" );

		Assert.That( result.InsufficientFunds.HasShortfall, Is.False,
			"The reported minimum return should not produce a shortfall." );
	}

	[Test]
	public void ReportShortfallAcrossReturnRange() {
		// A readable picture of how sensitive the plan is to return, for cases where the single
		// break-even figure hides how steep the cliff is on either side of it.
		Plan basePlan = LoadSamplePlan();

		TestContext.Out.WriteLine( "Annual return sweep (all other variables held at sample-plan.json):" );

		for( decimal rate = 1m; rate <= 8m; rate += 0.5m ) {
			CalculatedPlan result = Calculate( basePlan, rate );
			InsufficientFundsSummary funds = result.InsufficientFunds;

			string marker = funds.HasShortfall ? "" : "  <== solvent";

			TestContext.Out.WriteLine(
				$"  {rate,5:F2}%  Shortfall={funds.HasShortfall,-5}  " +
				$"Periods={funds.ShortfallPeriodCount,4}  " +
				$"Unfunded={funds.TotalUnfundedShortfall,14:N2}{marker}" );
		}

		Assert.Pass();
	}

	private static bool HasShortfall(
		Plan basePlan,
		decimal annualReturnPercent
	) {
		return Calculate( basePlan, annualReturnPercent ).InsufficientFunds.HasShortfall;
	}

	private static CalculatedPlan Calculate(
		Plan basePlan,
		decimal annualReturnPercent
	) {
		// Every other field carries over from the loaded plan untouched.
		Plan plan = basePlan with { AnnualReturnPercent = annualReturnPercent };
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
