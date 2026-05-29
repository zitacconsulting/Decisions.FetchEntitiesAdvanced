namespace Decisions.FetchEntitiesAdvanced.Join;

/// <summary>
/// String constants for the value type on each side of a join condition.
/// Stored as strings so available options can be filtered dynamically based on
/// the detected .NET type of the opposing field.
///
/// Unary check types (Is Null … Is Not Null Or Empty) live here rather than in
/// <see cref="JoinOperator"/> so that selecting one collapses the form to a
/// single-field check and hides the Condition and opposing section entirely.
/// </summary>
public static class JoinSideType
{
    // Comparison value types
    public const string Field         = "Field";
    public const string StringValue   = "String Value";
    public const string NumberValue   = "Number Value";
    public const string BoolValue     = "Bool Value";
    public const string DateTimeValue = "Date/Time Value";
    public const string GuidValue     = "Guid Value";

    // Unary field checks (no opposing side required)
    public const string IsNull           = "Is Null";
    public const string IsNotNull        = "Is Not Null";
    public const string IsEmpty          = "Is Empty";
    public const string IsNotEmpty       = "Is Not Empty";
    public const string IsNullOrEmpty    = "Is Null Or Empty";
    public const string IsNotNullOrEmpty = "Is Not Null Or Empty";
}
