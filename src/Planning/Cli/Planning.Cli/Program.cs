using System.Text.Json;
using System.Text.Json.Serialization;

using Planning.Calculator;
using Planning.Compiler;
using Planning.Graphing;
using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Plans;
using Planning.Reporter;

namespace Planning.Cli;

internal static class Program {

	private static readonly JsonSerializerOptions _serializerOptions = new JsonSerializerOptions {
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		RespectRequiredConstructorParameters = true,
		Converters = { new JsonStringEnumConverter() }
	};

	private const decimal DefaultSweepTolerance = 0.01m;
	private const decimal DefaultIncomeTolerance = 1m;
	private const decimal DefaultSlowGoRatio = 0.80m;
	private const decimal DefaultNoGoRatio = 0.90m;
	private static readonly decimal[] DefaultSweepReturnRates = [5.5m, 6.0m];

	private static int Main(
		string[] args
	) {
		string[] positional = [.. args.Where( a => !a.StartsWith( '-' ) )];
		bool noGraph = args.Any( a => a.Equals( "--no-graph", StringComparison.OrdinalIgnoreCase ) );
		bool sweepAnnualPercents = args.Any( a =>
			a.Equals( "--sweepannualpercents", StringComparison.OrdinalIgnoreCase ) );
		bool sweepRetirementIncome = args.Any( a =>
			a.Equals( "--sweepretirementincome", StringComparison.OrdinalIgnoreCase ) );
		bool sweepAges = args.Any( a =>
			a.Equals( "--sweepages", StringComparison.OrdinalIgnoreCase ) );

		decimal[]? sweepReturnRates;
		decimal? sweepTolerance;
		decimal? slowGoRatio;
		decimal? noGoRatio;

		if( !TryParseDecimalList( args, "--returns", out sweepReturnRates ) ) {
			Console.Error.WriteLine( "Invalid --returns value; expected a comma-separated list of percentages." );
			return 1;
		}

		if( !TryParseDecimal( args, "--tolerance", out sweepTolerance ) ) {
			Console.Error.WriteLine( "Invalid --tolerance value; expected a positive number." );
			return 1;
		}

		if( !TryParseDecimal( args, "--slowgo-ratio", out slowGoRatio ) ) {
			Console.Error.WriteLine( "Invalid --slowgo-ratio value; expected a positive fraction of GoGo." );
			return 1;
		}

		if( !TryParseDecimal( args, "--nogo-ratio", out noGoRatio ) ) {
			Console.Error.WriteLine( "Invalid --nogo-ratio value; expected a positive fraction of GoGo." );
			return 1;
		}

		string[] unknown = [.. args.Where( a =>
			a.StartsWith( '-' )
			&& !a.Equals( "--no-graph", StringComparison.OrdinalIgnoreCase )
			&& !a.Equals( "--sweepannualpercents", StringComparison.OrdinalIgnoreCase )
			&& !a.Equals( "--sweepretirementincome", StringComparison.OrdinalIgnoreCase )
			&& !a.Equals( "--sweepages", StringComparison.OrdinalIgnoreCase )
			&& !a.StartsWith( "--returns=", StringComparison.OrdinalIgnoreCase )
			&& !a.StartsWith( "--tolerance=", StringComparison.OrdinalIgnoreCase )
			&& !a.StartsWith( "--slowgo-ratio=", StringComparison.OrdinalIgnoreCase )
			&& !a.StartsWith( "--nogo-ratio=", StringComparison.OrdinalIgnoreCase ) )];

		int sweepCount = ( sweepAnnualPercents ? 1 : 0 )
			+ ( sweepRetirementIncome ? 1 : 0 )
			+ ( sweepAges ? 1 : 0 );

		if( positional.Length != 1 || unknown.Length != 0 || sweepCount > 1 ) {
			if( unknown.Length != 0 ) {
				Console.Error.WriteLine( $"Unrecognized option: {unknown[0]}" );
				Console.Error.WriteLine();
			}
			if( sweepCount > 1 ) {
				Console.Error.WriteLine( "Choose only one of --sweepannualpercents, --sweepretirementincome or --sweepages." );
				Console.Error.WriteLine();
			}
			Console.Error.WriteLine( "Usage: planning <input-plan.json> [--no-graph]" );
			Console.Error.WriteLine( "       planning <input-plan.json> --sweepannualpercents [--returns=<list>] [--tolerance=<pct>]" );
			Console.Error.WriteLine( "       planning <input-plan.json> --sweepretirementincome [--slowgo-ratio=<f>] [--nogo-ratio=<f>] [--tolerance=<amt>]" );
			Console.Error.WriteLine( "       planning <input-plan.json> --sweepages" );
			Console.Error.WriteLine();
			Console.Error.WriteLine( "  <input-plan.json>       Path to a JSON file describing a Plan." );
			Console.Error.WriteLine( "  --no-graph              Skip writing the plan graph." );
			Console.Error.WriteLine( "  --sweepannualpercents   Report the lowest solvent return and the highest solvent" );
			Console.Error.WriteLine( "                          inflation, instead of writing files." );
			Console.Error.WriteLine( "  --sweepretirementincome Report the highest solvent GoGo income, holding the plan's" );
			Console.Error.WriteLine( "                          rates fixed, instead of writing files." );
			Console.Error.WriteLine( "  --sweepages             Report the earliest solvent retirement age, holding the" );
			Console.Error.WriteLine( "                          plan's rates and income fixed. Members that both declare" );
			Console.Error.WriteLine( "                          a retirement age move in lockstep." );
			Console.Error.WriteLine( "  --returns=<list>        Comma-separated returns to measure the inflation ceiling" );
			Console.Error.WriteLine( "                          against. Defaults to 5.5,6.0." );
			Console.Error.WriteLine( "  --slowgo-ratio=<f>      SlowGo as a fraction of GoGo. Defaults to 0.80." );
			Console.Error.WriteLine( "  --nogo-ratio=<f>        NoGo as a fraction of GoGo. Defaults to 0.90." );
			Console.Error.WriteLine( "  --tolerance=<n>         Search precision. Defaults to 0.01 percentage points for" );
			Console.Error.WriteLine( "                          --sweepannualpercents and 1 dollar for --sweepretirementincome." );
			Console.Error.WriteLine();
			Console.Error.WriteLine( "Without a sweep, writes the following files alongside the input, sharing its name:" );
			Console.Error.WriteLine( "  <input-plan>.csv        The calculated CSV report." );
			Console.Error.WriteLine( "  <input-plan>.png        The calculated plan graph, unless --no-graph is given." );
			return 1;
		}

		string inputPath = positional[0];

		if( !File.Exists( inputPath ) ) {
			Console.Error.WriteLine( $"Input plan file not found: {inputPath}" );
			return 1;
		}

		string fullInputPath = Path.GetFullPath( inputPath );
		string csvPath = Path.ChangeExtension( fullInputPath, ".csv" );
		string pngPath = Path.ChangeExtension( fullInputPath, ".png" );

		Plan plan;
		try {
			string json = File.ReadAllText( inputPath );
			plan = JsonSerializer.Deserialize<Plan>( json, _serializerOptions )
				?? throw new InvalidOperationException( "The plan JSON deserialized to null." );
		} catch( JsonException ex ) {
			Console.Error.WriteLine( $"Failed to parse plan JSON: {ex.Message}" );
			return 1;
		} catch( Exception ex ) {
			Console.Error.WriteLine( $"Failed to read plan file: {ex.Message}" );
			return 1;
		}

		if( sweepAnnualPercents || sweepRetirementIncome || sweepAges ) {
			try {
				if( sweepAnnualPercents ) {
					SolvencySweep.RunAnnualPercents(
						plan,
						sweepReturnRates ?? DefaultSweepReturnRates,
						sweepTolerance ?? DefaultSweepTolerance,
						Console.Out );
				} else if( sweepAges ) {
					SolvencySweep.RunRetirementAges( plan, Console.Out );
				} else {
					SolvencySweep.RunRetirementIncome(
						plan,
						slowGoRatio ?? DefaultSlowGoRatio,
						noGoRatio ?? DefaultNoGoRatio,
						sweepTolerance ?? DefaultIncomeTolerance,
						Console.Out );
				}
			} catch( Exception ex ) {
				Console.Error.WriteLine( $"Failed to sweep plan: {ex.Message}" );
				return 1;
			}

			return 0;
		}

		try {
			CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
			CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );

			using( StreamWriter writer = new StreamWriter( csvPath ) ) {
				new PlanReporter().WriteToCsv( writer, compiledPlan, calculatedPlan );
			}

			if( !noGraph ) {
				new PlanGrapher().SaveTotalAssetsByYear( calculatedPlan, pngPath );
			}
		} catch( Exception ex ) {
			Console.Error.WriteLine( $"Failed to calculate plan: {ex.Message}" );
			return 1;
		}

		Console.WriteLine( $"Wrote calculated plan report to {csvPath}" );
		if( !noGraph ) {
			Console.WriteLine( $"Wrote calculated plan graph to {pngPath}" );
		}
		return 0;
	}

	/// <summary>
	/// Reads a <c>--name=value</c> option as a single decimal. Returns false only when the
	/// option is present but unparseable, so an absent option leaves the default in place.
	/// </summary>
	private static bool TryParseDecimal(
		string[] args,
		string name,
		out decimal? value
	) {
		value = null;

		string prefix = $"{name}=";
		string? arg = args.FirstOrDefault( a =>
			a.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) );

		if( arg is null ) {
			return true;
		}

		if( !decimal.TryParse( arg[prefix.Length..], out decimal parsed ) || parsed <= 0m ) {
			return false;
		}

		value = parsed;
		return true;
	}

	/// <summary>
	/// Reads a <c>--name=a,b,c</c> option as a list of decimals.
	/// </summary>
	private static bool TryParseDecimalList(
		string[] args,
		string name,
		out decimal[]? values
	) {
		values = null;

		string prefix = $"{name}=";
		string? arg = args.FirstOrDefault( a =>
			a.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) );

		if( arg is null ) {
			return true;
		}

		List<decimal> parsedValues = [];

		foreach( string part in arg[prefix.Length..].Split(
			',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) ) {

			if( !decimal.TryParse( part, out decimal parsed ) || parsed < 0m ) {
				return false;
			}

			parsedValues.Add( parsed );
		}

		if( parsedValues.Count == 0 ) {
			return false;
		}

		values = [.. parsedValues];
		return true;
	}
}
