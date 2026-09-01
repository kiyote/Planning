<#
.SYNOPSIS
	Sweeps member target ages against a set of burndown lengths to find the age pairs
	where enabling a burndown improves the net estate.

.DESCRIPTION
	For every combination of member target ages (each member swept one year at a time
	from its floor age up to its baseline age from the source plan) the script runs the
	plan once per burndown length, plus a baseline run with the burndown disabled.

	Each run writes a plan JSON into the output folder, invokes Planning.Cli against it
	with --no-graph, and parses the resulting CSV for:
		- Total Unfunded Shortfall  (must be zero for the run to be viable)
		- Plan Exhausted            (must never be True for the run to be viable)
		- Net Estate (start dollars)

	A burndown "makes sense" for an age pair when the run is viable and its net estate
	beats the no-burndown baseline for that same age pair. The per-run results and the
	baseline-relative comparison are written as two summary CSVs.

.PARAMETER PlanPath
	The source plan JSON whose members and burndown are varied. Every other field is
	left untouched.

.PARAMETER OutputPath
	Folder receiving the generated plans, per-run CSV/PNG output, and the summaries.

.PARAMETER BurndownYears
	The burndown lengths to test. A baseline of 0 (no burndown) is always run as well.

.PARAMETER FloorAge
	The lowest target age to sweep down to for every member.

.PARAMETER KeepRunArtifacts
	Keep the per-run plan JSON and CSV. By default they are deleted after parsing so a
	large sweep does not fill the output folder. Graphs are never generated.

.EXAMPLE
	./scripts/Invoke-BurndownSweep.ps1

.EXAMPLE
	./scripts/Invoke-BurndownSweep.ps1 -BurndownYears 10,15,20 -FloorAge 75 -KeepRunArtifacts
#>
[CmdletBinding()]
param(
	[string] $PlanPath = "$PSScriptRoot/../src/Planning/Cli/Planning.Cli/sample-plan.json",
	[string] $OutputPath = "$PSScriptRoot/../artifacts/burndown-sweep",
	[int[]] $BurndownYears = @( 10, 15, 20 ),
	[int] $FloorAge = 75,
	[switch] $KeepRunArtifacts
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$cliProject = Join-Path $PSScriptRoot '../src/Planning/Cli/Planning.Cli/Planning.Cli.csproj' | Resolve-Path
$PlanPath = Resolve-Path $PlanPath

# Parses the CLI's CSV output. The file is a period table followed by blank-line separated
# summary sections, so the period rows are read as CSV and the summaries as key/value lines.
function Read-RunResult {
	param(
		[Parameter( Mandatory )] [string] $CsvPath
	)

	$lines = Get-Content -LiteralPath $CsvPath

	$headerIndex = ( 0..( $lines.Count - 1 ) | Where-Object { $lines[$_].StartsWith( 'Period,' ) } | Select-Object -First 1 )
	if( $null -eq $headerIndex ) {
		throw "Could not find the period table header in '$CsvPath'."
	}

	$endIndex = $headerIndex
	while( $endIndex + 1 -lt $lines.Count -and -not [string]::IsNullOrWhiteSpace( $lines[$endIndex + 1] ) ) {
		$endIndex++
	}

	$periods = $lines[$headerIndex..$endIndex] | ConvertFrom-Csv
	$exhausted = [bool]( $periods | Where-Object { $_.'Plan Exhausted' -eq 'True' } )

	# The summary sections are "Label,Value" rows; pull out the two values that score a run.
	$netEstateStartDollars = $null
	$totalUnfundedShortfall = $null
	foreach( $line in $lines ) {
		if( $line -match '^Net Estate \(\d+ Dollars\),(.+)$' ) {
			$netEstateStartDollars = [decimal] $Matches[1]
		} elseif( $line -match '^Total Unfunded Shortfall,(.+)$' ) {
			$totalUnfundedShortfall = [decimal] $Matches[1]
		}
	}

	if( $null -eq $netEstateStartDollars ) {
		throw "Could not find the net estate summary in '$CsvPath'."
	}
	if( $null -eq $totalUnfundedShortfall ) {
		throw "Could not find the unfunded shortfall summary in '$CsvPath'."
	}

	[PSCustomObject]@{
		NetEstateStartDollars  = $netEstateStartDollars
		TotalUnfundedShortfall = $totalUnfundedShortfall
		PlanExhausted          = $exhausted
	}
}

# Writes a variant of the source plan with the given target ages and burndown length,
# runs the CLI against it, and returns the parsed result.
function Invoke-PlanRun {
	param(
		[Parameter( Mandatory )] [string] $Name,
		[Parameter( Mandatory )] [hashtable] $TargetAges,
		[Parameter( Mandatory )] [int] $Burndown
	)

	# Re-parse per run so each variant mutates a private copy of the plan.
	$plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
	foreach( $member in $plan.Members ) {
		$member.TargetAgeInYears = $TargetAges[$member.Name]
	}
	$plan.Burndown.BurndownYears = $Burndown

	$runPlanPath = Join-Path $OutputPath "$Name.json"
	$plan | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $runPlanPath -Encoding utf8

	# The graph is wasted work for a sweep; only the CSV is scored.
	$output = & dotnet run --project $cliProject --configuration Release --no-build -- $runPlanPath --no-graph 2>&1
	if( $LASTEXITCODE -ne 0 ) {
		throw "Planning.Cli failed for '$Name':`n$( $output -join [Environment]::NewLine )"
	}

	$result = Read-RunResult -CsvPath ( [IO.Path]::ChangeExtension( $runPlanPath, '.csv' ) )

	if( -not $KeepRunArtifacts ) {
		foreach( $extension in '.json', '.csv' ) {
			Remove-Item -LiteralPath ( [IO.Path]::ChangeExtension( $runPlanPath, $extension ) ) -ErrorAction SilentlyContinue
		}
	}

	$result
}

New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
$OutputPath = Resolve-Path $OutputPath

Write-Host "Building Planning.Cli..."
& dotnet build $cliProject --configuration Release | Out-Null
if( $LASTEXITCODE -ne 0 ) {
	throw 'Failed to build Planning.Cli.'
}

$basePlan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
$memberNames = @( $basePlan.Members | ForEach-Object { $_.Name } )
if( $memberNames.Count -ne 2 ) {
	throw "This sweep expects exactly two members but the plan has $( $memberNames.Count )."
}

# Each member is swept from the floor age up to its own baseline, so nobody is ever
# extended past the lifespan the source plan already assumes.
$ageRanges = [ordered]@{}
foreach( $member in $basePlan.Members ) {
	if( $member.TargetAgeInYears -lt $FloorAge ) {
		throw "Member '$( $member.Name )' has a target age of $( $member.TargetAgeInYears ), below the floor of $FloorAge."
	}
	$ageRanges[$member.Name] = $FloorAge..$member.TargetAgeInYears
}

$firstName = $memberNames[0]
$secondName = $memberNames[1]
$allBurndowns = @( 0 ) + $BurndownYears
$totalRuns = $ageRanges[$firstName].Count * $ageRanges[$secondName].Count * $allBurndowns.Count

Write-Host "Sweeping $totalRuns runs into $OutputPath"

$runs = [Collections.Generic.List[object]]::new()
$completed = 0

foreach( $firstAge in $ageRanges[$firstName] ) {
	foreach( $secondAge in $ageRanges[$secondName] ) {
		$targetAges = @{ $firstName = $firstAge; $secondName = $secondAge }

		foreach( $burndown in $allBurndowns ) {
			$name = "bd$burndown-$firstName$firstAge-$secondName$secondAge"

			$completed++
			Write-Progress `
				-Activity 'Sweeping burndowns' `
				-Status $name `
				-PercentComplete ( $completed / $totalRuns * 100 )

			$result = Invoke-PlanRun -Name $name -TargetAges $targetAges -Burndown $burndown

			$runs.Add( [PSCustomObject]@{
				BurndownYears          = $burndown
				"$firstName Age"       = $firstAge
				"$secondName Age"      = $secondAge
				NetEstateStartDollars  = $result.NetEstateStartDollars
				TotalUnfundedShortfall = $result.TotalUnfundedShortfall
				PlanExhausted          = $result.PlanExhausted
				Viable                 = ( -not $result.PlanExhausted ) -and ( $result.TotalUnfundedShortfall -eq 0 )
			} )
		}
	}
}

Write-Progress -Activity 'Sweeping burndowns' -Completed

$runsPath = Join-Path $OutputPath 'sweep-runs.csv'
$runs | Export-Csv -LiteralPath $runsPath -NoTypeInformation

# Score each burndown against the no-burndown run for the same age pair, since "worth doing"
# means the burndown beat leaving it off for that specific pair of lifespans.
$baselines = @{}
foreach( $run in $runs | Where-Object { $_.BurndownYears -eq 0 } ) {
	$baselines["$( $run."$firstName Age" )-$( $run."$secondName Age" )"] = $run
}

$comparisons = foreach( $run in $runs | Where-Object { $_.BurndownYears -ne 0 } ) {
	$baseline = $baselines["$( $run."$firstName Age" )-$( $run."$secondName Age" )"]
	$improvement = $run.NetEstateStartDollars - $baseline.NetEstateStartDollars

	[PSCustomObject]@{
		BurndownYears             = $run.BurndownYears
		"$firstName Age"          = $run."$firstName Age"
		"$secondName Age"         = $run."$secondName Age"
		BaselineNetEstate         = $baseline.NetEstateStartDollars
		BurndownNetEstate         = $run.NetEstateStartDollars
		NetEstateImprovement      = $improvement
		Viable                    = $run.Viable
		BaselineViable            = $baseline.Viable
		BurndownWorthIt           = $run.Viable -and $baseline.Viable -and ( $improvement -gt 0 )
	}
}

$comparisonPath = Join-Path $OutputPath 'sweep-comparison.csv'
$comparisons | Sort-Object NetEstateImprovement -Descending | Export-Csv -LiteralPath $comparisonPath -NoTypeInformation

$winners = @( $comparisons | Where-Object { $_.BurndownWorthIt } )

Write-Host ""
Write-Host "Wrote per-run results to $runsPath"
Write-Host "Wrote baseline comparison to $comparisonPath"
Write-Host "$( $winners.Count ) of $( @( $comparisons ).Count ) burndown runs improved the net estate."

if( $winners.Count -gt 0 ) {
	Write-Host ""
	Write-Host "Top 10 age pairs where a burndown helps:"
	$winners |
		Sort-Object NetEstateImprovement -Descending |
		Select-Object -First 10 |
		Format-Table -AutoSize
}
