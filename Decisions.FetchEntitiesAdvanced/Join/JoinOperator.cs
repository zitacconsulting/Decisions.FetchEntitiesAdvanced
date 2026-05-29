namespace Decisions.FetchEntitiesAdvanced.Join;

/// <summary>
/// String constants for binary join condition operators.
/// Stored as strings so the available operator list can be filtered dynamically
/// based on the .NET types detected on each side of the condition.
///
/// Unary checks (IS NULL, IS EMPTY, etc.) are no longer operators — they live in
/// <see cref="JoinSideType"/> so selecting one collapses the row to a single field.
/// </summary>
public static class JoinOperator
{
    public const string Equal          = "Equal";
    public const string NotEqual       = "Not Equal";
    public const string GreaterThan    = "Greater Than";
    public const string GreaterOrEqual = "Greater Or Equal";
    public const string LessThan       = "Less Than";
    public const string LessOrEqual    = "Less Or Equal";
    public const string Like           = "Like";
}
