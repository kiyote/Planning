namespace Planning.Model.Plans;

public record Expenditure(
	decimal Amount,
	bool Indexed,
	bool Repeats,
	int? IntervalYears,
	int StartYear,
	string Name
);
