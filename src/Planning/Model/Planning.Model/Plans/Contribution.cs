namespace Planning.Model.Plans;

public record Contribution(
	string Member,
	decimal Amount,
	int StartYear,
	bool Indexed
);
