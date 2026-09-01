namespace Planning.Graphing.Tests;

/// <summary>
/// Covers the mapping of event dates onto the chart's X axis.
///
/// The bars on the chart are yearly aggregates drawn at integer positions, so a bar for a
/// given year occupies roughly index-0.5 to index+0.5. Event markers ("Todd dies", phase
/// transitions) are drawn as vertical lines over those bars, which means the marker must land
/// on the same bar that reports the consequences of the event.
///
/// The original implementation offset the marker by the fraction of the calendar year elapsed.
/// That pushed late-year events nearly a full bar to the right, so a December death appeared
/// to occur the year after the life-insurance payout that it triggered. These tests pin the
/// corrected behaviour.
/// </summary>
public class PlanGrapherEventPositionTests {

	/// <summary>
	/// A late-December event must sit on its own year's bar. Under the previous fractional
	/// mapping this produced a position of roughly 4.99, visually landing on the following
	/// year's bar.
	/// </summary>
	[Test]
	public void EventPosition_ForALateDecemberDate_LandsOnItsOwnYearBar() {
		double position = InvokeEventPosition( firstYear: 2026, eventDate: new DateOnly( 2030, 12, 31 ) );

		Assert.That( position, Is.EqualTo( 4.0 ),
			"A 2030 event must align with the 2030 bar (index 4), not drift toward the 2031 bar. "
			+ "Drift is what made a December death appear to follow its own insurance payout." );
	}

	/// <summary>
	/// The alignment must not depend on where in the year the event falls: a January and a
	/// December event in the same year belong to the same bar, because the bar aggregates the
	/// entire year.
	/// </summary>
	[Test]
	public void EventPosition_ForDatesInTheSameYear_ResolvesToTheSamePosition() {
		double january = InvokeEventPosition( firstYear: 2026, eventDate: new DateOnly( 2030, 1, 1 ) );
		double december = InvokeEventPosition( firstYear: 2026, eventDate: new DateOnly( 2030, 12, 31 ) );

		Assert.That( december, Is.EqualTo( january ),
			"Both dates fall in the year summarised by a single bar, so both markers must align "
			+ "with that same bar." );
	}

	/// <summary>
	/// The first year of the plan maps to the first bar at index zero.
	/// </summary>
	[Test]
	public void EventPosition_ForTheFirstPlanYear_IsTheFirstBar() {
		double position = InvokeEventPosition( firstYear: 2026, eventDate: new DateOnly( 2026, 6, 15 ) );

		Assert.That( position, Is.EqualTo( 0.0 ) );
	}

	/// <summary>
	/// A leap year must not shift alignment; the previous day-of-year arithmetic varied with
	/// the length of the year.
	/// </summary>
	[Test]
	public void EventPosition_InALeapYear_AlignsWithItsYearBar() {
		double position = InvokeEventPosition( firstYear: 2026, eventDate: new DateOnly( 2032, 12, 31 ) );

		Assert.That( position, Is.EqualTo( 6.0 ),
			"2032 is a leap year; bar alignment must not depend on the number of days in the year." );
	}

	private static double InvokeEventPosition(
		int firstYear,
		DateOnly eventDate
	) {
		System.Reflection.MethodInfo method = typeof( PlanGrapher ).GetMethod(
			"EventPosition",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
		)!;

		return (double)method.Invoke( null, [firstYear, eventDate] )!;
	}
}
