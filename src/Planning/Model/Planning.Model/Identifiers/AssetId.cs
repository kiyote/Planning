namespace Planning.Model.Identifiers;

public readonly record struct AssetId( int Value ) : IComparable<AssetId> {
	public static implicit operator AssetId( int value ) => new( value );
	public int CompareTo( AssetId other ) => Value.CompareTo( other.Value );
	public override string ToString() => Value.ToString();
}
