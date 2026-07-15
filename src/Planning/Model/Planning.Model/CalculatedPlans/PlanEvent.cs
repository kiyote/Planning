namespace Planning.Model.CalculatedPlans;

/// <summary>
/// A dated, named event that occurs over the timeline of a calculated plan, such as a member
/// retiring, taking CPP or OAS, dying, or a retirement-income phase transition.
/// </summary>
public record PlanEvent(
	DateOnly Date,
	string Name,
	PlanEventKind Kind
);
