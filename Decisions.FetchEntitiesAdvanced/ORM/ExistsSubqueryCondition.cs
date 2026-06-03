using System.Collections.Generic;
using System.Data;
using DecisionsFramework.Data.ORMapper;
using DecisionsFramework.Data.ORMapper.DatabaseDrivers;

namespace Decisions.FetchEntitiesAdvanced.ORM;

/// <summary>
/// A <see cref="WhereCondition"/> that renders as:
/// <code>[NOT] EXISTS (SELECT 1 FROM childTable WHERE joinClause [AND additionalConditions])</code>
///
/// Used by both <c>RelatedEntityFilter</c> (ORMOneToManyRelationship-based) and
/// <c>JoinDefinition</c> when <c>OutputJoinedData = false</c> and <c>RequireMatch = true</c>.
/// </summary>
public class ExistsSubqueryCondition : WhereCondition
{
    private readonly string childTableName;

    /// <summary>
    /// Pre-composed ON clause. May contain multiple conditions joined with AND
    /// (e.g. <c>"chj_.location_id = main.id AND chj_.status = 'ACTIVE'"</c>).
    /// </summary>
    private readonly string joinClause;

    /// <summary>Optional additional WHERE conditions applied inside the subquery.</summary>
    private readonly AbstractWhereSet? additionalConditions;

    /// <summary>When <c>true</c>, renders as NOT EXISTS.</summary>
    private readonly bool negate;

    // ------------------------------------------------------------------
    // Constructors
    // ------------------------------------------------------------------

    /// <summary>
    /// Multiple join conditions from a <see cref="Decisions.FetchEntitiesAdvanced.Join.FieldMapping"/> list.
    /// All conditions are ANDed together in the subquery WHERE clause.
    /// </summary>
    public ExistsSubqueryCondition(
        string childTableName,
        IEnumerable<string> joinConditions,
        AbstractWhereSet? additionalConditions = null,
        bool negate = false)
    {
        this.childTableName = childTableName;
        this.joinClause = string.Join(" AND ", joinConditions);
        this.additionalConditions = additionalConditions;
        this.negate = negate;
    }

    // ------------------------------------------------------------------
    // WhereCondition implementation
    // ------------------------------------------------------------------

    public override string GetWhereString(IORMDatabaseInterface driver, ORMCommandSequence sequence)
    {
        string safeTable = driver.GetSafeTableName(childTableName);

        string additionalWhere = string.Empty;
        if (additionalConditions?.WhereConditions?.Count > 0)
        {
            string inner = additionalConditions.GetWhereString(driver, sequence);
            inner = inner.Trim();
            if (inner.StartsWith('(') && inner.EndsWith(')'))
                inner = inner[1..^1].Trim();
            if (!string.IsNullOrWhiteSpace(inner))
                additionalWhere = $" AND {inner}";
        }

        string keyword = negate ? "NOT EXISTS" : "EXISTS";
        return $"{keyword} (SELECT 1 FROM {safeTable} WHERE {joinClause}{additionalWhere})";
    }

    public override void ApplyValuesToCommand(IDbCommand command, IORMDatabaseInterface driver, ORMCommandSequence sequence)
    {
        additionalConditions?.ApplyValuesToCommand(command, driver, sequence);
    }
}
