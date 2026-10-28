
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

## CPP retirement and survivor benefits

`CPPPercent` remains each member's estimated age-65 entitlement as a percentage of
`CPPMaximum`. Normal retirement CPP is unchanged: the compiler applies the chosen start-age
adjustment and indexes payments using the plan's inflation assumption.

Use the published **age-65 retirement maximum** for `CPPMaximum` and the published
**age-65 combined retirement/survivor maximum** for `CPPCombinedSurvivorMaximum`, both for
the same period and expressed in plan-start dollars. The latter is not a ceiling on every
survivor's actual payment. For example, the January 2026 amounts are 1,507.65 and 1,531.56.

No additional personal benefit estimates are required. For a ballpark enhancement allowance,
the model assumes each member has earned the same `CPPPercent` of the base and both additional
components. At full entitlement, the combined pension adds 60% of the deceased's enhanced
pension, reduced by 40%, above the retirement maximum. Consequently:

```text
Estimated enhanced maximum = (CPPCombinedSurvivorMaximum - CPPMaximum) / 0.36
Estimated base maximum = CPPMaximum - Estimated enhanced maximum
```

With the sample's maxima this estimates about 66.42 of enhanced CPP and 1,441.23 of base CPP
per month. Published amounts are rounded, so this is an approximation, not a reconstruction of
an individual's contribution history. It does not project extra enhancement accrual from future
work; adjust the overall entitlement estimate if needed. The combined maximum must be between
100% and 136% of the retirement maximum. A zero `CPPMaximum` disables CPP.

For a survivor aged 65 or older:

* The deceased's unadjusted entitlement is used, not their early/late-start-adjusted payment.
* Until the survivor starts their own CPP, the survivor benefit is 60% of that entitlement.
* Once both pensions are payable, each component's survivor benefit is reduced by the lesser
  of 40% of itself and 40% of the survivor's own unadjusted component.
* Only the base component is capped against the survivor's **unadjusted** base entitlement.
  The enhanced components are not capped, and deferral increases to the survivor's own CPP
  remain intact.
* For CPP started before 65, the applicable special retirement-pension adjustment is included
  in the `CPP` income column, rather than the `CPP Survivor` column.

Plans requiring a CPP survivor pension before age 65 are rejected explicitly; under-65 and
historical grandfather rules are not modelled. Survivor payments begin in the month after
death, stop when the survivor dies, and remain taxable. The same calculation feeds reports,
graphs and value sweeps, including `--sweepcpp`, which maximizes estate among solvent plans.

Sources: [Service Canada payment amounts](https://www.canada.ca/en/services/benefits/publicpensions/cpp/payment-amounts.html),
[CPP Act section 58](https://laws-lois.justice.gc.ca/eng/acts/C-8/section-58.html), and
[section 46(5)](https://laws-lois.justice.gc.ca/eng/acts/C-8/section-46.html).

## Pension splitting

The sample plan enables `"AllowPensionSplitting": true` in `TaxPolicy`. Set it to `false`
to compare against a no-splitting projection.

When enabled, the calculator proposes reallocating eligible registered-account withdrawal
income from the higher-income spouse to the lower-income spouse for tax purposes. It transfers
at most 50% of the higher earner's eligible withdrawals, and no more than is needed to equalize
their taxable incomes. The split is applied only if it lowers combined federal and provincial
tax, including modeled credits, Ontario surtax and Health Premium, and OAS clawback. Equal-cost
or higher-cost splits leave the original allocation unchanged. This is a guarded equalization
heuristic, not a search for the optimal split amount.

Splitting requires exactly two members, both still alive at settlement. The transferring
spouse must turn 65 by December 31 of the tax year; the receiving spouse may be younger.
The model treats RRSP assets as RRIFs at retirement. Only withdrawals from `Taxable` assets
are splittable; CPP, OAS, capital gains, and TFSA withdrawals are not. This setting changes
taxable-income attribution, not account ownership or desired household spending.

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
