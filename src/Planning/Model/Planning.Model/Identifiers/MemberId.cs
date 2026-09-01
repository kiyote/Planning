namespace Planning.Model.Identifiers;

public readonly record struct MemberId( int Value ) : IComparable<MemberId> {
	public int CompareTo( MemberId other ) => Value.CompareTo( other.Value );
	public override string ToString() => Value.ToString();
}
