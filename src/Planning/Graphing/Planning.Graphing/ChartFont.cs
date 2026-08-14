using System.Reflection;

namespace Planning.Graphing;

/// <summary>
/// Registers the chart font with ScottPlot so that generated images look the same on every
/// platform.
///
/// By default ScottPlot resolves fonts through SkiaSharp against the fonts installed on the
/// host operating system. Windows and Linux ship different font sets, so the same plot renders
/// with different typefaces, metrics, and hinting depending on where it runs. Naming a font
/// does not fix this on its own: if the named font is not installed, the resolver silently
/// falls back to something else.
///
/// The font is therefore embedded in this assembly and registered explicitly. ScottPlot's
/// font API takes a file path, so the embedded copy is written to a temporary file once per
/// process and registered from there.
/// </summary>
internal static class ChartFont {

	/// <summary>
	/// The font family name used for every text element in generated charts.
	/// </summary>
	public const string FamilyName = "Roboto";

	private const string ResourceName = "Planning.Graphing.Fonts.Roboto-Regular.ttf";

	private static readonly Lock _gate = new();
	private static bool _registered;

	/// <summary>
	/// Registers the embedded font and makes it ScottPlot's default. Safe to call repeatedly;
	/// registration happens only once per process.
	/// </summary>
	public static void EnsureRegistered() {
		lock( _gate ) {
			if( _registered ) {
				return;
			}

			string fontPath = ExtractFont();

			ScottPlot.Fonts.AddFontFile( FamilyName, fontPath );

			// Set every family alias, not just the default. ScottPlot resolves some text
			// elements through the sans/serif/monospace aliases, and leaving those pointing at
			// system fonts would reintroduce the platform difference for those elements.
			ScottPlot.Fonts.Default = FamilyName;
			ScottPlot.Fonts.Sans = FamilyName;
			ScottPlot.Fonts.Serif = FamilyName;
			ScottPlot.Fonts.Monospace = FamilyName;

			_registered = true;
		}
	}

	/// <summary>
	/// Writes the embedded font to a temporary file and returns its path. The file is keyed by
	/// assembly version so that a rebuilt assembly does not reuse a stale extracted font.
	/// </summary>
	private static string ExtractFont() {
		Assembly assembly = typeof( ChartFont ).Assembly;
		string version = assembly.GetName().Version?.ToString() ?? "0";

		string directory = Path.Combine( Path.GetTempPath(), $"Planning.Graphing.Fonts.{version}" );
		Directory.CreateDirectory( directory );

		string fontPath = Path.Combine( directory, "Roboto-Regular.ttf" );
		if( File.Exists( fontPath ) ) {
			return fontPath;
		}

		using Stream? resource = assembly.GetManifestResourceStream( ResourceName )
			?? throw new InvalidOperationException(
				$"The embedded chart font '{ResourceName}' is missing from {assembly.GetName().Name}." );

		// Write to a unique file and then move into place, so that two processes extracting
		// concurrently cannot observe a partially written font.
		string temporaryPath = Path.Combine( directory, $"{Guid.NewGuid():N}.tmp" );
		using( FileStream file = File.Create( temporaryPath ) ) {
			resource.CopyTo( file );
		}

		try {
			File.Move( temporaryPath, fontPath, overwrite: false );
		} catch( IOException ) {
			// Another process won the race and already put the font in place, which is fine.
			File.Delete( temporaryPath );
		}

		return fontPath;
	}
}
