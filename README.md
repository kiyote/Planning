
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
