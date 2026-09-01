namespace Planning.Model.Plans;

/// <summary>
/// Describes how withdrawals from an asset are treated for income tax purposes.
/// </summary>
public enum AssetTaxStatus {

	Unknown = 0,

	/// <summary>
	/// The full withdrawal amount is added to the taxable base (e.g. RRSP/RRIF).
	/// </summary>
	Taxable = 1,

	/// <summary>
	/// Only the capital gains inclusion portion (50%) of the withdrawal is added to the
	/// taxable base (e.g. non-registered investment accounts).
	/// </summary>
	CapitalGains = 2,

	/// <summary>
	/// Withdrawals are not taxable (e.g. TFSA).
	/// </summary>
	TaxExempt = 3
}
