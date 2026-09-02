namespace Planning.Model.Plans;

/// <param name="Member">
/// The member whose account receives the contribution: the annuitant.
/// </param>
/// <param name="Indexed">
/// Whether <paramref name="Amount"/> grows over time. When false the contribution stays flat at
/// the stated amount for the life of the plan.
/// </param>
/// <param name="AnnualIncreasePercent">
/// The rate at which <paramref name="Amount"/> grows each year while <paramref name="Indexed"/>
/// is set. Stating a rate decouples the contribution from the cost of living, because the reason
/// a contribution rises is usually a raise or a deliberate savings decision. Null falls back to
/// the plan's inflation rate, for a contribution that is only meant to hold its real value.
/// Ignored entirely when <paramref name="Indexed"/> is false.
/// </param>
/// <param name="Spousal">
/// The member who funds the contribution, when that is not <paramref name="Member"/>. A spousal
/// contribution consumes the contributor's registered room rather than the annuitant's, and
/// withdrawals taken within the attribution window are taxed back to the contributor. Null, empty
/// or equal to <paramref name="Member"/> means an ordinary contribution the member makes for
/// themselves.
/// </param>
public record Contribution(
	string Member,
	decimal Amount,
	int StartYear,
	bool Indexed,
	decimal? AnnualIncreasePercent,
	string? Spousal = null
) {

	/// <summary>
	/// Whether this contribution is made by one member into the other member's account, which is
	/// what triggers both the room transfer and the attribution rule.
	/// </summary>
	public bool IsSpousal => !string.IsNullOrWhiteSpace( Spousal ) && Spousal != Member;

	/// <summary>
	/// The member who actually funds the contribution, which is the annuitant themselves unless
	/// this is a spousal contribution.
	/// </summary>
	public string Contributor => IsSpousal ? Spousal! : Member;

	public static readonly Contribution None = new Contribution(
		Member: "",
		Amount: 0m,
		StartYear: 0,
		Indexed: false,
		AnnualIncreasePercent: null
	);
}
