namespace Planning.Model.Identifiers;

public readonly record struct MemberId( int Value ) : IComparable<MemberId> {
	public static implicit operator MemberId( int value ) => new( value );
	public int CompareTo( MemberId other ) => Value.CompareTo( other.Value );
	public override string ToString() => Value.ToString();
}
