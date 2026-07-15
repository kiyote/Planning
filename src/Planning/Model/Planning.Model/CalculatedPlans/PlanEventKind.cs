namespace Planning.Model.CalculatedPlans;

/// <summary>
/// Distinguishes the category of a <see cref="PlanEvent"/> so consumers (such as graphing)
/// can render each category differently.
/// </summary>
public enum PlanEventKind {

	/// <summary>
	/// A household member lifecycle event such as retirement, CPP start, OAS start, or death.
	/// </summary>
	Lifecycle,

	/// <summary>
	/// A retirement-income lifestyle phase transition (Go-Go, Slow-Go, or No-Go).
	/// </summary>
	RetirementPhase
}
