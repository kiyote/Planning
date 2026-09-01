namespace Planning.Model.Identifiers;

public readonly record struct ContributionId( int Value ) : IComparable<ContributionId> {
	public int CompareTo( ContributionId other ) => Value.CompareTo( other.Value );
	public override string ToString() => Value.ToString();
}
