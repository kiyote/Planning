using Planning.Model.Identifiers;

namespace Planning.Model.CompiledPlans;

/// <param name="MemberId">
/// The member who funds the contribution and whose registered room it consumes. For a spousal
/// contribution this is the contributor, not the annuitant.
/// </param>
/// <param name="DestinationMemberId">
/// The member whose accounts receive the contribution. Equal to <paramref name="MemberId"/>
/// unless this is a spousal contribution.
/// </param>
public record CompiledContribution(
	ContributionId ContributionId,
	MemberId MemberId,
	MemberId DestinationMemberId,
	decimal Amount
) {

	/// <summary>
	/// Whether the funds are being contributed into the other member's accounts.
	/// </summary>
	public bool IsSpousal => MemberId != DestinationMemberId;
}
