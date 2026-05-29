using System.Data;
using DecisionsFramework.Data.ORMapper;
using DecisionsFramework.Data.ORMapper.DatabaseDrivers;

namespace Decisions.FetchEntitiesAdvanced.ORM;

/// <summary>
/// A <see cref="WhereCondition"/> that injects a pre-composed SQL fragment verbatim.
/// Used for constant- and expression-based join conditions that cannot be expressed
/// through the typed <see cref="FieldWhereCondition"/> API (e.g. <c>table.col = 'ACTIVE'</c>
/// or <c>table.col IS NULL</c>).
/// </summary>
public class RawSqlWhereCondition : WhereCondition
{
    private readonly string sql;

    public RawSqlWhereCondition(string sql) => this.sql = sql;

    public override string GetWhereString(IORMDatabaseInterface driver, ORMCommandSequence sequence) => sql;

    public override void ApplyValuesToCommand(IDbCommand command, IORMDatabaseInterface driver, ORMCommandSequence sequence)
    {
        // All values are baked into the SQL string — no parameters to bind.
    }
}
