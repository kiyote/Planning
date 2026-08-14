<#
.SYNOPSIS
	Sweeps burndown length against investment return and reports the effect on the estate.

.DESCRIPTION
	Answers whether a burndown strategy helps or hurts, and whether that conclusion holds
	across return assumptions.

	Two things make a naive sweep misleading, and this script handles both:

	1. Every asset in sample-plan.json carries its own "ReturnPercentages". Those override
	   the plan-level "AnnualReturnPercent", so editing the plan-level value alone changes
	   nothing at all. This script rewrites the per-asset values.

	2. Estate values across different return rates are not comparable -- at 8% the estate is
	   orders of magnitude larger than at 3% purely from compounding. Only the comparison
	   BETWEEN burndown strategies AT THE SAME return rate is meaningful, so results are
	   reported as deltas against the no-burndown baseline within each rate.

	Inflation is deliberately held constant so the return axis reads as a real-return sweep.

.PARAMETER Returns
	Asset return percentages to test.

.PARAMETER BurndownYears
	Burndown lengths to test. The first entry is used as the baseline for deltas.

.EXAMPLE
	./sweep-burndown.ps1
	./sweep-burndown.ps1 -Returns 4,6,8 -BurndownYears 0,10,20
#>
[CmdletBinding()]
param(
	[decimal[]] $Returns = @( 3, 4, 5, 6, 7, 8 ),
	[int[]] $BurndownYears = @( 0, 15, 25 ),
	[string] $PlanPath = 'src/Planning/Cli/Planning.Cli/sample-plan.json',
	[string] $OutputDirectory = '.verify/sweep'
)

$ErrorActionPreference = 'Stop'

if( -not ( Test-Path $PlanPath ) ) {
	throw "Plan not found: $PlanPath"
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

Write-Host 'Building CLI...' -ForegroundColor Cyan
dotnet build src/Planning/Cli/Planning.Cli --nologo -v q | Out-Null
if( $LASTEXITCODE -ne 0 ) {
	throw 'Build failed.'
}

$source = Get-Content $PlanPath -Raw

# Pulled from the report footer rather than recomputed here, so the sweep always agrees
# with the application's own numbers.
function Get-SummaryValue {
	param( [string[]] $Lines, [string] $Label )

	$match = $Lines | Select-String -Pattern ( '^' + [regex]::Escape( $Label ) + ',' ) | Select-Object -First 1
	if( -not $match ) {
		return $null
	}

	return [decimal] ( $match.Line -split ',' )[1]
}

$results = [System.Collections.Generic.List[object]]::new()

foreach( $years in $BurndownYears ) {
	foreach( $rate in $Returns ) {
		$label = "bd$years-r$rate"
		$planFile = Join-Path $OutputDirectory "$label.json"

		# Per-asset returns are the operative lever; the plan-level value is overridden.
		$plan = $source -replace '"Value":\s*[\d.]+', ( '"Value": ' + $rate )
		$plan = $plan -replace '"BurndownYears":\s*\d+', ( '"BurndownYears": ' + $years )
		Set-Content -Path $planFile -Value $plan

		Write-Host "  running burndown=$years return=$rate%" -ForegroundColor DarkGray
		dotnet run --project src/Planning/Cli/Planning.Cli --no-build -- $planFile | Out-Null
		if( $LASTEXITCODE -ne 0 ) {
			throw "Run failed for $label."
		}

		$csv = Get-Content ( Join-Path $OutputDirectory "$label.csv" )

		$results.Add( [pscustomobject]@{
			BurndownYears = $years
			ReturnPercent = $rate
			TotalTax      = Get-SummaryValue -Lines $csv -Label 'Total Tax Including Terminal'
			NetEstate     = Get-SummaryValue -Lines $csv -Label 'Net Estate'
			NetEstate2026 = Get-SummaryValue -Lines $csv -Label 'Net Estate (2026 Dollars)'
		} )
	}
}

$baselineYears = $BurndownYears[0]

foreach( $row in $results ) {
	$baseline = $results | Where-Object {
		$_.ReturnPercent -eq $row.ReturnPercent -and $_.BurndownYears -eq $baselineYears
	} | Select-Object -First 1

	# Deltas are only ever taken within a single return rate; comparing estates across
	# different rates would measure compounding rather than strategy.
	$row | Add-Member -NotePropertyName EstateDelta -NotePropertyValue ( $row.NetEstate2026 - $baseline.NetEstate2026 )
	$row | Add-Member -NotePropertyName TaxDelta -NotePropertyValue ( $row.TotalTax - $baseline.TotalTax )
}

$csvPath = Join-Path $OutputDirectory 'sweep-results.csv'
$results | Export-Csv -Path $csvPath -NoTypeInformation
Write-Host "`nResults written to $csvPath" -ForegroundColor Green

$results | Format-Table `
	BurndownYears,
	ReturnPercent,
	@{ Label = 'TotalTax'; Expression = { '{0:N0}' -f $_.TotalTax }; Align = 'right' },
	@{ Label = 'NetEstate2026'; Expression = { '{0:N0}' -f $_.NetEstate2026 }; Align = 'right' },
	@{ Label = 'EstateDelta'; Expression = { '{0:N0}' -f $_.EstateDelta }; Align = 'right' } `
	-AutoSize
