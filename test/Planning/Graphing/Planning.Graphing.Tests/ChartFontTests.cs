using Planning.Graphing;

namespace Planning.Graphing.Tests;

/// <summary>
/// Covers the embedded chart font.
///
/// ScottPlot resolves fonts through SkiaSharp against the host operating system's installed
/// fonts, so the same plot renders differently on Windows and Linux. The font is therefore
/// embedded in the graphing assembly and registered explicitly at render time.
///
/// The important property to protect is that the font is genuinely registered rather than
/// silently falling back to a system font, because a fallback would look correct on a
/// developer's Windows machine while still producing different output on Linux.
/// </summary>
public class ChartFontTests {

	/// <summary>
	/// The font must resolve to a real typeface after registration, and that typeface must
	/// actually be the embedded font. This is the check that distinguishes real registration
	/// from a silent fallback: SkiaSharp substitutes a typeface for an unknown family rather
	/// than failing, so asserting on the resolved family name is what catches it.
	/// </summary>
	[Test]
	public void EnsureRegistered_ResolvesToTheEmbeddedTypefaceRatherThanASystemFallback() {
		ChartFont.EnsureRegistered();

		SkiaSharp.SKTypeface typeface = ScottPlot.Fonts.GetTypeface( ChartFont.FamilyName, bold: false, italic: false );

		Assert.Multiple( () => {
			Assert.That( typeface, Is.Not.Null,
				$"'{ChartFont.FamilyName}' must resolve to a typeface after registration." );
			Assert.That( typeface.FamilyName, Is.EqualTo( ChartFont.FamilyName ),
				$"The resolved typeface must be the embedded '{ChartFont.FamilyName}'. A different "
				+ "family name means SkiaSharp substituted a system font, which would make output "
				+ "differ between Windows and Linux." );
		} );
	}

	/// <summary>
	/// Registration must also make the embedded font the default, so that any text element
	/// which is not styled explicitly still avoids the host's system fonts.
	/// </summary>
	[Test]
	public void EnsureRegistered_MakesTheEmbeddedFontTheDefaultForEveryAlias() {
		ChartFont.EnsureRegistered();

		Assert.Multiple( () => {
			Assert.That( ScottPlot.Fonts.Default, Is.EqualTo( ChartFont.FamilyName ) );
			Assert.That( ScottPlot.Fonts.Sans, Is.EqualTo( ChartFont.FamilyName ) );
			Assert.That( ScottPlot.Fonts.Serif, Is.EqualTo( ChartFont.FamilyName ) );
			Assert.That( ScottPlot.Fonts.Monospace, Is.EqualTo( ChartFont.FamilyName ) );
		} );
	}

	/// <summary>
	/// Registration is performed on every render, so it must be safe to call repeatedly and
	/// must not depend on being the first caller.
	/// </summary>
	[Test]
	public void EnsureRegistered_CalledRepeatedly_RemainsRegistered() {
		ChartFont.EnsureRegistered();
		ChartFont.EnsureRegistered();
		ChartFont.EnsureRegistered();

		SkiaSharp.SKTypeface typeface = ScottPlot.Fonts.GetTypeface( ChartFont.FamilyName, bold: false, italic: false );

		Assert.That( typeface.FamilyName, Is.EqualTo( ChartFont.FamilyName ),
			"Repeated registration must be a no-op rather than corrupting the registered font." );
	}

	/// <summary>
	/// The whole approach depends on the font actually being embedded in the assembly. If the
	/// build stops embedding it, registration would throw at render time; this test fails
	/// faster and says why.
	/// </summary>
	[Test]
	public void GraphingAssembly_EmbedsTheFontResource() {
		string[] resources = typeof( PlanGrapher ).Assembly.GetManifestResourceNames();

		Assert.That( resources, Has.One.EqualTo( "Planning.Graphing.Fonts.Roboto-Regular.ttf" ),
			"The chart font must be embedded in the assembly so that no system font is required." );
	}
}
