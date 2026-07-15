namespace Planning.Compiler;

public sealed class PlanValidationException : InvalidOperationException {

	public IReadOnlyList<string> Errors { get; }

	public PlanValidationException(
		IReadOnlyList<string> errors
	) : base( BuildMessage( errors ) ) {
		Errors = errors;
	}

	private static string BuildMessage(
		IReadOnlyList<string> errors
	) {
		return "The plan is invalid:" + Environment.NewLine
			+ string.Join( Environment.NewLine, errors.Select( e => "- " + e ) );
	}
}
