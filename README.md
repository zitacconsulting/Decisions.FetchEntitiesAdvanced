# Fetch Entities (Advanced)

> ⚠️ **Important:** Use this module at your own risk. See the **Disclaimer** section below.

A flow step module for the [Decisions](https://decisions.com) platform that replaces the built-in *Fetch Entities* step with full AND/OR filter trees, explicit JOIN-based related-entity loading, collection and entity-reference field filtering, and SQL paging.

## Features

- **Recursive AND/OR filter tree** — nest filter groups to any depth with AND or OR logic.
- **Rich filter operators** — `=`, `≠`, `>`, `≥`, `<`, `≤`, `LIKE` (with `%` wildcards), plus unary checks: IS NULL, IS NOT NULL, IS EMPTY, IS NOT EMPTY, IS NULL OR EMPTY, IS NOT NULL OR EMPTY.
- **Step-input values** — any filter condition can be wired to a flow input instead of a static value.
- **Collection field filtering** — filter on `ORMOneToManyRelationship` child lists: Contains, Does Not Contain, First/Last In List, Count comparisons (`=`, `≠`, `>`, `≥`, `<`, `≤`), and aggregate comparisons (Sum, Average, Min, Max of a sub-field).
- **Entity-reference field filtering** — traverse dot-path chains through entity-reference navigation properties (e.g. `SubtypeData.NestedRef.SomeField`).
- **JOIN-based output** — join any ORM entity to the primary result; output is a generated DTO with a `Source` property and one array per join.
- **Inverse FK joins** — join from child to parent even when the FK column has no C# property on the child class (managed by the parent's `ORMOneToManyRelationship`).
- **Filter-only JOINs** — joins with *Include In Output* disabled and *Require Match* enabled translate to correlated EXISTS subqueries.
- **Chained JOINs** — join a second type to the result of a first join (A → B → C).
- **Sort & limit** — sort by any field; optionally cap results with *Limit Results*.
- **SQL paging** — enable *Use Paging* to get `PageSize` and `PageNumber` step inputs (sort field required for stable pages).
- **Full parity with built-in step** — Fast Fetch, Edit Copy, Read Uncommitted (NoLock), Fetch Deleted Entities, Respect Permission, Folder-aware filtering.
- **Debug output** — enable *Show Query In Output* to capture the generated SQL in the flow output.
- **Validation** — stale join conditions and sort fields are flagged when the primary type changes.

## Requirements

- Decisions 9.25 or later

## Installation

### Option 1: Install Pre-built Module
1. Download the compiled module (`.zip` file).
2. Log into the Decisions Portal.
3. Navigate to **System > Administration > Features**.
4. Click **Install Module**.
5. Upload the `.zip` file.
6. Restart the Decisions service if prompted.

### Option 2: Build from Source
See the [Building from Source](#building-from-source) section below.

## Usage

Once installed, add the module as a dependency to your project, then drag **Fetch Entities (Advanced)** from the **Database** category in the Flow Designer.

### Basic filtering

1. Set **Type Name** to the ORM entity type you want to query.
2. Add one or more **Filters**. Each filter node can be:
   - A **leaf condition** — pick a field, an operator, and a value (static or step input).
   - An **AND** or **OR** group — add child conditions that are combined with the chosen logic.
3. Wire any step-input conditions from your flow data.

### LIKE operator

Use `%` as a wildcard character in string values:
- `%foo` — ends with "foo"
- `foo%` — starts with "foo"
- `%foo%` — contains "foo"

### Collection field filtering

When a field is a child-entity list (`ORMOneToManyRelationship`), additional operators appear:

| Operator | Description |
|---|---|
| List Contains / Does Not Contain | At least one (or no) child row matches a sub-field condition. |
| First In List / Last In List | The first or last child row (by PK) matches a sub-field condition. |
| Count = / ≠ / > / ≥ / < / ≤ | Compare the number of child rows to a threshold. |
| Sum / Average / Min / Max Of | Aggregate a numeric sub-field and compare to a threshold. |

### JOIN-based output

1. Add a **Join Datatype** entry.
2. Set **Source** (the primary type or a prior join's alias) and **Join Datatype** (the related type).
3. Add one or more **Join Conditions** — map a field from the join type to a field from the source, or to a literal value.
4. Optionally set an **Output Alias** (defaults to the related type's short name).
5. Enable **Include In Output** to include the matched related entities in the output DTO.
6. Enable **Require Match (Inner Join)** to exclude primary rows with no matching related rows.
7. **Save** the step — the output type is generated automatically.

The step outputs an array of objects with:
- `Source` — the primary entity.
- One array property per output join, named by the join's effective alias.

### Paging

1. Enable **Use Paging**.
2. Set a **Sort Field** (sorting by the entity ID field is recommended for stable pages).
3. Wire **PageSize** (int) and **PageNumber** (int, 1-based) from your flow.

## Step Reference

### Settings

| Setting | Default | Description |
|---|---|---|
| Type Name | — | The ORM entity type to query. |
| Filters | — | Recursive AND/OR filter tree. |
| Join Datatype | — | Zero or more join definitions. |
| Sort Field | — | Field to order results by. Cleared automatically when Type Name changes. |
| Sort Order | Ascending | Ascending or Descending. |
| Limit Results | — | Maximum rows to return. Hidden when Use Paging is enabled. |
| Use Paging | false | Enables PageSize / PageNumber step inputs. |
| Respect Permission | true | Applies Decisions permission checks to the query. |
| Fetch Deleted Entities | false | Includes soft-deleted rows. |
| Fast Fetch | true | Uses the ORM fast-fetch path. |
| Edit Copy | false | Returns editable (detached) entity copies. |
| Read Uncommitted (NoLock) | true | Adds a NoLock hint where supported. |
| Show Path for One Result | false | Adds a *Result* outcome for single-row results. |
| Show Query In Output | false | Adds a *Query* string output to all outcomes. |

### Dynamic Inputs

| Input | Type | Condition |
|---|---|---|
| FolderId | String | Primary type is folder-aware. |
| PageSize | Int | Use Paging = true. |
| PageNumber | Int | Use Paging = true. |
| *(filter input names)* | Varies | Each filter condition with a step-input value. |

### Outcomes

| Outcome | Outputs | Description |
|---|---|---|
| No Results | Query? | No rows matched. |
| Results | EntityResults[], Query? | One or more rows returned. |
| Result | EntityResult, Query? | Exactly one row; only present when *Show Path for One Result* is enabled. |

## Building from Source

### Prerequisites

- .NET 10.0 SDK or higher
- `CreateDecisionsModule` Global Tool (installed automatically during build)
- Decisions Platform SDK (NuGet package: `DecisionsSDK`)

### Build Steps

#### On Linux/macOS:
```bash
chmod +x build_module.sh
./build_module.sh
```

#### On Windows (PowerShell):
```powershell
.\build_module.ps1
```

#### Manual Build:
```bash
dotnet build build.proj
dotnet msbuild build.proj -t:build_module
```

### Build Output

The build creates `Decisions.FetchEntitiesAdvanced.zip` in the root directory. Upload it to Decisions via **System > Administration > Features**.

## Project Structure

```
Decisions.FetchEntitiesAdvanced/
├── Conditions/
│   ├── EntityRefOperator.cs       # Operator constants for entity-reference fields
│   ├── FilterNode.cs              # Filter tree node (leaf, AND group, OR group)
│   ├── FilterNodeType.cs          # Enum: Filter, And, Or
│   ├── FilterValueType.cs         # Value type constants (StepInput, StringValue, …)
│   └── ListOperator.cs            # Operator constants for collection fields
├── Join/
│   ├── FieldMapping.cs            # Single join condition with UI dropdowns
│   ├── FieldMappingValueType.cs   # Side-type constants for join conditions
│   ├── JoinDefinition.cs          # One JOIN entry (type, conditions, alias, options)
│   ├── JoinOperator.cs            # Operator constants (=, ≠, >, LIKE, …)
│   └── JoinSideType.cs            # Side-type constants (Field, StringValue, IsNull, …)
├── ORM/
│   ├── ExistsSubqueryCondition.cs # WHERE EXISTS (…) ORM condition
│   └── RawSqlWhereCondition.cs    # Raw SQL fragment ORM condition
└── Steps/
    └── AdvancedFetchEntitiesStep.cs  # Main step implementation
```

## Disclaimer

This module is provided "as is" without warranties of any kind. Use it at your own risk. The authors, maintainers, and contributors disclaim all liability for any direct, indirect, incidental, special, or consequential damages, including data loss or service interruption, arising from the use of this software.

**Important Notes:**
- Always test in a non-production environment first.
- This module is not officially supported by Decisions.

## License

[MIT](LICENSE)
