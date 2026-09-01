namespace Planning.Model.Plans;

public record LifeInsurance(
	string Member,
	decimal Amount
) {

	public static readonly LifeInsurance None = new LifeInsurance( "", 0.0m );
}
