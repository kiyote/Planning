using Planning.Model.CalculatedPlans;
using Planning.Model.Plans;

namespace Planning.Graphing;

public class PlanGrapher {

	private const int DefaultWidth = 1200;
	private const int DefaultHeight = 700;

	/// <summary>
	/// A year's aggregated plot values, including the ending asset value broken down by
	/// tax status so the makeup of the total can be shown as a stacked bar.
	/// </summary>
	private sealed record YearlyTotal(
		int Year,
		IReadOnlyDictionary<AssetTaxStatus, double> AssetsByTaxStatus,
		double TotalAssets,
		double Shortfall,
		double TotalTax,
		double BenefitIncome,
		double TotalWithdrawals
	);

	// Bottom-to-top ordering of the asset stack, with a distinct colour per tax status.
	private static readonly (AssetTaxStatus Status, string Label, ScottPlot.Color Color)[] AssetStackOrder = [
		( AssetTaxStatus.Taxable, "Taxable Assets", ScottPlot.Colors.Blue ),
		( AssetTaxStatus.CapitalGains, "Capital Gains Assets", ScottPlot.Colors.Green ),
		( AssetTaxStatus.TaxExempt, "Tax Exempt Assets", ScottPlot.Colors.Orange )
	];

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

		IReadOnlyList<YearlyTotal> yearlyTotals = BuildYearlyTotals( calculatedPlan );

		// Registered before the plot is created so that every text element picks up the
		// embedded font rather than whatever the host operating system happens to provide.
		ChartFont.EnsureRegistered();

		ScottPlot.Plot plot = new ScottPlot.Plot();
		plot.Axes.Title.Label.FontName = ChartFont.FamilyName;
		plot.Axes.Bottom.Label.FontName = ChartFont.FamilyName;
		plot.Axes.Left.Label.FontName = ChartFont.FamilyName;
		plot.Axes.Right.Label.FontName = ChartFont.FamilyName;
		plot.Axes.Bottom.TickLabelStyle.FontName = ChartFont.FamilyName;
		plot.Axes.Left.TickLabelStyle.FontName = ChartFont.FamilyName;
		plot.Axes.Right.TickLabelStyle.FontName = ChartFont.FamilyName;
		plot.Legend.FontName = ChartFont.FamilyName;

		ScottPlot.Color shortfallColor = ScottPlot.Colors.Red;
		ScottPlot.Color taxColor = ScottPlot.Colors.Black;
		ScottPlot.Color benefitColor = ScottPlot.Colors.Gray;
		ScottPlot.Color withdrawalColor = ScottPlot.Colors.LightBlue;

		List<ScottPlot.Bar> bars = [];
		for( int index = 0; index < yearlyTotals.Count; index++ ) {
			YearlyTotal yearlyTotal = yearlyTotals[index];
			double stackBase = 0;

			// Each tax status contributes a coloured segment, stacked bottom-to-top so the
			// composition of the year's total assets is visible at a glance.
			foreach( (AssetTaxStatus status, string _, ScottPlot.Color color) in AssetStackOrder ) {
				double amount = yearlyTotal.AssetsByTaxStatus.GetValueOrDefault( status );

				if( amount == 0 ) {
					continue;
				}

				bars.Add( new ScottPlot.Bar {
					Position = index,
					ValueBase = stackBase,
					Value = stackBase + amount,
					FillColor = color
				} );

				stackBase += amount;
			}

			// Red shortfall segment stacks on top of the whole asset stack.
			if( yearlyTotal.Shortfall != 0 ) {
				bars.Add( new ScottPlot.Bar {
					Position = index,
					ValueBase = stackBase,
					Value = stackBase + yearlyTotal.Shortfall,
					FillColor = shortfallColor
				} );
			}
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
		taxAxis.Label.FontName = ChartFont.FamilyName;
		taxAxis.TickLabelStyle.FontName = ChartFont.FamilyName;

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

		// Only show asset segments that actually occur in this plan so the legend stays honest.
		List<ScottPlot.LegendItem> legendItems = [
			.. AssetStackOrder
				.Where( entry => yearlyTotals.Any( t => t.AssetsByTaxStatus.GetValueOrDefault( entry.Status ) != 0 ) )
				.Select( entry => new ScottPlot.LegendItem { LabelText = entry.Label, FillColor = entry.Color } )
		];

		legendItems.AddRange( [
			new ScottPlot.LegendItem { LabelText = "Shortfall", FillColor = shortfallColor },
			new ScottPlot.LegendItem { LabelText = "Total Tax", LineColor = taxColor, LineWidth = 2 },
			new ScottPlot.LegendItem { LabelText = "Total Income", LineColor = benefitColor, LineWidth = 2 },
			new ScottPlot.LegendItem { LabelText = "Total Withdrawals", LineColor = withdrawalColor, LineWidth = 2 }
		] );
		plot.Legend.ManualItems.Clear();
		plot.Legend.ManualItems.AddRange( legendItems );
		plot.ShowLegend( ScottPlot.Edge.Bottom );

		RetirementIncome retirementIncome = calculatedPlan.RetirementIncome;
		plot.Title( $"Total Assets by Year for Go-Go ${retirementIncome.GoGo}, Slow-Go ${retirementIncome.SlowGo}, No-Go ${retirementIncome.NoGo}" );
		plot.XLabel( "Year" );
		plot.YLabel( "Total Assets" );
		plot.Axes.Margins( bottom: 0 );

		// The title and axis labels are replaced by the calls above, so the font is reapplied
		// here rather than relying on the styles set when the plot was created.
		plot.Axes.Title.Label.FontName = ChartFont.FamilyName;
		plot.Axes.Bottom.Label.FontName = ChartFont.FamilyName;
		plot.Axes.Left.Label.FontName = ChartFont.FamilyName;

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
		IReadOnlyList<YearlyTotal> yearlyTotals,
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
		double position = EventPosition( firstYear, eventDate );

		ScottPlot.Plottables.VerticalLine line = plot.Add.VerticalLine( position );
		line.Color = color;
		line.LineWidth = 1;
		line.LinePattern = ScottPlot.LinePattern.Dashed;

		// Draw the label rotated 90 degrees, running vertically down the line it belongs to,
		// as white text inside a filled green box.
		ScottPlot.Plottables.Text text = plot.Add.Text( label, position, labelY );
		text.LabelFontName = ChartFont.FamilyName;
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
		double position = EventPosition( firstYear, eventDate );

		ScottPlot.Plottables.VerticalLine line = plot.Add.VerticalLine( position );
		line.Color = color;
		line.LineWidth = 1;
		line.LinePattern = ScottPlot.LinePattern.Dotted;

		// Draw the label rotated 90 degrees as green text with no background box.
		ScottPlot.Plottables.Text text = plot.Add.Text( label, position, labelY );
		text.LabelFontName = ChartFont.FamilyName;
		text.LabelFontColor = color;
		text.LabelRotation = -90;
		text.LabelAlignment = ScottPlot.Alignment.MiddleRight;
		text.LabelPadding = 2;
		text.OffsetX = -2;
	}

	/// <summary>
	/// Maps an event date onto the X axis position used by the yearly bars.
	///
	/// Each bar summarises a whole calendar year and is drawn at the integer index of that
	/// year, so the bar spans roughly index-0.5 to index+0.5. An event is therefore aligned to
	/// the centre of the bar for the year it falls in, rather than to the fraction of the year
	/// elapsed. Using the elapsed fraction placed a late-year event such as a December death
	/// almost a full bar to the right of the bar that actually reports its consequences, which
	/// made the marker appear to lag the year it belongs to.
	/// </summary>
	private static double EventPosition(
		int firstYear,
		DateOnly eventDate
	) {
		return eventDate.Year - firstYear;
	}

	/// <summary>
	/// Aggregates the household's total asset value, unfunded shortfall, total tax, total
	/// income, and total withdrawals to a single value per calendar year. Total assets use the
	/// final period recorded within each year; shortfall, total tax, total income, and total
	/// withdrawals are summed across all periods in the year.
	/// </summary>
	private static IReadOnlyList<YearlyTotal> BuildYearlyTotals(
		CalculatedPlan calculatedPlan
	) {
		return [.. calculatedPlan.Periods
			.GroupBy( p => p.PeriodDate.Year )
			.OrderBy( g => g.Key )
			.Select( g => {
				CalculatedPeriod lastPeriod = g.OrderBy( p => p.PeriodDate ).Last();

				return new YearlyTotal(
					Year: g.Key,
					AssetsByTaxStatus: lastPeriod.EndingAssets
						.GroupBy( a => a.TaxStatus )
						.ToDictionary( ag => ag.Key, ag => (double)ag.Sum( a => a.Amount ) ),
					TotalAssets: (double)lastPeriod.TotalAssets,
					Shortfall: (double)g.Sum( p => p.UnfundedShortfall ),
					TotalTax: (double)g.Sum( p => p.TotalTax ),
					BenefitIncome: (double)g.Sum( p => p.TotalIncome ),
					TotalWithdrawals: (double)g.Sum( p => p.Withdrawals.Sum( w => w.Amount ) )
				);
			} )];
	}
}
