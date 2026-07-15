namespace Planning.Model.Plans;

public record Contribution(
	string Member,
	string Asset,
	decimal Amount,
	int StartYear
);
