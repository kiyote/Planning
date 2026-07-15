using Planning.Model.CalculatedPlans;

namespace Planning.Graphing;

public class PlanGrapher {

	private const int DefaultWidth = 1200;
	private const int DefaultHeight = 700;

	/// <summary>
	/// Generates a bar graph of the household's total asset value by year and
	/// saves it as a PNG image to the given file path.
	/// </summary>
	/// <param name="calculatedPlan">The calculated plan to graph. Its <see cref="CalculatedPlan.Events"/> are used to annotate the graph with timeline markers.</param>
	/// <param name="filePath">The destination PNG file path.</param>
	/// <param name="width">The image width in pixels.</param>
	/// <param name="height">The image height in pixels.</param>
	public void SaveTotalAssetsByYear(
		CalculatedPlan calculatedPlan,
		string filePath,
		int width = DefaultWidth,
		int height = DefaultHeight
	) {
		ArgumentNullException.ThrowIfNull( calculatedPlan );
		ArgumentException.ThrowIfNullOrWhiteSpace( filePath );

		IReadOnlyList<(int Year, double TotalAssets, double Shortfall, double TotalTax, double BenefitIncome, double TotalWithdrawals)> yearlyTotals = BuildYearlyTotals( calculatedPlan );

		ScottPlot.Plot plot = new ScottPlot.Plot();

		ScottPlot.Color assetColor = ScottPlot.Colors.Blue;
		ScottPlot.Color shortfallColor = ScottPlot.Colors.Red;
		ScottPlot.Color taxColor = ScottPlot.Colors.Black;
		ScottPlot.Color benefitColor = ScottPlot.Colors.Gray;
		ScottPlot.Color withdrawalColor = ScottPlot.Colors.LightBlue;

		List<ScottPlot.Bar> bars = [];
		foreach( (int _, double totalAssets, double shortfall, double _, double _, double _) in yearlyTotals ) {
			int index = bars.Count / 2;

			// Blue asset segment sits at the base of the stack.
			bars.Add( new ScottPlot.Bar {
				Position = index,
				ValueBase = 0,
				Value = totalAssets,
				FillColor = assetColor
			} );

			// Red shortfall segment stacks on top of the asset segment.
			bars.Add( new ScottPlot.Bar {
				Position = index,
				ValueBase = totalAssets,
				Value = totalAssets + shortfall,
				FillColor = shortfallColor
			} );
		}

		plot.Add.Bars( bars );

		// Yellow line overlay of the total taxes paid each year, plotted against a
		// secondary (right) Y axis so its smaller scale remains readable.
		ScottPlot.IYAxis taxAxis = plot.Axes.Right;
		double[] taxXs = [.. Enumerable.Range( 0, yearlyTotals.Count ).Select( i => (double)i )];
		double[] taxYs = [.. yearlyTotals.Select( t => t.TotalTax )];
		ScottPlot.Plottables.Scatter taxLine = plot.Add.Scatter( taxXs, taxYs );
		taxLine.Color = taxColor;
		taxLine.LineWidth = 2;
		taxLine.MarkerSize = 5;
		taxLine.Axes.YAxis = taxAxis;
		taxAxis.Label.Text = "Total Tax";

		// Gray line overlay of the household's total income (all sources) each year, plotted
		// against the same secondary (right) Y axis as the tax line.
		double[] benefitYs = [.. yearlyTotals.Select( t => t.BenefitIncome )];
		ScottPlot.Plottables.Scatter benefitLine = plot.Add.Scatter( taxXs, benefitYs );
		benefitLine.Color = benefitColor;
		benefitLine.LineWidth = 2;
		benefitLine.MarkerSize = 5;
		benefitLine.Axes.YAxis = taxAxis;

		// Light blue line overlay of the total amount withdrawn from assets each year, plotted
		// against the same secondary (right) Y axis as the tax and income lines.
		double[] withdrawalYs = [.. yearlyTotals.Select( t => t.TotalWithdrawals )];
		ScottPlot.Plottables.Scatter withdrawalLine = plot.Add.Scatter( taxXs, withdrawalYs );
		withdrawalLine.Color = withdrawalColor;
		withdrawalLine.LineWidth = 2;
		withdrawalLine.MarkerSize = 5;
		withdrawalLine.Axes.YAxis = taxAxis;

		// Annotate the timeline with labelled vertical lines for the plan's events.
		AddEventMarkers( plot, yearlyTotals, calculatedPlan.Events );

		ScottPlot.Tick[] ticks = [.. yearlyTotals.Select( ( total, index ) =>
			new ScottPlot.Tick( index, total.Year.ToString() ) )];
		plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual( ticks );
		plot.Axes.Bottom.MajorTickStyle.Length = 0;

		// Rotate the year labels 45 degrees and align them so they sit neatly under each tick.
		plot.Axes.Bottom.TickLabelStyle.Rotation = 45;
		plot.Axes.Bottom.TickLabelStyle.Alignment = ScottPlot.Alignment.MiddleLeft;

		ScottPlot.LegendItem[] legendItems = [
			new ScottPlot.LegendItem { LabelText = "Assets", FillColor = assetColor },
			new ScottPlot.LegendItem { LabelText = "Shortfall", FillColor = shortfallColor },
			new ScottPlot.LegendItem { LabelText = "Total Tax", LineColor = taxColor, LineWidth = 2 },
			new ScottPlot.LegendItem { LabelText = "Total Income", LineColor = benefitColor, LineWidth = 2 },
			new ScottPlot.LegendItem { LabelText = "Total Withdrawals", LineColor = withdrawalColor, LineWidth = 2 }
		];
		plot.Legend.ManualItems.Clear();
		plot.Legend.ManualItems.AddRange( legendItems );
		plot.ShowLegend( ScottPlot.Edge.Bottom );

		Planning.Model.Plans.RetirementIncome retirementIncome = calculatedPlan.RetirementIncome;
		plot.Title( $"Total Assets by Year for Go-Go ${retirementIncome.GoGo}, Slow-Go ${retirementIncome.SlowGo}, No-Go ${retirementIncome.NoGo}" );
		plot.XLabel( "Year" );
		plot.YLabel( "Total Assets" );
		plot.Axes.Margins( bottom: 0 );

		plot.SavePng( filePath, width, height );
	}

	/// <summary>
	/// Adds labelled vertical lines for the plan's timeline events. Lifecycle events (retirement,
	/// CPP start, OAS start, and death) are drawn as dashed lines with boxed labels, combining
	/// events that fall within the same calendar month. Retirement-income phase transitions
	/// (Go-Go, Slow-Go, No-Go) are drawn as dotted lines with green text labels.
	/// </summary>
	private static void AddEventMarkers(
		ScottPlot.Plot plot,
		IReadOnlyList<(int Year, double TotalAssets, double Shortfall, double TotalTax, double BenefitIncome, double TotalWithdrawals)> yearlyTotals,
		IReadOnlyList<PlanEvent> events
	) {
		if( yearlyTotals.Count == 0 ) {
			return;
		}

		int firstYear = yearlyTotals[0].Year;
		int lastYear = yearlyTotals[^1].Year;

		// Anchor the rotated labels near the top of the plot area so they run down alongside
		// each line rather than overlapping the title.
		double labelY = yearlyTotals.Max( t => t.TotalAssets );

		ScottPlot.Color eventColor = ScottPlot.Colors.Green;

		IEnumerable<PlanEvent> visibleEvents = events
			.Where( e => e.Date.Year >= firstYear && e.Date.Year <= lastYear );

		// Lifecycle events: group those within the same calendar month so their labels can be
		// combined into a single, more readable boxed annotation.
		IEnumerable<IGrouping<(int Year, int Month), PlanEvent>> monthGroups = visibleEvents
			.Where( e => e.Kind == PlanEventKind.Lifecycle )
			.GroupBy( e => (e.Date.Year, e.Date.Month) );

		foreach( IGrouping<(int Year, int Month), PlanEvent> group in monthGroups ) {
			// Use the earliest date in the month to position the combined marker.
			DateOnly markerDate = group.Min( e => e.Date );
			string combinedLabel = string.Join( ", ", group.OrderBy( e => e.Date ).Select( e => e.Name ) );
			AddEventMarker( plot, firstYear, labelY, markerDate, combinedLabel, eventColor );
		}

		// Retirement-income phase transitions: dotted lines with green text labels (no box).
		foreach( PlanEvent phaseEvent in visibleEvents.Where( e => e.Kind == PlanEventKind.RetirementPhase ) ) {
			AddPhaseMarker( plot, firstYear, labelY, phaseEvent.Date, phaseEvent.Name, eventColor );
		}
	}

	/// <summary>
	/// Adds a single labelled vertical line for an event date, mapping the date onto the
	/// fractional year position used by the X axis.
	/// </summary>
	private static void AddEventMarker(
		ScottPlot.Plot plot,
		int firstYear,
		double labelY,
		DateOnly eventDate,
		string label,
		ScottPlot.Color color
	) {
		// Bars are positioned by year index (0-based); map the event date to a fractional
		// index using its position within the calendar year.
		double daysInYear = DateTime.IsLeapYear( eventDate.Year ) ? 366.0 : 365.0;
		double yearFraction = ( eventDate.DayOfYear - 1 ) / daysInYear;
		double position = eventDate.Year - firstYear + yearFraction;

		ScottPlot.Plottables.VerticalLine line = plot.Add.VerticalLine( position );
		line.Color = color;
		line.LineWidth = 1;
		line.LinePattern = ScottPlot.LinePattern.Dashed;

		// Draw the label rotated 90 degrees, running vertically down the line it belongs to,
		// as white text inside a filled green box.
		ScottPlot.Plottables.Text text = plot.Add.Text( label, position, labelY );
		text.LabelFontColor = ScottPlot.Colors.White;
		text.LabelRotation = -90;
		text.LabelAlignment = ScottPlot.Alignment.MiddleRight;
		text.LabelBackgroundColor = color;
		text.LabelPadding = 2;
		text.OffsetX = -2;
	}

	/// <summary>
	/// Adds a single labelled dotted vertical line marking a retirement-income phase transition.
	/// The label is drawn as green text with no background box to distinguish it from the
	/// boxed lifecycle event markers.
	/// </summary>
	private static void AddPhaseMarker(
		ScottPlot.Plot plot,
		int firstYear,
		double labelY,
		DateOnly eventDate,
		string label,
		ScottPlot.Color color
	) {
		double daysInYear = DateTime.IsLeapYear( eventDate.Year ) ? 366.0 : 365.0;
		double yearFraction = ( eventDate.DayOfYear - 1 ) / daysInYear;
		double position = eventDate.Year - firstYear + yearFraction;

		ScottPlot.Plottables.VerticalLine line = plot.Add.VerticalLine( position );
		line.Color = color;
		line.LineWidth = 1;
		line.LinePattern = ScottPlot.LinePattern.Dotted;

		// Draw the label rotated 90 degrees as green text with no background box.
		ScottPlot.Plottables.Text text = plot.Add.Text( label, position, labelY );
		text.LabelFontColor = color;
		text.LabelRotation = -90;
		text.LabelAlignment = ScottPlot.Alignment.MiddleRight;
		text.LabelPadding = 2;
		text.OffsetX = -2;
	}

	/// <summary>
	/// Aggregates the household's total asset value, unfunded shortfall, total tax, total
	/// income, and total withdrawals to a single value per calendar year. Total assets use the
	/// final period recorded within each year; shortfall, total tax, total income, and total
	/// withdrawals are summed across all periods in the year.
	/// </summary>
	private static IReadOnlyList<(int Year, double TotalAssets, double Shortfall, double TotalTax, double BenefitIncome, double TotalWithdrawals)> BuildYearlyTotals(
		CalculatedPlan calculatedPlan
	) {
		return [.. calculatedPlan.Periods
			.GroupBy( p => p.PeriodDate.Year )
			.OrderBy( g => g.Key )
			.Select( g => (
				Year: g.Key,
				TotalAssets: (double)g.OrderBy( p => p.PeriodDate ).Last().TotalAssets,
				Shortfall: (double)g.Sum( p => p.UnfundedShortfall ),
				TotalTax: (double)g.Sum( p => p.TotalTax ),
				BenefitIncome: (double)g.Sum( p => p.TotalIncome ),
				TotalWithdrawals: (double)g.Sum( p => p.Withdrawals.Sum( w => w.Amount ) )
			) )];
	}
}
