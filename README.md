# Decisions.FetchEntitiesAdvanced

An advanced "Fetch Entities" flow step for the Decisions platform (v9.25+) that goes beyond
the built-in step with mixed AND/OR filter logic, per-condition null handling, EXISTS subquery
filtering, explicit JOIN-based related entity loading, and SQL paging.

## Features

| Feature | Description |
|---|---|
| **Mixed AND/OR filters** | `FilterGroup` supports nested AND/OR groups with sub-groups |
| **Per-condition null handling** | `SkipFilter`, `MatchNull`, or `ReturnNoResults` per field |
| **EXISTS/NOT EXISTS filters** | Filter by child collections via `ORMOneToManyRelationship` without including child data in output |
| **Explicit JOINs** | Join any ORM entity type to any other via user-specified fields; supports chaining (A → B → C) |
| **Output Joined Data** | Toggle: joins can filter-only (EXISTS) or batch-load related entities into a generated output type |
| **Generated output type** | Auto-generated `DefinedDataStructure` DTO created on first save; immutable shape validation on subsequent saves |
| **Paging** | PageSize + PageNumber flow input (sort field required) |
| **Full parity** | FastFetch, EditCopy, ReadUncommitted, FetchDeletedEntities, RespectPermission, LimitResults |

## Installation

1. Build the module:
   ```bash
   chmod +x build_module.sh
   ./build_module.sh
   ```
2. Upload `Decisions.FetchEntitiesAdvanced.zip` to your Decisions instance via the Module Manager.

## Usage

### Basic filtering with AND/OR groups

1. Drop **Fetch Entities (Advanced)** onto a flow.
2. Set **Type Name** to the ORM entity type (e.g., `DecisionsFramework.ServiceLayer.Services.Accounts.Account`).
3. Configure **Root Filter Group**: add conditions, set their **Input Alias** (creates a flow input), **Match Type**, and **When Null** behaviour.
4. Map flow data to the generated inputs.

### EXISTS filtering (filter by child collections)

1. Add a **Related Entity Filter** with the `ORMOneToManyRelationship` field name.
2. Set **Presence** to `HasAtLeastOne` (EXISTS) or `HasNone` (NOT EXISTS).
3. Optionally add a **Filter Group** for additional child-entity conditions.

### JOIN-based output (joined data)

1. Enable **Output Joined Data**.
2. Set **Output Type Name** (e.g., `LocationWithRooms`).
3. Add one or more **Join Definitions**:
   - **Related Type Name**: the type to join (e.g., `MyApp.Entities.Room`)
   - **Related Join Field**: the FK field on the related type (e.g., `LocationId`)
   - **Source Table**: `"main"` for primary → related; or an earlier join's **Output Alias** for chaining
   - **Source Join Field**: the matching field on the source (e.g., `Id`)
   - **Output Alias**: the property name on the generated type (e.g., `Rooms`)
   - **Is One To Many**: `true` → `Room[]`, `false` → `Room`
   - **Require Match**: whether to exclude primary entities with no matching related rows
4. **Save** the step — the output type is generated automatically on the first save.
5. The step outputs `LocationWithRooms[]` containing a `Source` property (the primary entity)
   and one property per join alias.

### Paging

1. Set **Page Size** (e.g., `50`).
2. Set **Sort Field** (sorting by the entity ID field is recommended for stable paging).
3. A **PageNumber** (int) flow input is automatically added to the step — wire it from your flow.

## Known limitations (v1)

- **Permission checking**: `RespectPermission` creates a plain query without the `vwGetFolderPerms` JOIN
  (the platform's `FolderService.BuildSelectEntitiesAndFolderSecurity` is internal). This will be
  addressed in a future version.
- **Paging is in-memory for the offset**: the step fetches `(PageNumber * PageSize)` rows from the
  database and slices in application memory. For large datasets with high page numbers, use
  `LimitResults` as an additional bound.
- **Chained JOIN RequireMatch**: when chained joins (SourceTable ≠ "main") use `RequireMatch = true`,
  the filtering is approximate for the chain walk-back. Direct joins work precisely.
- **SDK version 9.25.0**: if unavailable on NuGet, update the version to `9.26.0` in
  `Decisions.FetchEntitiesAdvanced.csproj`.

## Project structure

```
Decisions.FetchEntitiesAdvanced/
├── Conditions/
│   ├── ExistsPresence.cs
│   ├── FilterCombinator.cs
│   ├── FilterCondition.cs
│   ├── FilterGroup.cs
│   ├── NullBehavior.cs
│   └── RelatedEntityFilter.cs
├── Join/
│   └── JoinDefinition.cs
├── ORM/
│   └── ExistsSubqueryCondition.cs
└── Steps/
    └── AdvancedFetchEntitiesStep.cs
```
