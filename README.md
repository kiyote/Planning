
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

Each member must specify three percentages, all between 0 and 100:

* `CPPBasePercent`: estimated share of the base CPP maximum at age 65.
* `CPPFirstAdditionalPercent`: estimated share of maximum first-additional accrual during
  eligible working months from 2019 onward.
* `CPPSecondAdditionalPercent`: estimated share of maximum second-additional accrual during
  eligible working months from 2024 onward.

The sample uses Todd 90/100/100 and Tina 50/90/0. These are planning assumptions, not verified
benefit quotes. No salary or separately quoted enhancement payment is required. A value of
100 means maximum accrual in the included working months, not a fully matured 40-year pension.

Use the published **age-65 retirement maximum** for `CPPMaximum` and the published
**age-65 combined retirement/survivor maximum** for `CPPCombinedSurvivorMaximum`, both for
the same period and expressed in plan-start dollars. The latter is not a ceiling on every
survivor's actual payment. For example, the January 2026 amounts are 1,507.65 and 1,531.56.

The two published maxima are used only to estimate the plan-start base reference. At full
entitlement, the combined maximum exceeds the retirement maximum by 36% of the enhanced
component:

```text
Reference enhanced maximum = (CPPCombinedSurvivorMaximum - CPPMaximum) / 0.36
Reference base maximum = CPPMaximum - Reference enhanced maximum
```

With the sample's maxima, the reference base maximum is about 1,441.23 per month. Published
amounts are rounded, so this remains an approximation. The combined maximum must be at least
100% and less than 136% of the retirement maximum, leaving a positive base reference.
A zero `CPPMaximum` disables every CPP component.

The compiler builds separate monthly base, first-additional and second-additional entitlements,
expressed in plan-start dollars before start-age adjustment. Enhancement contributions are
assumed continuous from the later of age 18 eligibility and the enhancement's introduction,
until retirement, CPP commencement, or death, whichever comes first. The death month is included.
A member with no retirement age uses the household retirement date. The percentages can
approximate reduced earnings or contribution gaps; individual employment histories are not inferred.

First-additional accrual uses phase-in weights of 15%, 30%, 50%, 75% and 100% in 2019 through
2023, then 100%. The fully accrued replacement rate is 8.33% of pensionable earnings.
Second-additional accrual uses the higher earnings band: 4,700/68,500 in 2024, 9,900/71,300
in 2025, 10,400/74,600 in 2026, and the fully phased 14% band thereafter. Its replacement rate
is 33.33%. Each component uses its best 480 eligible months, divided by 480 even for shorter
careers. The reference earnings level is inferred from base CPP's 25% replacement rate.

No real growth in the wage reference is assumed: all components are subsequently indexed by
the plan's inflation rate. Start-age adjustments apply to the sum of the three components.
Deferring CPP after stopping work does not earn extra contribution years. Contributions after
CPP commencement and their post-retirement benefits are not modelled. CSV layout is unchanged:
the components are rolled into `CPP`, and survivor components into `CPP Survivor`.

**Migration:** replace the old `CPPPercent` with `CPPBasePercent` and explicitly add both
additional percentages (use zero where none is assumed). The sample retains the old proportional
model's inferred base shares, 90% and 50%, then replaces its enhancement allowance with projected
accrual. Do not retain the old total CPP amount and add the enhancements again. If the original
percentage came from an official total pension estimate, reconcile that estimate and its future
work assumptions before treating the base percentage as a verified entitlement.

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
[CPP enhancement](https://www.canada.ca/en/services/benefits/publicpensions/cpp/cpp-enhancement.html),
[CPP Act section 48.1](https://laws-lois.justice.gc.ca/eng/acts/C-8/section-48.1.html),
[section 48.2](https://laws-lois.justice.gc.ca/eng/acts/C-8/section-48.2.html),
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
