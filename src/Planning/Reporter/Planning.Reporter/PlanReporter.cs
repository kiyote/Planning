using System.Globalization;

using Planning.Model;
using Planning.Model.CalculatedPlans;
using Planning.Model.CompiledPlans;
using Planning.Model.Identifiers;
using Planning.Model.Plans;

namespace Planning.Reporter;

public class PlanReporter {

	private const string PeriodDateFormat = "MMM yyyy";

	public void WriteToCsv(
		TextWriter writer,
		CompiledPlan compiledPlan,
		CalculatedPlan calculatedPlan
	) {
		IReadOnlyDictionary<AssetId, CompiledAsset> assetsById = compiledPlan.Assets
			.ToDictionary( a => a.AssetId );
		IReadOnlyDictionary<MemberId, CompiledMember> membersById = compiledPlan.Members
			.ToDictionary( m => m.MemberId );

		string AssetLabel( AssetId assetId, string suffix ) {
			CompiledAsset asset = assetsById[assetId];
			CompiledMember member = membersById[asset.MemberId];
			return $"{asset.Name} ({member.Name}){suffix}";
		}

		string IncomeLabel( MemberId memberId, string name ) {
			CompiledMember member = membersById[memberId];
			return $"{name} ({member.Name})";
		}

		CalculatedPeriod? firstPeriod = calculatedPlan.Periods.FirstOrDefault();

		List<string> header = [ "Period" ];

		if( firstPeriod is not null ) {
			header.AddRange( firstPeriod.StartingAssets.Select( a => AssetLabel( a.AssetId, " [Start]" ) ) );
			header.AddRange( firstPeriod.TaxableIncome.Select( i => IncomeLabel( i.MemberId, i.Name ) ) );
			header.AddRange( firstPeriod.NonTaxableIncome.Select( i => IncomeLabel( i.MemberId, i.Name ) ) );
			header.Add( "Total Taxable Income" );
			header.Add( "Total Non-Taxable Income" );
			header.Add( "Total Income" );
			header.Add( "Retirement Income" );
			header.Add( "Shortfall" );
			header.Add( "Actual Retirement Income" );
			header.Add( "Requested Withdrawal" );
			header.Add( "Actual Withdrawal" );
			header.Add( "Unfunded Shortfall" );
			header.Add( "Plan Exhausted" );
			header.AddRange( firstPeriod.Withdrawals.Select( w => AssetLabel( w.AssetId, " Withdrawl" ) ) );
			header.AddRange( firstPeriod.Contribution.Select( c => AssetLabel( c.AssetId, " Contribution" ) ) );
			header.AddRange( firstPeriod.EndingAssets.Select( a => AssetLabel( a.AssetId, " [End]" ) ) );
			header.AddRange( firstPeriod.EndingAssets.Select( a => AssetLabel( a.AssetId, " Backlog" ) ) );
			header.Add( "Total Assets" );
			header.Add( "Total Tax" );
			header.Add( "Tax Funding Withdrawal" );
			header.Add( "Unfunded Tax" );
			header.Add( "Burndown Withdrawal" );
			header.Add( "Burndown Tax" );
			header.Add( "Burndown Transfer" );
			header.Add( "RRIF Minimum Withdrawal" );
			header.Add( "RRIF Minimum Transfer" );
		}

		WriteRow( writer, header );

		foreach( CalculatedPeriod period in calculatedPlan.Periods ) {
			List<string> row = [ period.PeriodDate.ToString( PeriodDateFormat, CultureInfo.InvariantCulture ) ];

			row.AddRange( period.StartingAssets.Select( a => a.Amount.FormatRounded() ) );
			row.AddRange( period.TaxableIncome.Select( i => i.Amount.FormatRounded() ) );
			row.AddRange( period.NonTaxableIncome.Select( i => i.Amount.FormatRounded() ) );
			row.Add( period.TotalTaxableIncome.FormatRounded() );
			row.Add( period.TotalNonTaxableIncome.FormatRounded() );
			row.Add( period.TotalIncome.FormatRounded() );
			row.Add( period.DesiredRetirementIncome.FormatRounded() );
			row.Add( period.RetirementIncomeShortfall.FormatRounded() );
			row.Add( period.ActualRetirementIncome.FormatRounded() );
			row.Add( period.RequestedWithdrawal.FormatRounded() );
			row.Add( period.ActualWithdrawal.FormatRounded() );
			row.Add( period.UnfundedShortfall.FormatRounded() );
			row.Add( period.PlanExhausted.ToString() );
			row.AddRange( period.Withdrawals.Select( w => w.Amount.FormatRounded() ) );
			row.AddRange( period.Contribution.Select( c => c.Amount.FormatRounded() ) );
			row.AddRange( period.EndingAssets.Select( a => a.Amount.FormatRounded() ) );
			row.AddRange( period.EndingAssets.Select( a => a.ContributionBacklog.FormatRounded() ) );
			row.Add( period.TotalAssets.FormatRounded() );
			row.Add( period.TotalTax.FormatRounded() );
			row.Add( period.TaxFundingWithdrawal.FormatRounded() );
			row.Add( period.UnfundedTax.FormatRounded() );
			row.Add( period.BurndownWithdrawal.FormatRounded() );
			row.Add( period.BurndownTax.FormatRounded() );
			row.Add( period.BurndownTransfer.FormatRounded() );
			row.Add( period.RrifMinimumWithdrawal.FormatRounded() );
			row.Add( period.RrifMinimumTransfer.FormatRounded() );

			WriteRow( writer, row );
		}

		WriteInsufficientFundsSummary( writer, calculatedPlan.InsufficientFunds );
		WriteTaxSummary( writer, calculatedPlan.TaxSummary );
		WriteEstateSummary( writer, calculatedPlan.EstateSummary );
	}

	private static void WriteEstateSummary(
		TextWriter writer,
		EstateSummary summary
	) {
		writer.WriteLine();
		WriteRow( writer, [ "Estate Summary" ] );
		WriteRow( writer, [ "Gross Estate", summary.GrossEstate.FormatRounded() ] );
		WriteRow( writer, [ "Terminal Tax", summary.TerminalTax.FormatRounded() ] );
		WriteRow( writer, [ "Net Estate", summary.NetEstate.FormatRounded() ] );
		WriteRow( writer, [
			$"Net Estate ({summary.PlanStartYear} Dollars)",
			summary.NetEstateInPlanStartDollars.FormatRounded()
		] );
	}

	private static void WriteTaxSummary(
		TextWriter writer,
		TaxSummary summary
	) {
		writer.WriteLine();
		WriteRow( writer, [ "Tax Summary" ] );
		WriteRow( writer, [ "Total Federal Tax", summary.TotalFederalTax.FormatRounded() ] );
		WriteRow( writer, [ "Total Provincial Tax", summary.TotalProvincialTax.FormatRounded() ] );
		WriteRow( writer, [ "Total Tax", summary.TotalTax.FormatRounded() ] );
		WriteRow( writer, [ "Terminal Federal Tax", summary.TerminalFederalTax.FormatRounded() ] );
		WriteRow( writer, [ "Terminal Provincial Tax", summary.TerminalProvincialTax.FormatRounded() ] );
		WriteRow( writer, [ "Terminal Tax", summary.TerminalTax.FormatRounded() ] );
		WriteRow( writer, [ "Total Tax Including Terminal", summary.TotalTaxIncludingTerminal.FormatRounded() ] );
	}

	private static void WriteInsufficientFundsSummary(
		TextWriter writer,
		InsufficientFundsSummary summary
	) {
		writer.WriteLine();
		WriteRow( writer, [ "Insufficient Funds Summary" ] );
		WriteRow( writer, [ "Has Shortfall", summary.HasShortfall.ToString() ] );
		WriteRow( writer, [
			"First Shortfall Date",
			summary.FirstShortfallDate?.ToString( PeriodDateFormat, CultureInfo.InvariantCulture ) ?? string.Empty
		] );
		WriteRow( writer, [
			"First Shortfall Period",
			summary.FirstShortfallPeriod?.Value.ToString( CultureInfo.InvariantCulture ) ?? string.Empty
		] );
		WriteRow( writer, [
			"Shortfall Period Count",
			summary.ShortfallPeriodCount.ToString( CultureInfo.InvariantCulture )
		] );
		WriteRow( writer, [ "Total Unfunded Shortfall", summary.TotalUnfundedShortfall.FormatRounded() ] );
	}

	public void WriteToCsv(
		TextWriter writer,
		CompiledPlan compiledPlan
	) {
		IReadOnlyDictionary<AssetId, CompiledAsset> assetsById = compiledPlan.Assets
			.ToDictionary( a => a.AssetId );
		IReadOnlyDictionary<MemberId, CompiledMember> membersById = compiledPlan.Members
			.ToDictionary( m => m.MemberId );

		CompiledPeriod? firstPeriod = compiledPlan.Periods.FirstOrDefault();

		List<string> header = [ "Period" ];

		if( firstPeriod is not null ) {
			foreach( CompiledIncome income in compiledPlan.Income[firstPeriod] ) {
				CompiledMember member = membersById[income.MemberId];
				header.Add( $"{income.Name} ({member.Name})" );
			}

			foreach( CompiledContribution contribution in compiledPlan.Contribution[firstPeriod] ) {
				CompiledMember member = membersById[contribution.MemberId];
				header.Add( $"{member.Name} Contribution" );
			}

			header.Add( "Retirement Income" );
		}

		WriteRow( writer, header );

		foreach( CompiledPeriod period in compiledPlan.Periods ) {
			List<string> row = [ period.PeriodDate.ToString( PeriodDateFormat, CultureInfo.InvariantCulture ) ];

			row.AddRange( compiledPlan.Income[period].Select( i => i.Amount.FormatRounded() ) );
			row.AddRange( compiledPlan.Contribution[period].Select( c => c.Amount.FormatRounded() ) );
			row.Add( compiledPlan.RetirementIncome[period].FormatRounded() );

			WriteRow( writer, row );
		}
	}

	[Obsolete( "Use WriteToCsv instead. This overload will be removed in a future release." )]
	public void WriteToCSV(
		TextWriter writer,
		CompiledPlan compiledPlan,
		CalculatedPlan calculatedPlan
	) => WriteToCsv( writer, compiledPlan, calculatedPlan );

	[Obsolete( "Use WriteToCsv instead. This overload will be removed in a future release." )]
	public void WriteToCSV(
		TextWriter writer,
		Plan plan,
		CompiledPlan compiledPlan
	) => WriteToCsv( writer, compiledPlan );

	private static void WriteRow(
		TextWriter writer,
		IReadOnlyList<string> fields
	) {
		for( int i = 0; i < fields.Count; i++ ) {
			if( i > 0 ) {
				writer.Write( ',' );
			}

			writer.Write( EscapeField( fields[i] ) );
		}

		writer.WriteLine();
	}

	private static string EscapeField(
		string value
	) {
		bool mustQuote = value.Contains( ',' )
			|| value.Contains( '"' )
			|| value.Contains( '\n' )
			|| value.Contains( '\r' );

		if( !mustQuote ) {
			return value;
		}

		string escaped = value.Replace( "\"", "\"\"" );
		return $"\"{escaped}\"";
	}
}
