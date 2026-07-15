namespace Planning.Model.Plans;

/// <summary>
/// A single progressive income-tax bracket. Income above <see cref="LowerBound"/>
/// (and below the next bracket's lower bound) is taxed at <see cref="Rate"/>.
/// </summary>
/// <param name="LowerBound">The inclusive lower income threshold at which this bracket's rate begins to apply, in nominal (start-year) dollars.</param>
/// <param name="Rate">The marginal tax rate for income within this bracket, expressed as a percentage (for example, 15 for 15%).</param>
public record TaxBracket(
	decimal LowerBound,
	decimal Rate
);
