namespace Planning.Model.Identifiers;

public readonly record struct PeriodNumber( int Value ) : IComparable<PeriodNumber> {
	public int CompareTo( PeriodNumber other ) => Value.CompareTo( other.Value );
	public override string ToString() => Value.ToString();
}
