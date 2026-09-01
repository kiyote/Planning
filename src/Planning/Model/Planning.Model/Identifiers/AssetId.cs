namespace Planning.Model.Identifiers;

public readonly record struct AssetId( int Value ) : IComparable<AssetId> {
	public int CompareTo( AssetId other ) => Value.CompareTo( other.Value );
	public override string ToString() => Value.ToString();
}
