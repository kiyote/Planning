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

	private static int Main( string[] args ) {
		if( args.Length != 1 ) {
			Console.Error.WriteLine( "Usage: planning <input-plan.json>" );
			Console.Error.WriteLine();
			Console.Error.WriteLine( "  <input-plan.json>  Path to a JSON file describing a Plan." );
			Console.Error.WriteLine();
			Console.Error.WriteLine( "Writes two files alongside the input, sharing its name:" );
			Console.Error.WriteLine( "  <input-plan>.csv   The calculated CSV report." );
			Console.Error.WriteLine( "  <input-plan>.png   The calculated plan graph." );
			return 1;
		}

		string inputPath = args[0];

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
			plan = JsonSerializer.Deserialize<Plan>( json, SerializerOptions )
				?? throw new InvalidOperationException( "The plan JSON deserialized to null." );
		}
		catch( JsonException ex ) {
			Console.Error.WriteLine( $"Failed to parse plan JSON: {ex.Message}" );
			return 1;
		}
		catch( Exception ex ) {
			Console.Error.WriteLine( $"Failed to read plan file: {ex.Message}" );
			return 1;
		}

		try {
			CompiledPlan compiledPlan = new PlanCompiler().Compile( plan );
			CalculatedPlan calculatedPlan = new PlanCalculator().Calculate( plan, compiledPlan );

			using( StreamWriter writer = new StreamWriter( csvPath ) ) {
				new PlanReporter().WriteToCsv( writer, compiledPlan, calculatedPlan );
			}

			new PlanGrapher().SaveTotalAssetsByYear( calculatedPlan, pngPath );
		}
		catch( Exception ex ) {
			Console.Error.WriteLine( $"Failed to calculate plan: {ex.Message}" );
			return 1;
		}

		Console.WriteLine( $"Wrote calculated plan report to {csvPath}" );
		Console.WriteLine( $"Wrote calculated plan graph to {pngPath}" );
		return 0;
	}

	private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions {
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		RespectRequiredConstructorParameters = true,
		Converters = { new JsonStringEnumConverter() }
	};
}
