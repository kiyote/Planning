
# Planning

For the love of all that is good in the universe, this is NOT FINANCIAL ADVICE.

## Options

For a complete list of options, execute `dotnet run` in the `Planning.Cli` directory.

## Quickstart

### Generating plot and CSV

In the `Planning.Cli` directory, execute `dotnet run sample-plan.json`. The corresponding `sample-plan.csv` and `sample-plan.png` files will be generated in the same directory.

### Running value sweeps

In the `Planning.Cli` directory, execute `dotnet run sample-plan.json [OPTION]`, with one of the following options:

* `--sweepretirementincome` finds the largest GoGo, SlowGo, and NoGo retirement incomes that leave the plan solvent. The sweep assumes a ratio of 100%/80%/90% for GoGo/SlowGo/NoGo retirement income. To control these ratios, use `--slowgo-ratio` and `--nogo-ratio`.
* `--sweepages` finds the youngest age that the retirement can begin and the plan will remain solvent.
* `--sweepannualpercents` finds the lowest annual rate of return at the given inflation percentage that the plan will remain solvent.  It will also find the highest inflation percentage at the given annual rate of return that the plan will remain solvent.

## Expenditures

Every plan must include an `Expenditures` array. Use `"Expenditures": []` when there are none.
Each entry describes a household expense:

```json
"Expenditures": [
  { "Amount": 50000, "Indexed": true, "Repeats": true, "IntervalYears": 15, "StartYear": 2020, "Name": "Vehicle" },
  { "Amount": 25000, "Indexed": false, "Repeats": false, "IntervalYears": null, "StartYear": 2045, "Name": "Roof" }
]
```

All six fields are required, including an explicit `null` for `IntervalYears` when no interval
is specified. A repeating expense requires a positive, non-null `IntervalYears`. A non-repeating
expense occurs only in `StartYear`, and its `IntervalYears` value is ignored.

An occurrence adds its full amount to desired after-tax income in January, using the normal
withdrawal, tax, and shortfall calculations. It does not permanently increase monthly lifestyle
income. Multiple expenses in the same month are added together.

Repeating expenses occur in `StartYear`, then every `IntervalYears` after that. Only occurrences
on or after both the plan start date and the household's first modeled retirement date are
charged. Earlier occurrences are assumed paid and are skipped without moving the recurrence
schedule or accumulating missed spending. In particular, starting the plan or retiring after
January 1 skips any occurrence due that January. Expenses continue after either member dies,
through the plan's final period.

For `Indexed: true`, `Amount` is the known cost in `StartYear` dollars:

```text
Occurrence amount = Amount * (1 + AnnualInflationPercent / 100)^(OccurrenceYear - StartYear)
```

This includes inflation from before the plan starts. For example, a Vehicle cost of 50,000 in
2020 with a 15-year interval next occurs in 2035 and uses 15 years of inflation, regardless of
the plan's start year. Indexing remains anchored to 2020 for later occurrences. With
`Indexed: false`, every occurrence uses the stated amount unchanged.

Names must be nonblank, amounts must be nonnegative, and start years must be in 1-9999.
Duplicate names are allowed and remain independent expenses. Zero-amount expenses have no
effect and generate no markers.

The graph shows expenditures as red dotted lines with red name labels, combining expense
names into one marker per year. Existing green event markers are unchanged. CSV layouts are
unchanged: the existing `Retirement Income` column includes expenditures. Solvency and other
sweeps also include expenditures.
