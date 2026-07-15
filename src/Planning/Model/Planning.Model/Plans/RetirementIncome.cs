namespace Planning.Model.Plans;

public record RetirementIncome(
	decimal GoGo,
	decimal SlowGo,
	int SlowGoYears,
	decimal NoGo,
	int NoGoYears
);
