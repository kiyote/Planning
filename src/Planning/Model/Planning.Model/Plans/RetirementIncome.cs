namespace Planning.Model.Plans;

public record RetirementIncome(
	decimal GoGo,
	decimal SlowGo,
	int SlowGoYears,
	decimal NoGo,
	int NoGoYears
) {
	public static readonly RetirementIncome None = new RetirementIncome(
		GoGo: 0.0m,
		SlowGo: 0.0m,
		SlowGoYears: 0,
		NoGo: 0.0m,
		NoGoYears: 0
	);
}
