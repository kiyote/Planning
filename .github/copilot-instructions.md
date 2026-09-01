# Copilot Instructions

## Project Guidelines
- Prefers explicit parameters over optional parameters with default values in C# (e.g., avoid `decimal CostBase = 0m` on record positional parameters; require callers to pass the value).
- In test code, default parameter values are acceptable when they represent neutral/empty initial values (e.g., `decimal costBase = 0m`). However, magic numbers — values that encode a real decision — should be passed explicitly (preferably as named arguments) so their origin and rationale can be explained. Production code still prefers fully explicit parameters over optional ones with defaults.