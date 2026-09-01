namespace Planning.Model.Plans;

public record Inheritance(
	string Member,
	decimal Amount,
	int AgeReceived
) {

	public static readonly Inheritance None = new Inheritance( "", 0.0m, 0 );
}
