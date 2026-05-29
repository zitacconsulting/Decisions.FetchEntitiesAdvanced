namespace Decisions.FetchEntitiesAdvanced.Join;

/// <summary>How a side of a <see cref="FieldMapping"/> resolves to a SQL expression.</summary>
public enum FieldMappingValueType
{
    /// <summary>Resolved to a column reference on the relevant table (e.g. <c>alias.column_name</c>).</summary>
    Field,

    /// <summary>Treated as a quoted SQL string constant (e.g. <c>'ACTIVE'</c>).</summary>
    Constant,

    /// <summary>
    /// Injected verbatim as a raw SQL fragment.
    /// The special value <c>"null"</c> (case-insensitive) generates an <c>IS NULL</c> condition
    /// instead of <c>= NULL</c>.
    /// </summary>
    Expression
}
