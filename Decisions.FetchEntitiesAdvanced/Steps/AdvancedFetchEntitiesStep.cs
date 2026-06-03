using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using Decisions.FetchEntitiesAdvanced;
using Decisions.FetchEntitiesAdvanced.Conditions;
using Decisions.FetchEntitiesAdvanced.Join;
using Decisions.FetchEntitiesAdvanced.ORM;
using DecisionsFramework;
using DecisionsFramework.Data.ORMapper;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.DataStructure;
using DecisionsFramework.Design.Flow;
using DecisionsFramework.Design.Flow.CoreSteps;
using DecisionsFramework.Design.Flow.Mapping;
using DecisionsFramework.Design.Flow.StepImplementations;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.Properties.Attributes;
using DecisionsFramework.ServiceLayer;
using DecisionsFramework.ServiceLayer.Services.Folder;
using DecisionsFramework.ServiceLayer.Utilities;
using DecisionsFramework.Utilities;
using DecisionsFramework.Utilities.Data;

namespace Decisions.FetchEntitiesAdvanced.Steps;

/// <summary>
/// An advanced alternative to the built-in "Fetch Entities" step with:
/// <list type="bullet">
///   <item>Recursive AND/OR filter tree that builds the SQL WHERE clause directly</item>
///   <item>Explicit JOIN-based related entity loading with output type generation</item>
///   <item>SQL LIMIT and optional step-input-driven paging</item>
///   <item>Full parity with built-in step settings (NoLock, FastFetch, EditCopy, etc.)</item>
/// </list>
/// </summary>
[ShapeImageAndColorProvider(StepColorType.UserDefinedTypesStepColor, "flow step images|database.svg")]
[AutoRegisterStep("Fetch Entities (Advanced)", "Database")]
[Writable]
public class AdvancedFetchEntitiesStep : BaseFlowAwareStep, ISyncStep, IDataConsumer, IDataProducer, IValidationSource
{
    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    private const string GENERATED_TYPE_NAMESPACE = "Zitac.FetchAdvanced.Generated";
    private const string MAIN_ALIAS = "main";

    // -------------------------------------------------------------------------
    // Backing fields
    // -------------------------------------------------------------------------

    [WritableValue] private string? typeName;
    [WritableValue] private FilterNode[]? filterNodes;
    [WritableValue] private JoinDefinition[]? joinDefinitions;
    [WritableValue] private string? lastGeneratedTypeName;
    [WritableValue] private string? sortField;
    [WritableValue] private ORMResultOrder sortOrder = ORMResultOrder.Ascending;
    [WritableValue] private int? limitResults;
    [WritableValue] private bool usePaging;
    [WritableValue] private bool respectPermission = true;
    [WritableValue] private bool fetchDeletedEntities;
    [WritableValue] private bool fastFetch = true;
    [WritableValue] private bool editCopy;
    [WritableValue] private bool readUncommitted = true;
    [WritableValue] private bool showPathForOneResult;
    [WritableValue] private bool showQueryInOutput;

    // -------------------------------------------------------------------------
    // Properties — Entity Fetch Definition
    // -------------------------------------------------------------------------

    [PropertyClassification(0, "Type Name", new[] { "Entity Fetch Definition" })]
    [TypePickerEditor(TypePick.ORMEntity)]
    public string? TypeName
    {
        get => typeName;
        set
        {
            typeName  = value;
            sortField = null;   // reset — old field name is invalid on the new type
            OnPropertyChanged(nameof(TypeName));
            OnPropertyChanged(nameof(SortField));
            OnPropertyChanged(nameof(InputData));
            ClearFlowStepCache();
            OnPropertyChanged(nameof(OutcomeScenarios));
            OnPropertyChanged(nameof(PrimaryEntityFieldNames));
            UpdateJoinContext();
            UpdateFilterContext();
        }
    }

    [PropertyClassification(5, "Join Datatype", new[] { "Entity Fetch Definition" })]
    public JoinDefinition[]? JoinDefinitions
    {
        get => joinDefinitions;
        set
        {
            WireJoinMappingEvents(joinDefinitions, attach: false);
            joinDefinitions = value;
            WireJoinMappingEvents(joinDefinitions, attach: true);
            UpdateJoinContext();
            UpdateFilterContext();
            OnPropertyChanged(nameof(JoinDefinitions));
            OnPropertyChanged(nameof(InputData));
            ClearFlowStepCache();
            OnPropertyChanged(nameof(OutcomeScenarios));
        }
    }

    [PropertyClassification(15, "Filters", new[] { "Entity Fetch Definition" })]
    public FilterNode[]? FilterNodes
    {
        get => filterNodes;
        set
        {
            WireFilterNodeEvents(filterNodes, attach: false);
            filterNodes = value;
            WireFilterNodeEvents(filterNodes, attach: true);
            UpdateFilterContext();
            OnPropertyChanged(nameof(FilterNodes));
            OnPropertyChanged(nameof(InputData));
        }
    }

    [PropertyClassification(61, "Sort Field", new[] { "Entity Fetch Definition" })]
    [SelectStringEditor("PrimaryEntityFieldNames", SelectStringEditorType.DropdownList, true)]
    public string? SortField
    {
        get => sortField;
        set { sortField = value; OnPropertyChanged(nameof(SortField)); }
    }

    [PropertyClassification(62, "Sort Order", new[] { "Entity Fetch Definition" })]
    public ORMResultOrder SortOrder
    {
        get => sortOrder;
        set { sortOrder = value; OnPropertyChanged(nameof(SortOrder)); }
    }

    [PropertyClassification(70, "Limit Results", new[] { "Entity Fetch Definition" })]
    [BooleanPropertyHidden(nameof(UsePaging), true)]
    public int? LimitResults
    {
        get => limitResults;
        set { limitResults = value; OnPropertyChanged(nameof(LimitResults)); }
    }

    [PropertyClassification(71, "Use Paging", new[] { "Entity Fetch Definition" })]
    public bool UsePaging
    {
        get => usePaging;
        set
        {
            usePaging = value;
            OnPropertyChanged(nameof(UsePaging));
            OnPropertyChanged(nameof(LimitResults));
            OnPropertyChanged(nameof(InputData));
            ClearFlowStepCache();
            OnPropertyChanged(nameof(OutcomeScenarios));
        }
    }

    // -------------------------------------------------------------------------
    // Properties — Security
    // -------------------------------------------------------------------------

    [PropertyClassification(0, "Respect Permission", new[] { "Security" })]
    public bool RespectPermission
    {
        get => respectPermission;
        set { respectPermission = value; OnPropertyChanged(nameof(RespectPermission)); }
    }

    // -------------------------------------------------------------------------
    // Properties — Modify Fetch Behavior
    // -------------------------------------------------------------------------

    [PropertyClassification(0, "Fetch Deleted Entities", new[] { "Modify Fetch Behavior" })]
    public bool FetchDeletedEntities
    {
        get => fetchDeletedEntities;
        set { fetchDeletedEntities = value; OnPropertyChanged(nameof(FetchDeletedEntities)); }
    }

    [PropertyClassification(1, "Fast Fetch (Read-Only Mode)", new[] { "Modify Fetch Behavior" })]
    public bool FastFetch
    {
        get => fastFetch;
        set { fastFetch = value; OnPropertyChanged(nameof(FastFetch)); }
    }

    [PropertyClassification(2, "Edit Copy", new[] { "Modify Fetch Behavior" })]
    public bool EditCopy
    {
        get => editCopy;
        set { editCopy = value; OnPropertyChanged(nameof(EditCopy)); }
    }

    [PropertyClassification(3, "Read Uncommitted (NoLock)", new[] { "Modify Fetch Behavior" })]
    public bool ReadUncommitted
    {
        get => readUncommitted;
        set { readUncommitted = value; OnPropertyChanged(nameof(ReadUncommitted)); }
    }

    // -------------------------------------------------------------------------
    // Properties — Modify Outputs
    // -------------------------------------------------------------------------

    [PropertyClassification(0, "Show Path for One Result", new[] { "Modify Outputs" })]
    public bool ShowPathForOneResult
    {
        get => showPathForOneResult;
        set { showPathForOneResult = value; OnPropertyChanged(nameof(ShowPathForOneResult)); ClearFlowStepCache(); OnPropertyChanged(nameof(OutcomeScenarios)); }
    }

    [PropertyClassification(1, "Show Query In Output", new[] { "Modify Outputs" })]
    public bool ShowQueryInOutput
    {
        get => showQueryInOutput;
        set { showQueryInOutput = value; OnPropertyChanged(nameof(ShowQueryInOutput)); ClearFlowStepCache(); OnPropertyChanged(nameof(OutcomeScenarios)); }
    }

    // -------------------------------------------------------------------------
    // Dropdown source properties (hidden from UI)
    // -------------------------------------------------------------------------

    [PropertyHidden]
    public string[] PrimaryEntityFieldNames => GetOrmFieldNamesForType(GetPrimaryType());

    // -------------------------------------------------------------------------
    // IDataConsumer — dynamic flow inputs
    // -------------------------------------------------------------------------

    public DataDescription[] InputData
    {
        get
        {
            var inputs = new List<DataDescription>();

            // Step inputs from filter nodes (recursive)
            var seenFilterInputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectFilterNodeInputs(filterNodes, inputs, seenFilterInputs);

            // FolderId for folder-aware types
            var primary = GetPrimaryType();
            if (primary != null && typeof(AbstractFolderEntity).IsAssignableFrom(primary))
                inputs.Add(new DataDescription(new DecisionsNativeType(typeof(string)), "FolderId", false, true, true));

            // Paging step inputs
            if (usePaging)
            {
                inputs.Add(new DataDescription(new DecisionsNativeType(typeof(int)), "PageSize", false, false, false));
                inputs.Add(new DataDescription(new DecisionsNativeType(typeof(int)), "PageNumber", false, true, false));
            }

            // Step inputs from join field mappings
            var seenJoinInputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var join in JoinDefinitions ?? [])
            {
                foreach (var m in join.FieldMappings ?? [])
                {
                    if (!m.IsBinaryCondition) continue;
                    if (m.JoinUseStepInput && FieldMapping.IsLiteralType(m.JoinValueType)
                        && !string.IsNullOrWhiteSpace(m.JoinInputName)
                        && seenJoinInputs.Add(m.JoinInputName!))
                    {
                        inputs.Add(new DataDescription(
                            new DecisionsNativeType(InputTypeForSideType(m.JoinValueType)),
                            m.JoinInputName!, false, false, false));
                    }
                    if (m.SourceUseStepInput && FieldMapping.IsLiteralType(m.SourceValueType)
                        && !string.IsNullOrWhiteSpace(m.SourceInputName)
                        && seenJoinInputs.Add(m.SourceInputName!))
                    {
                        inputs.Add(new DataDescription(
                            new DecisionsNativeType(InputTypeForSideType(m.SourceValueType)),
                            m.SourceInputName!, false, false, false));
                    }
                }
            }

            return inputs.ToArray();
        }
    }

    private static void CollectFilterNodeInputs(
        FilterNode[]? nodes,
        List<DataDescription> inputs,
        HashSet<string> seen)
    {
        foreach (var node in nodes ?? [])
        {
            if (node.IsFilterNode && node.IsStepInput && !string.IsNullOrWhiteSpace(node.InputName))
            {
                if (seen.Add(node.InputName!))
                {
                    // Determine input type: collection → sub-field type; entity ref → sub-field type or string FK;
                    // primitive → field type
                    Type? ft;
                    if (node.IsCollectionField)
                        ft = FilterNode.CountOperators.Contains(node.Operator ?? string.Empty)
                            ? typeof(double)  // COUNT(*) is always numeric
                            : OrmFieldHelper.GetFieldNetType(node.ElementTypeName, node.SubField);
                    else if (node.IsEntityRefField && node.FieldName?.Contains('.') == true)
                        ft = GetEntityRefSubFieldType(node);
                    else
                        ft = OrmFieldHelper.GetFieldNetType(node.SelectedTypeFullName, node.FieldName);
                    inputs.Add(new DataDescription(
                        new DecisionsNativeType(FilterInputType(ft)),
                        node.InputName!, false, false, false));
                }
            }
            CollectFilterNodeInputs(node.Children, inputs, seen);
        }
    }

    // -------------------------------------------------------------------------
    // IDataProducer — outcome scenarios
    // -------------------------------------------------------------------------

    public override OutcomeScenarioData[] OutcomeScenarios
    {
        get
        {
            var queryDesc = ShowQueryInOutput
                ? new DataDescription(new DecisionsNativeType(typeof(string)), "Query", false, false, true)
                : null;
            var totalDesc = usePaging
                ? new DataDescription(new DecisionsNativeType(typeof(long)), "TotalResults", false, false, false)
                : null;

            var noResultsBase = new List<DataDescription>();
            if (queryDesc != null) noResultsBase.Add(queryDesc);
            if (totalDesc != null) noResultsBase.Add(totalDesc);
            var noResultsData = noResultsBase.ToArray();

            var primary = GetPrimaryType();
            DataDescription? resultDesc = null;

            if (HasOutputJoins)
            {
                var generatedType = TypeUtilities.FindTypeByFullName(GENERATED_TYPE_NAMESPACE + "." + GenerateOutputTypeName());
                if (generatedType != null)
                    resultDesc = new DataDescription(new DecisionsNativeType(generatedType), "EntityResults", true, false, false);
            }
            else if (primary != null)
            {
                resultDesc = new DataDescription(new DecisionsNativeType(primary), "EntityResults", true, false, false);
            }

            DataDescription[] resultData;
            if (resultDesc != null)
            {
                var resultBase = new List<DataDescription>();
                if (queryDesc != null) resultBase.Add(queryDesc);
                if (totalDesc != null) resultBase.Add(totalDesc);
                resultBase.Add(resultDesc);
                resultData = resultBase.ToArray();
            }
            else
            {
                resultData = noResultsData;
            }

            var outcomes = new List<OutcomeScenarioData>
            {
                new OutcomeScenarioData("No Results", noResultsData),
            };

            if (ShowPathForOneResult && resultDesc != null)
            {
                var singleDesc = new DataDescription(resultDesc.Type, "EntityResult", false, false, false);
                var singleBase = new List<DataDescription>();
                if (queryDesc != null) singleBase.Add(queryDesc);
                if (totalDesc != null) singleBase.Add(totalDesc);
                singleBase.Add(singleDesc);
                outcomes.Add(new OutcomeScenarioData("Result", singleBase.ToArray()));
            }

            outcomes.Add(new OutcomeScenarioData("Results", resultData));
            return outcomes.ToArray();
        }
    }

    // -------------------------------------------------------------------------
    // IValidationSource
    // -------------------------------------------------------------------------

    public ValidationIssue[] GetValidationIssues()
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(TypeName))
        {
            issues.Add(new ValidationIssue(this, "Primary type must be selected."));
            return issues.ToArray();
        }

        if (GetPrimaryType() == null)
        {
            issues.Add(new ValidationIssue(this, $"Type '{TypeName}' could not be resolved. Make sure the assembly is loaded."));
            return issues.ToArray();
        }

        // Validate filter tree
        foreach (var node in filterNodes ?? [])
            issues.AddRange(node.GetValidationIssues());

        // Paging requires a sort field
        if (usePaging && string.IsNullOrWhiteSpace(SortField))
            issues.Add(new ValidationIssue(this, "Sort Field is required when Use Paging is enabled. Sorting by the entity ID field is recommended for stable paging."));

        // Per-join validations
        foreach (var join in JoinDefinitions ?? [])
            issues.AddRange(join.GetValidationIssues());

        // Name collision detection across joins and primary type
        var mainTypeName = GetPrimaryType()?.Name;
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (mainTypeName != null) seenNames.Add(mainTypeName);
        foreach (var join in JoinDefinitions ?? [])
        {
            var name = join.EffectiveName;
            if (name == null) continue;
            if (!seenNames.Add(name))
                issues.Add(new ValidationIssue(this,
                    $"Name '{name}' is already used by another join or the primary type. " +
                    "Set a unique Output Alias to resolve the conflict."));
        }

        // Auto-generate output type when there are output joins and no other errors
        if (HasOutputJoins && issues.Count == 0)
        {
            var typeIssue = EnsureOutputType();
            if (typeIssue != null)
                issues.Add(typeIssue);
        }

        // Warn about joins that neither contribute to output nor require a match
        foreach (var join in JoinDefinitions ?? [])
        {
            if (!join.IncludeInOutput && !join.RequireMatch)
                issues.Add(new ValidationIssue(this,
                    $"Join '{join.EffectiveName ?? join.RelatedTypeName ?? "unnamed"}' has Include In Output = false and Require Match (Inner Join) = false. This join has no effect and can be removed.",
                    null,
                    BreakLevel.Warning));
        }

        return issues.ToArray();
    }



    // -------------------------------------------------------------------------
    // ISyncStep.Run
    // -------------------------------------------------------------------------

    public ResultData Run(StepStartData data)
    {
        var primaryType = GetPrimaryType()
            ?? throw new InvalidOperationException($"Primary type '{TypeName}' could not be resolved.");

        // 1. Build base SELECT statement
        var statement = BuildBaseStatement(primaryType, respectPermission: RespectPermission);
        statement.NoLock = CompositeSelectStatement.NoLockReadDefault && ReadUncommitted;
        statement.Distinct = true;

        // 2. Filter tree → WHERE clause
        var filterSql = BuildFilterNodesSql(filterNodes, data, primaryType, JoinDefinitions ?? []);
        if (filterSql != null)
            statement.WhereConditions.WhereConditions.Add(new RawSqlWhereCondition(filterSql));

        // 3. JoinDefinitions → EXISTS subqueries for filter-only joins
        foreach (var join in JoinDefinitions ?? [])
        {
            bool filterOnly = !HasOutputJoins || !join.IncludeInOutput || join.IsChainedJoin;
            if (!filterOnly) continue;
            if (!join.RequireMatch) continue;
            var joinExists = BuildExistsForJoin(join, data, primaryType, JoinDefinitions!);
            if (joinExists != null)
                statement.WhereConditions.WhereConditions.Add(joinExists);
        }

        // 4. FolderId filter for folder-aware entity types
        if (typeof(AbstractFolderEntity).IsAssignableFrom(primaryType)
            && data.Data.TryGetValue("FolderId", out var folderIdObj)
            && folderIdObj is string folderId && !string.IsNullOrEmpty(folderId))
        {
            ApplyFolderFilter(statement, primaryType, folderId);
        }

        // 5. Sort
        if (!string.IsNullOrWhiteSpace(SortField))
        {
            var colName = GetOrmColumnName(primaryType, SortField) ?? SortField;
            statement.OrderBy[$"main.{colName}"] = SortOrder;
        }

        // 6. Limit / Paging
        int? skip = null;
        int? fetchLimit = null;

        if (usePaging)
        {
            int pgSize = data.Data.TryGetValue("PageSize", out var psObj) && psObj != null
                ? Convert.ToInt32(psObj) : 0;
            int pgNumber = data.Data.TryGetValue("PageNumber", out var pnObj) && pnObj != null
                ? Convert.ToInt32(pnObj) : 1;
            if (pgSize > 0)
            {
                int page = pgNumber < 1 ? 1 : pgNumber;
                skip = (page - 1) * pgSize;
                fetchLimit = pgSize;
            }
        }
        else if (limitResults.HasValue && limitResults.Value > 0)
        {
            statement.MaxResultSetSize = limitResults.Value;
        }

        // 7. Query string for debugging
        string queryStr = ShowQueryInOutput ? statement.GetQueryWithParametersValue() : string.Empty;
        if (ShowQueryInOutput && skip.HasValue && fetchLimit.HasValue)
            queryStr += $" /* PAGING: LIMIT {fetchLimit.Value} OFFSET {skip.Value} */";

        // 8. Execute primary query
        // Note: CompositeSelectStatement.MaxResultSetSize combined with Distinct=true causes
        // the ORM to return 0 results (ORM internal issue). Paging is therefore applied
        // in-memory after a full fetch. For very large tables consider adding a dedicated
        // sort+limit index and using LimitResults as a hard safety cap instead.
        var orm = new DynamicORM();
        var allEntities = orm.Fetch(primaryType, statement, FetchDeletedEntities, !FastFetch) ?? [];

        // TotalResults is taken from the full ORM result BEFORE slicing. Using RunQueryForCount
        // would give the wrong number because the ORM applies soft-delete and permission filters
        // in-memory after the SQL runs — the SQL count sees all DB rows, the ORM fetch does not.
        long? totalCount = (skip.HasValue && fetchLimit.HasValue) ? (long)allEntities.Length : null;

        var primary = allEntities;
        if (skip.HasValue && fetchLimit.HasValue)
        {
            primary = primary.Length > skip.Value
                ? primary.Skip(skip.Value).Take(fetchLimit.Value).ToArray()
                : [];
        }

        if (EditCopy)
            for (int i = 0; i < primary.Length; i++)
                primary[i] = primary[i].GetEditCopy();

        if (primary.Length == 0)
            return NoResultsOutcome(data, queryStr, totalCount);

        // 9. Filter-only mode
        if (!HasOutputJoins)
            return BuildResultOutcome(primary, null, queryStr, totalCount);

        // 10. Batch-load related entities for output joins
        var tableResults = new Dictionary<string, IORMEntity[]>(StringComparer.OrdinalIgnoreCase)
        {
            [MAIN_ALIAS]       = primary,
            [primaryType.Name] = primary
        };

        // Maps joinEffectiveName → (sourceEntityId → fkColumnValue) for inverse FK fields
        // that have no C# property on the source entity (e.g. _Maintype on Subtype).
        var inverseFKLookup = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);

        foreach (var join in JoinDefinitions ?? [])
        {
            if (!join.IncludeInOutput || join.IsChainedJoin) continue;
            if (string.IsNullOrWhiteSpace(join.RelatedTypeName)) continue;
            var joinEffectiveName = join.EffectiveName;
            if (joinEffectiveName == null) continue;

            var relatedType = TypeUtilities.FindTypeByFullName(join.RelatedTypeName);
            if (relatedType == null) continue;

            if (!tableResults.TryGetValue(join.SourceTable ?? MAIN_ALIAS, out var sourceEntities))
                continue;

            var sourceType = ResolveSourceType(join.SourceTable, primaryType, JoinDefinitions!);

            var f2fMappings = (join.FieldMappings ?? [])
                .Where(m => m.JoinValueType == JoinSideType.Field
                         && m.SourceValueType == JoinSideType.Field
                         && m.Operator == JoinOperator.Equal
                         && !string.IsNullOrEmpty(m.JoinFieldName)
                         && !string.IsNullOrEmpty(m.SourceFieldName))
                .ToArray();

            if (f2fMappings.Length == 0)
            {
                tableResults[joinEffectiveName] = [];
                continue;
            }

            var primaryMapping = f2fMappings[0];
            var keyValues = sourceEntities
                .Select(e => GetOrmFieldValue(e, primaryMapping.SourceFieldName!))
                .Where(v => v != null).Distinct().ToArray<object?>();

            // Fallback for inverse FK columns (exist in DB but have no C# property on the entity).
            Dictionary<string, object?>? sourceFKMap = null;
            if (keyValues.Length == 0 && sourceType != null)
            {
                sourceFKMap = FetchColumnValuesMap(sourceType, primaryMapping.SourceFieldName!, sourceEntities);
                keyValues   = sourceFKMap.Values.Where(v => v != null).Distinct().ToArray<object?>();
                if (keyValues.Length > 0)
                    inverseFKLookup[joinEffectiveName] = sourceFKMap;
            }

            if (keyValues.Length == 0)
            {
                tableResults[joinEffectiveName] = [];
                continue;
            }

            var relatedStmt = BuildBaseStatement(relatedType);
            relatedStmt.NoLock = CompositeSelectStatement.NoLockReadDefault && ReadUncommitted;

            var primaryColName = GetOrmColumnName(relatedType, primaryMapping.JoinFieldName!) ?? primaryMapping.JoinFieldName;
            relatedStmt.WhereConditions.WhereConditions.Add(
                new FieldWhereCondition($"main.{primaryColName}", QueryMatchType.IsInList, keyValues));

            foreach (var fm in f2fMappings.Skip(1))
            {
                var addlKeys = sourceEntities
                    .Select(e => GetOrmFieldValue(e, fm.SourceFieldName!))
                    .Where(v => v != null).Distinct().ToArray();
                if (addlKeys.Length == 0) continue;
                var addlCol = GetOrmColumnName(relatedType, fm.JoinFieldName!) ?? fm.JoinFieldName;
                relatedStmt.WhereConditions.WhereConditions.Add(
                    new FieldWhereCondition($"main.{addlCol}", QueryMatchType.IsInList, addlKeys));
            }

            foreach (var m in join.FieldMappings ?? [])
            {
                if (m.JoinValueType == JoinSideType.Field
                    && m.SourceValueType == JoinSideType.Field
                    && m.Operator == JoinOperator.Equal) continue;
                foreach (var rawCond in BuildMappingConditions(new[] { m }, relatedType, null, "main", "", data))
                    relatedStmt.WhereConditions.WhereConditions.Add(new RawSqlWhereCondition(rawCond));
            }

            var related = orm.Fetch(relatedType, relatedStmt, FetchDeletedEntities, !FastFetch) ?? [];
            tableResults[joinEffectiveName] = related;

            if (join.RequireMatch)
            {
                primary = primary
                    .Where(p => related.Any(r =>
                        f2fMappings.All(m =>
                        {
                            var joinVal = GetOrmFieldValue(r, m.JoinFieldName!);
                            var srcVal  = GetOrmFieldValue(p, m.SourceFieldName!);
                            if (srcVal == null && sourceFKMap != null)
                            {
                                var pid = p.GetPrimaryKeyValue();
                                if (pid != null) sourceFKMap.TryGetValue(pid, out srcVal);
                            }
                            return Equals(joinVal, srcVal);
                        })))
                    .ToArray();
                tableResults[MAIN_ALIAS]       = primary;
                tableResults[primaryType.Name] = primary;
            }
        }

        if (primary.Length == 0)
            return NoResultsOutcome(data, queryStr, totalCount);

        // 11. Instantiate generated DTO array
        var generatedTypeName = GenerateOutputTypeName();
        var dtoType = TypeUtilities.FindTypeByFullName(GENERATED_TYPE_NAMESPACE + "." + generatedTypeName);
        if (dtoType == null)
            throw new InvalidOperationException(
                $"Generated output type '{GENERATED_TYPE_NAMESPACE}.{generatedTypeName}' could not be found. " +
                "Save the step to trigger type generation, then reload the flow.");

        var dtoArray = BuildDtoArray(dtoType, primary, tableResults, inverseFKLookup);
        return BuildResultOutcome(null, dtoArray, queryStr, totalCount);
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    private Type? GetPrimaryType() =>
        string.IsNullOrWhiteSpace(typeName) ? null : TypeUtilities.FindTypeByFullName(typeName);

    // FlowStep.outcomeScenariosCache is internal and only cleared via FlowStep.ClearCaches() (also internal).
    // CreateDataStep (same framework assembly) calls FlowStep?.ClearCaches() directly; we must use reflection.
    // Without this, FlowStep returns stale cached outcomes after ShowPathForOneResult / ShowQueryInOutput toggle.
    private void ClearFlowStepCache() =>
        FlowStep?.GetType()
            .GetMethod("ClearCaches", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.Invoke(FlowStep, null);

    // --- Statement builder ---------------------------------------------------

    private static CompositeSelectStatement BuildBaseStatement(Type entityType, bool respectPermission = false)
    {
        var tableName = ORMEntityAttribute.Of(entityType)?.GetTableName(entityType)
            ?? throw new InvalidOperationException($"No ORMEntityAttribute found on type '{entityType.FullName}'.");

        var stmt = new CompositeSelectStatement(new CompositeSelectStatement.TableDefinition(tableName, MAIN_ALIAS)
        {
            AllFields = true
        });

        // For folder-aware entity types, apply folder-level permission filtering when requested.
        // BuildStatementForEntitiesWithPermission adds JOINs to vwGetFolderPerms/entity_folder
        // and handles the admin bypass itself — admins get the plain statement unchanged.
        if (respectPermission && typeof(AbstractFolderEntity).IsAssignableFrom(entityType))
        {
            FolderService.BuildStatementForEntitiesWithPermission(
                null, FolderPermission.CanView,
                CompositeSelectStatement.JoinType.InnerJoin,
                stmt, tableName, MAIN_ALIAS,
                includeHidden: true);
        }

        return stmt;
    }

    // --- Filter tree SQL builder ---------------------------------------------

    /// <summary>
    /// Builds SQL for the top-level filter array. Multiple top-level nodes are ANDed together.
    /// </summary>
    private static string? BuildFilterNodesSql(
        FilterNode[]? nodes,
        StepStartData data,
        Type primaryType,
        JoinDefinition[] allJoins)
    {
        var parts = (nodes ?? [])
            .Select(n => BuildFilterNodeSql(n, data, primaryType, allJoins))
            .Where(s => s != null)
            .Select(s => s!)
            .ToList();

        if (parts.Count == 0) return null;
        if (parts.Count == 1) return parts[0];
        return $"({string.Join(" AND ", parts)})";
    }

    private static string? BuildFilterNodeSql(
        FilterNode node,
        StepStartData data,
        Type primaryType,
        JoinDefinition[] allJoins)
    {
        if (node.IsFilterNode)
            return BuildFilterLeafSql(node, data, primaryType, allJoins);

        // And / Or composite — recurse into children
        var childSqls = (node.Children ?? [])
            .Select(c => BuildFilterNodeSql(c, data, primaryType, allJoins))
            .Where(s => s != null)
            .Select(s => s!)
            .ToList();

        if (childSqls.Count == 0) return null;
        if (childSqls.Count == 1) return childSqls[0];

        string op = node.NodeType == FilterNodeType.And ? "AND" : "OR";
        return $"({string.Join($" {op} ", childSqls)})";
    }

    private static string? BuildFilterLeafSql(
        FilterNode node,
        StepStartData data,
        Type primaryType,
        JoinDefinition[] allJoins)
    {
        if (string.IsNullOrWhiteSpace(node.FieldName)) return null;

        var sourceTable = node.SourceTable;

        // Primary type — direct WHERE condition (or collection subquery)
        if (string.IsNullOrEmpty(sourceTable)
            || string.Equals(sourceTable, primaryType.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(sourceTable, MAIN_ALIAS, StringComparison.OrdinalIgnoreCase))
        {
            var fieldType = OrmFieldHelper.GetFieldNetType(primaryType.FullName, node.FieldName);
            if (fieldType != null && FilterNode.IsORMCollectionType(fieldType, out var elemType) && elemType != null)
                return BuildCollectionFilterSql(node, data, primaryType, MAIN_ALIAS, elemType);
            if (node.IsEntityRefField)
            {
                return node.FieldName?.Contains('.') == true
                    ? BuildEntityRefPathFilterSql(node, data, primaryType, MAIN_ALIAS)
                    : BuildEntityRefNullCheckSql(node, MAIN_ALIAS);
            }
            return BuildFilterFieldSql(node, data, primaryType, MAIN_ALIAS);
        }

        // Joined type — correlated EXISTS subquery
        var join = allJoins.FirstOrDefault(j =>
            string.Equals(j.EffectiveName, sourceTable, StringComparison.OrdinalIgnoreCase));
        if (join == null || string.IsNullOrWhiteSpace(join.RelatedTypeName)) return null;

        var relatedType = TypeUtilities.FindTypeByFullName(join.RelatedTypeName);
        if (relatedType == null) return null;

        var relatedTable = ORMEntityAttribute.Of(relatedType)?.GetTableName(relatedType);
        if (relatedTable == null) return null;

        var srcType = ResolveSourceType(join.SourceTable, primaryType, allJoins);
        string joinSrcAlias = string.IsNullOrEmpty(join.SourceTable)
            || join.SourceTable == MAIN_ALIAS
            || string.Equals(join.SourceTable, primaryType.Name, StringComparison.OrdinalIgnoreCase)
            ? MAIN_ALIAS : join.SourceTable;

        const string fltAlias = "flt_";
        var onConditions = BuildMappingConditions(join.FieldMappings ?? [], relatedType, srcType, fltAlias, joinSrcAlias, data);
        var filterCond   = BuildFilterFieldSql(node, data, relatedType, fltAlias);
        if (filterCond == null) return null;

        var allConds = onConditions.Append(filterCond);
        return $"EXISTS (SELECT 1 FROM {QT(relatedTable)} {fltAlias} WHERE {string.Join(" AND ", allConds)})";
    }

    // --- Collection filter SQL -----------------------------------------------

    private static readonly ConcurrentDictionary<(Type, Type), string?> FKFieldCache = new();

    /// <summary>
    /// Returns the FK column name in <paramref name="childType"/>'s table that references <paramref name="parentType"/>.
    /// Tries to read it from the ORMOneToManyRelationship field on the parent by instantiating the parent,
    /// then falls back to the naming convention "{ParentTypeName}Id" → snake_case.
    /// </summary>
    private static string? FindCollectionFKField(Type parentType, Type childType)
    {
        return FKFieldCache.GetOrAdd((parentType, childType), key =>
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var relGenericType = typeof(ORMOneToManyRelationship<>).MakeGenericType(key.Item2);

            for (var t = key.Item1; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var f in t.GetFields(flags))
                {
                    var ft = f.FieldType;
                    if (!ft.IsGenericType) continue;
                    if (ft.GetGenericTypeDefinition() != typeof(ORMOneToManyRelationship<>)) continue;
                    var elemArg = ft.GetGenericArguments()[0];
                    if (elemArg != key.Item2 && !elemArg.IsAssignableFrom(key.Item2)) continue;

                    try
                    {
                        if (key.Item1.GetConstructor(Type.EmptyTypes) == null) continue;
                        var inst = Activator.CreateInstance(key.Item1);
                        var rel  = f.GetValue(inst);
                        if (rel == null) continue;
                        var fieldNameProp = ft.GetProperty("FieldName", BindingFlags.Public | BindingFlags.Instance);
                        return fieldNameProp?.GetValue(rel) as string;
                    }
                    catch { /* fall through to convention */ }
                }
            }

            // Convention fallback: snake_case("{ParentTypeName}Id")
            try   { return ORMFieldAttribute.GetFieldNameFromPropertyName(key.Item1.Name + "Id"); }
            catch { return key.Item1.Name.ToLowerInvariant() + "_id"; }
        });
    }

    private static string? BuildCollectionFilterSql(
        FilterNode node,
        StepStartData data,
        Type parentType,
        string parentAlias,
        Type elementType)
    {
        var childAttr = ORMEntityAttribute.Of(elementType);
        if (childAttr == null) return null;

        string childTable = childAttr.GetTableName(elementType);
        string parentPK   = ORMEntityAttribute.Of(parentType)?.GetKeyName(parentType) ?? "id";
        string? fkField   = FindCollectionFKField(parentType, elementType);
        if (fkField == null) return null;

        string op = node.Operator ?? string.Empty;

        string qt  = QT(childTable);
        string qfk = Q(fkField);
        string qpk = Q(parentPK);

        if (op == FilterValueType.IsEmpty)
            return $"NOT EXISTS (SELECT 1 FROM {qt} c__ WHERE c__.{qfk} = {parentAlias}.{qpk})";
        if (op == FilterValueType.IsNotEmpty)
            return $"EXISTS (SELECT 1 FROM {qt} c__ WHERE c__.{qfk} = {parentAlias}.{qpk})";

        // Count operators — COUNT(*) compared to a threshold; no sub-field needed
        if (FilterNode.CountOperators.Contains(op))
        {
            string? countVal = BuildCollectionValueExpr(node, data, typeof(double));
            if (countVal == null) return null;
            string cmpOp = op switch
            {
                ListOperator.CountEq  => "=",
                ListOperator.CountNe  => "<>",
                ListOperator.CountGt  => ">",
                ListOperator.CountGte => ">=",
                ListOperator.CountLt  => "<",
                ListOperator.CountLte => "<=",
                _                     => "="
            };
            return $"(SELECT COUNT(*) FROM {qt} c__ WHERE c__.{qfk} = {parentAlias}.{qpk}) {cmpOp} {countVal}";
        }

        // Aggregate operators — SUM/AVG/MIN/MAX of a sub-field compared to a threshold
        if (op is ListOperator.SumOf or ListOperator.AvgOf or ListOperator.MinOf or ListOperator.MaxOf)
        {
            if (string.IsNullOrWhiteSpace(node.SubField) || string.IsNullOrWhiteSpace(node.SubFieldOperator))
                return null;
            var    rawAggCol = GetOrmColumnName(elementType, node.SubField!) ?? node.SubField;
            string qAggCol   = Q(rawAggCol!);
            var    aggFt     = OrmFieldHelper.GetFieldNetType(node.ElementTypeName, node.SubField);
            string? aggVal   = BuildCollectionValueExpr(node, data,
                op is ListOperator.SumOf or ListOperator.AvgOf ? typeof(double) : aggFt);
            if (aggVal == null) return null;
            string aggFunc = op switch
            {
                ListOperator.SumOf => "SUM",
                ListOperator.AvgOf => "AVG",
                ListOperator.MinOf => "MIN",
                ListOperator.MaxOf => "MAX",
                _                  => "SUM"
            };
            string aggOp = OperatorToSql(node.SubFieldOperator!);
            return $"(SELECT {aggFunc}(c__.{qAggCol}) FROM {qt} c__ WHERE c__.{qfk} = {parentAlias}.{qpk}) {aggOp} {aggVal}";
        }

        // Contains / DoesNotContain / FirstInList / LastInList — need SubField + SubFieldOperator
        if (string.IsNullOrWhiteSpace(node.SubField) || string.IsNullOrWhiteSpace(node.SubFieldOperator))
            return null;

        var    rawSubCol = GetOrmColumnName(elementType, node.SubField!) ?? node.SubField;
        string qSubCol   = Q(rawSubCol!);
        var    subFt     = OrmFieldHelper.GetFieldNetType(node.ElementTypeName, node.SubField);

        string? valueExpr = BuildCollectionValueExpr(node, data, subFt);
        if (valueExpr == null) return null;

        string subCond   = ApplyOperator($"c__.{qSubCol}", node.SubFieldOperator!, valueExpr);
        string baseWhere = $"c__.{qfk} = {parentAlias}.{qpk}";

        if (op == ListOperator.Contains)
            return $"EXISTS (SELECT 1 FROM {qt} c__ WHERE {baseWhere} AND {subCond})";
        if (op == ListOperator.DoesNotContain)
            return $"NOT EXISTS (SELECT 1 FROM {qt} c__ WHERE {baseWhere} AND {subCond})";

        // FirstInList / LastInList — scalar subquery ordered by child PK
        string qChildPK = Q(childAttr.GetKeyName(elementType));
        string sortDir  = op == ListOperator.FirstInList ? "ASC" : "DESC";
        string scalarOp = OperatorToSql(node.SubFieldOperator!);
        // SQL Server uses TOP 1 in the SELECT clause; PostgreSQL uses LIMIT 1 at the end.
        bool   isMsSql     = DynamicORM.DatabaseDriver.DatabaseType == DataBaseTypeEnum.MSSQL;
        string topClause   = isMsSql ? "TOP 1 " : "";
        string limitClause = isMsSql ? "" : " LIMIT 1";
        return $"(SELECT {topClause}c__.{qSubCol} FROM {qt} c__ WHERE c__.{qfk} = {parentAlias}.{qpk} ORDER BY c__.{qChildPK} {sortDir}{limitClause}) {scalarOp} {valueExpr}";
    }

    /// <summary>Bare entity ref field (no dot): only IS NULL / IS NOT NULL on the FK column.</summary>
    private static string? BuildEntityRefNullCheckSql(FilterNode node, string tableAlias)
    {
        var fieldExpr = $"{tableAlias}.{Q("_" + node.FieldName)}";
        if (node.Operator == FilterValueType.IsNull)    return $"{fieldExpr} IS NULL";
        if (node.Operator == FilterValueType.IsNotNull) return $"{fieldExpr} IS NOT NULL";
        return null;
    }

    /// <summary>
    /// Dot-path entity ref (e.g. "SubtypeData.NestedRef.SomeField"): strips the first segment as the FK,
    /// then delegates the rest to <see cref="BuildEntityRefPathSql"/> for recursive IN-subquery nesting.
    /// </summary>
    private static string? BuildEntityRefPathFilterSql(FilterNode node, StepStartData data, Type primaryType, string tableAlias)
    {
        int dot = node.FieldName!.IndexOf('.');
        string entityRefField = node.FieldName[..dot];
        string subPath        = node.FieldName[(dot + 1)..];

        var refType = OrmFieldHelper.GetFieldNetType(primaryType.FullName, entityRefField);
        if (refType == null) return null;

        var refAttr = ORMEntityAttribute.Of(refType);
        if (refAttr == null) return null;

        string refTable = refAttr.GetTableName(refType);
        string refPK    = refAttr.GetKeyName(refType);
        string fkCol    = "_" + entityRefField;

        string? innerWhere = BuildEntityRefPathSql(refType, subPath, node.Operator!, node, data, "erf__", 0);
        if (innerWhere == null) return null;

        return $"{tableAlias}.{Q(fkCol)} IN (SELECT erf__.{Q(refPK)} FROM {QT(refTable)} erf__ WHERE {innerWhere})";
    }

    private static string? BuildEntityRefSubValueExpr(FilterNode node, StepStartData data, Type? terminalFieldType)
    {
        if (node.ValueType == FilterValueType.StepInput)
        {
            if (string.IsNullOrWhiteSpace(node.InputName)) return null;
            data.Data.TryGetValue(node.InputName!, out var raw);
            if (raw == null) return null;
            return FormatFilterValue(raw, terminalFieldType);
        }
        return node.ValueType switch
        {
            FilterValueType.StringValue   => $"'{(node.StringValue ?? string.Empty).Replace("'", "''")}'",
            FilterValueType.NumberValue   => (node.NumberValue ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            FilterValueType.BoolValue     => SqlBool(node.BoolValue),
            FilterValueType.DateTimeValue => node.DateTimeValue == null ? null : $"'{node.DateTimeValue.Value:yyyy-MM-dd HH:mm:ss}'",
            FilterValueType.GuidValue     => SafeGuidLit(node.GuidValue),
            _ => null
        };
    }

    /// <summary>
    /// Recursively builds a WHERE fragment by walking a dot-separated sub-field path through entity ref joins.
    /// Single segment (no dot) → comparison on the terminal field.
    /// Multi-segment → FK IN (SELECT pk FROM NextTable WHERE [recurse]).
    /// </summary>
    private static string? BuildEntityRefPathSql(
        Type currentType,
        string dotPath,
        string terminalOperator,
        FilterNode node,
        StepStartData data,
        string alias,
        int depth)
    {
        int dot = dotPath.IndexOf('.');

        if (dot < 0)
        {
            // Terminal: compare the primitive field directly
            var subCol = GetOrmColumnName(currentType, dotPath) ?? dotPath;
            var expr   = $"{alias}.{Q(subCol)}";

            if (node.IsUnary)
                return node.ValueType switch
                {
                    FilterValueType.IsNull           => $"{expr} IS NULL",
                    FilterValueType.IsNotNull        => $"{expr} IS NOT NULL",
                    FilterValueType.IsEmpty          => $"{expr} = ''",
                    FilterValueType.IsNotEmpty       => $"{expr} <> ''",
                    FilterValueType.IsNullOrEmpty    => $"({expr} IS NULL OR {expr} = '')",
                    FilterValueType.IsNotNullOrEmpty => $"({expr} IS NOT NULL AND {expr} <> '')",
                    _ => null
                };

            var terminalFt    = OrmFieldHelper.GetFieldNetType(currentType.FullName, dotPath);
            string? valueExpr = BuildEntityRefSubValueExpr(node, data, terminalFt);
            if (valueExpr == null) return null;
            return ApplyOperator(expr, terminalOperator, valueExpr);
        }

        // Navigate one entity-ref hop and recurse
        string navField  = dotPath[..dot];
        string remainder = dotPath[(dot + 1)..];

        var nextType = OrmFieldHelper.GetFieldNetType(currentType.FullName, navField);
        if (nextType == null) return null;

        var nextAttr = ORMEntityAttribute.Of(nextType);
        if (nextAttr == null) return null;

        string fkCol     = "_" + navField;
        string nextTable = nextAttr.GetTableName(nextType);
        string nextPK    = nextAttr.GetKeyName(nextType);
        string nextAlias = $"er{depth}_";

        string? innerWhere = BuildEntityRefPathSql(nextType, remainder, terminalOperator, node, data, nextAlias, depth + 1);
        if (innerWhere == null) return null;

        return $"{alias}.{Q(fkCol)} IN (SELECT {nextAlias}.{Q(nextPK)} FROM {QT(nextTable)} {nextAlias} WHERE {innerWhere})";
    }

    private static Type? GetEntityRefSubFieldType(FilterNode node)
    {
        // node.FieldName is a full dot-path (e.g. "SubtypeData.NestedRef.SomeField")
        var primaryType = TypeUtilities.FindTypeByFullName(node.SelectedTypeFullName);
        return primaryType != null ? FilterNode.ResolveEntityRefPathTerminalType(primaryType, node.FieldName) : null;
    }

    private static string? BuildCollectionValueExpr(FilterNode node, StepStartData data, Type? stepInputFieldType)
    {
        if (node.ValueType == FilterValueType.StepInput)
        {
            if (string.IsNullOrWhiteSpace(node.InputName)) return null;
            data.Data.TryGetValue(node.InputName!, out var raw);
            if (raw == null) return null;
            return FormatFilterValue(raw, stepInputFieldType);
        }
        return node.ValueType switch
        {
            FilterValueType.StringValue   => $"'{(node.StringValue ?? string.Empty).Replace("'", "''")}'",
            FilterValueType.NumberValue   => (node.NumberValue ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            FilterValueType.BoolValue     => SqlBool(node.BoolValue),
            FilterValueType.DateTimeValue => node.DateTimeValue == null ? null : $"'{node.DateTimeValue.Value:yyyy-MM-dd HH:mm:ss}'",
            FilterValueType.GuidValue     => SafeGuidLit(node.GuidValue),
            _ => null
        };
    }

    private static string ApplyOperator(string fieldExpr, string op, string valueExpr) =>
        $"{fieldExpr} {OperatorToSql(op)} {valueExpr}";

    private static string OperatorToSql(string op) => op switch
    {
        JoinOperator.Equal          => "=",
        JoinOperator.NotEqual       => "<>",
        JoinOperator.GreaterThan    => ">",
        JoinOperator.GreaterOrEqual => ">=",
        JoinOperator.LessThan       => "<",
        JoinOperator.LessOrEqual    => "<=",
        // PostgreSQL: ILIKE for case-insensitive matching. SQL Server: LIKE is already
        // case-insensitive by default on most collations.
        JoinOperator.Like           => DynamicORM.DatabaseDriver.DatabaseType == DataBaseTypeEnum.MSSQL
                                           ? "LIKE" : "ILIKE",
        _                           => "="
    };

    // Q  — wraps a column/field name in the driver's quote characters (PostgreSQL: "name", SQL Server: [name]).
    // QT — wraps a table name. Both require isQuoted:true; the default (false) is a no-op passthrough.
    private static string Q(string name)  => DynamicORM.DatabaseDriver.GetSafeFieldName(name, isQuoted: true);
    private static string QT(string name) => DynamicORM.DatabaseDriver.GetSafeTableName(name, isQuoted: true);

    // SQL Server stores booleans as bit (0/1); PostgreSQL uses TRUE/FALSE.
    private static string SqlBool(bool value) =>
        DynamicORM.DatabaseDriver.DatabaseType == DataBaseTypeEnum.MSSQL
            ? (value ? "1" : "0")
            : (value ? "TRUE" : "FALSE");

    // Validates and embeds a GUID literal safely. Returns null if the value is not a valid GUID,
    // which callers treat as "skip this condition" — preventing SQL injection via GUID fields.
    private static string? SafeGuidLit(string? value) =>
        Guid.TryParse(value, out var g) ? $"'{g:D}'" : null;

    private static string? BuildFilterFieldSql(
        FilterNode node,
        StepStartData data,
        Type entityType,
        string tableAlias)
    {
        var colName   = GetOrmColumnName(entityType, node.FieldName!) ?? node.FieldName;
        var fieldExpr = $"{tableAlias}.{Q(colName!)}";

        if (node.IsUnary)
            return node.ValueType switch
            {
                FilterValueType.IsNull           => $"{fieldExpr} IS NULL",
                FilterValueType.IsNotNull        => $"{fieldExpr} IS NOT NULL",
                FilterValueType.IsEmpty          => $"{fieldExpr} = ''",
                FilterValueType.IsNotEmpty       => $"{fieldExpr} <> ''",
                FilterValueType.IsNullOrEmpty    => $"({fieldExpr} IS NULL OR {fieldExpr} = '')",
                FilterValueType.IsNotNullOrEmpty => $"({fieldExpr} IS NOT NULL AND {fieldExpr} <> '')",
                _ => null
            };

        string? valueExpr;
        if (node.ValueType == FilterValueType.StepInput)
        {
            if (string.IsNullOrWhiteSpace(node.InputName)) return null;
            data.Data.TryGetValue(node.InputName!, out var raw);
            if (raw == null) return null; // null input → skip this filter
            var ft = OrmFieldHelper.GetFieldNetType(node.SelectedTypeFullName, node.FieldName);
            valueExpr = FormatFilterValue(raw, ft);
        }
        else
        {
            valueExpr = node.ValueType switch
            {
                FilterValueType.StringValue =>
                    $"'{(node.StringValue ?? string.Empty).Replace("'", "''")}'",
                FilterValueType.NumberValue =>
                    (node.NumberValue ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
                FilterValueType.BoolValue =>
                    SqlBool(node.BoolValue),
                FilterValueType.DateTimeValue =>
                    node.DateTimeValue == null ? null : $"'{node.DateTimeValue.Value:yyyy-MM-dd HH:mm:ss}'",
                FilterValueType.GuidValue =>
                    SafeGuidLit(node.GuidValue),
                _ => null
            };
        }

        if (valueExpr == null) return null;

        return ApplyOperator(fieldExpr, node.Operator ?? JoinOperator.Equal, valueExpr);
    }

    private static string FormatFilterValue(object value, Type? fieldType)
    {
        if (fieldType == typeof(bool))
            return SqlBool(Convert.ToBoolean(value));
        if (OrmFieldHelper.IsNumericType(fieldType))
            return Convert.ToDouble(value).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (OrmFieldHelper.IsDateTimeType(fieldType) && value is DateTime dt)
            return $"'{dt:yyyy-MM-dd HH:mm:ss}'";
        if (fieldType == typeof(Guid))
            return $"'{value}'";
        return $"'{value.ToString()?.Replace("'", "''") ?? string.Empty}'";
    }

    // --- EXISTS builder (filter-only joins) ----------------------------------

    private static ExistsSubqueryCondition? BuildExistsForJoin(
        JoinDefinition join,
        StepStartData data,
        Type primaryType,
        JoinDefinition[] allJoins)
    {
        var mappings = join.FieldMappings;
        if (mappings == null || mappings.Length == 0) return null;
        if (string.IsNullOrWhiteSpace(join.RelatedTypeName)) return null;

        var relatedType = TypeUtilities.FindTypeByFullName(join.RelatedTypeName);
        if (relatedType == null) return null;

        var relatedTable = ORMEntityAttribute.Of(relatedType)?.GetTableName(relatedType);
        if (relatedTable == null) return null;

        var sourceType = ResolveSourceType(join.SourceTable, primaryType, allJoins);
        if (sourceType == null) return null;

        string sourceAlias = string.IsNullOrEmpty(join.SourceTable)
            || join.SourceTable == MAIN_ALIAS
            || string.Equals(join.SourceTable, primaryType.Name, StringComparison.OrdinalIgnoreCase)
            ? MAIN_ALIAS : join.SourceTable;

        var conditions = BuildMappingConditions(mappings, relatedType, sourceType, "chj_", sourceAlias, data);
        if (conditions.Count == 0) return null;

        return new ExistsSubqueryCondition($"{relatedTable} chj_", conditions, negate: false);
    }

    // --- Folder filter -------------------------------------------------------

    private static void ApplyFolderFilter(CompositeSelectStatement stmt, Type entityType, string folderId)
    {
        // Validate folderId is a real GUID before embedding in SQL — prevents injection
        // if a non-GUID value reaches this point via the FolderId step input.
        if (!Guid.TryParse(folderId, out var folderGuid)) return;
        string safeFolderId = folderGuid.ToString("D");

        bool noLock = CompositeSelectStatement.NoLockReadDefault;
        string noLockHint = noLock ? " (nolock) " : string.Empty;

        string fieldName = entityType == typeof(Folder) ? "folder_id" : "entity_folder_id";
        string subQuery  = $"select child_folder_id from folder_parent_xref{noLockHint} where folder_id = '{safeFolderId}' " +
                           $"union all select folder_id as child_folder_id from entity_folder{noLockHint} where folder_id = '{safeFolderId}'";

        stmt.JoinList.Add(new CompositeSelectStatement.JoinDefinition(
            CompositeSelectStatement.JoinType.InnerJoin,
            new CompositeSelectStatement.TableDefinition(subQuery, "xref_sub_folders") { IsSubQuery = true },
            new OrWhereSet(new AndWhereSet(new WhereCondition[]
            {
                new FieldWhereCondition($"main.{fieldName}", QueryMatchType.EqualsToOtherField, "xref_sub_folders.child_folder_id")
                {
                    FieldConverter = new KeyFieldConverter()
                }
            }))));
    }

    // --- DTO instantiation ---------------------------------------------------

    private Array BuildDtoArray(
        Type dtoType, IORMEntity[] primary,
        Dictionary<string, IORMEntity[]> tableResults,
        Dictionary<string, Dictionary<string, object?>>? inverseFKLookup = null)
    {
        var sourceProp = dtoType.GetProperty("Source");
        var joinProps  = (JoinDefinitions ?? [])
            .Where(j => j.IncludeInOutput && !j.IsChainedJoin && j.EffectiveName != null)
            .Select(j => (join: j, prop: dtoType.GetProperty(j.EffectiveName!)))
            .Where(x => x.prop != null)
            .ToArray();

        var result = Array.CreateInstance(dtoType, primary.Length);

        for (int i = 0; i < primary.Length; i++)
        {
            var dto = Activator.CreateInstance(dtoType)!;
            sourceProp?.SetValue(dto, primary[i]);

            foreach (var (join, prop) in joinProps)
            {
                if (!tableResults.TryGetValue(join.EffectiveName!, out var relatedAll)) continue;

                var relatedType = TypeUtilities.FindTypeByFullName(join.RelatedTypeName!);
                if (relatedType == null) continue;

                var f2fMappings = (join.FieldMappings ?? [])
                    .Where(m => m.JoinValueType == JoinSideType.Field
                             && m.SourceValueType == JoinSideType.Field
                             && m.Operator == JoinOperator.Equal
                             && !string.IsNullOrEmpty(m.JoinFieldName)
                             && !string.IsNullOrEmpty(m.SourceFieldName))
                    .ToArray();

                Dictionary<string, object?>? fkMapForJoin = null;
                inverseFKLookup?.TryGetValue(join.EffectiveName!, out fkMapForJoin);
                var matched = relatedAll
                    .Where(r => f2fMappings.Length == 0 || f2fMappings.All(m =>
                    {
                        var joinVal = GetOrmFieldValue(r, m.JoinFieldName!);
                        var srcVal  = GetOrmFieldValue(primary[i], m.SourceFieldName!);
                        if (srcVal == null && fkMapForJoin != null)
                        {
                            var pid = primary[i].GetPrimaryKeyValue();
                            if (pid != null) fkMapForJoin.TryGetValue(pid, out srcVal);
                        }
                        return Equals(joinVal, srcVal);
                    }))
                    .ToArray();

                var arr = Array.CreateInstance(relatedType, matched.Length);
                for (int j = 0; j < matched.Length; j++) arr.SetValue(matched[j], j);
                prop!.SetValue(dto, arr);
            }

            result.SetValue(dto, i);
        }

        return result;
    }

    // --- Result helpers ------------------------------------------------------

    private ResultData NoResultsOutcome(StepStartData data, string query = "", long? totalCount = null)
    {
        var dict = new Dictionary<string, object?>();
        if (ShowQueryInOutput) dict["Query"] = query;
        if (usePaging) dict["TotalResults"] = totalCount ?? 0L;
        return new ResultData("No Results", dict);
    }

    private ResultData BuildResultOutcome(IORMEntity[]? primaryResults, Array? dtoResults, string query, long? totalCount = null)
    {
        int count = dtoResults?.Length ?? primaryResults?.Length ?? 0;

        if (count == 0)
        {
            var emptyDict = new Dictionary<string, object?>();
            if (ShowQueryInOutput) emptyDict["Query"] = query;
            if (usePaging) emptyDict["TotalResults"] = totalCount ?? 0L;
            return new ResultData("No Results", emptyDict);
        }

        var dict = new Dictionary<string, object?>();
        if (ShowQueryInOutput) dict["Query"] = query;
        if (usePaging) dict["TotalResults"] = totalCount ?? (long)count;

        if (ShowPathForOneResult && count == 1)
        {
            dict["EntityResult"] = dtoResults != null ? dtoResults.GetValue(0) : primaryResults![0];
            return new ResultData("Result", dict);
        }

        dict["EntityResults"] = (object?)dtoResults ?? primaryResults;
        return new ResultData("Results", dict);
    }

    // --- Output type generation ----------------------------------------------

    private ValidationIssue? EnsureOutputType()
    {
        var primaryType = GetPrimaryType();
        if (primaryType == null) return null;

        var typeName = GenerateOutputTypeName();

        if (!string.IsNullOrWhiteSpace(lastGeneratedTypeName) && lastGeneratedTypeName != typeName)
            TryDeleteOrphanedType(lastGeneratedTypeName);

        string fullName = $"{GENERATED_TYPE_NAMESPACE}.{typeName}";
        var existing = DataStructureService.GetDataStructureByName(fullName) as DefinedDataStructure;

        if (existing == null)
        {
            try { CreateOutputType(primaryType); }
            catch (Exception ex)
            {
                return new ValidationIssue(this, $"Failed to generate output type '{typeName}': {ex.Message}");
            }
            lastGeneratedTypeName = typeName;
            return null;
        }

        var shapeIssue = ValidateOutputTypeShape(existing, primaryType);
        if (shapeIssue == null) lastGeneratedTypeName = typeName;
        return shapeIssue;
    }

    private static void TryDeleteOrphanedType(string typeName)
    {
        var existing = DataStructureService.GetDataStructureByName($"{GENERATED_TYPE_NAMESPACE}.{typeName}");
        if (existing == null) return;
        try { new DynamicORM().Delete(existing); }
        catch { /* Still referenced — leave it */ }
    }

    private void CreateOutputType(Type primaryType)
    {
        string folderId = Flow?.EntityFolderID ?? string.Empty;
        var typeName = GenerateOutputTypeName();

        var def = new DefinedDataStructure
        {
            EntityFolderID = folderId,
            EntityName = typeName,
            DataTypeName = typeName,
            DataTypeNameSpace = GENERATED_TYPE_NAMESPACE,
            GenerateEntityServiceFor = false,
            CanChangeServiceGeneration = false,
            StorageOption = StorageOption.NotDatabaseStored,
            IncludeDatabaseMarkups = false,
            GenerateDataType = true
        };

        var members = new List<DefinedDataTypeDataMember>
        {
            new DefinedDataTypeDataMember
            {
                RelationshipName = "Source",
                IsList = false,
                RelatedToDataType = primaryType.FullName
            }
        };

        foreach (var join in JoinDefinitions ?? [])
        {
            if (!join.IncludeInOutput || join.IsChainedJoin) continue;
            var memberName = join.EffectiveName;
            if (memberName == null || string.IsNullOrWhiteSpace(join.RelatedTypeName)) continue;
            var relatedType = TypeUtilities.FindTypeByFullName(join.RelatedTypeName);
            if (relatedType == null) continue;

            members.Add(new DefinedDataTypeDataMember
            {
                RelationshipName = memberName,
                IsList = true,
                RelatedToDataType = relatedType.FullName
            });
        }

        def.Children = members.ToArray();
        DataStructureService.AddOrUpdateDataStructure(def);
    }

    private ValidationIssue? ValidateOutputTypeShape(DefinedDataStructure existing, Type primaryType)
    {
        var mismatches = new List<string>();

        var sourceChild = existing.Children?.FirstOrDefault(c => c.RelationshipName == "Source");
        if (sourceChild == null)
            mismatches.Add("missing 'Source' property");
        else if (sourceChild.RelatedToDataType != primaryType.FullName)
            mismatches.Add($"'Source' has type '{sourceChild.RelatedToDataType}', expected '{primaryType.FullName}'");

        foreach (var join in JoinDefinitions ?? [])
        {
            if (!join.IncludeInOutput || join.IsChainedJoin) continue;
            var memberName = join.EffectiveName;
            if (memberName == null) continue;
            var child = existing.Children?.FirstOrDefault(c => c.RelationshipName == memberName);
            if (child == null)
            {
                mismatches.Add($"missing property '{memberName}'");
            }
            else
            {
                if (child.RelatedToDataType != join.RelatedTypeName)
                    mismatches.Add($"'{memberName}' type mismatch");
                if (!child.IsList)
                    mismatches.Add($"'{memberName}' must be a list");
            }
        }

        if (mismatches.Count == 0) return null;

        return new ValidationIssue(this,
            $"Output type '{GenerateOutputTypeName()}' does not match the current join configuration " +
            $"({string.Join("; ", mismatches)}). " +
            "Re-save the step to regenerate the type.");
    }

    // --- Output type helpers -------------------------------------------------

    private bool HasOutputJoins =>
        (joinDefinitions ?? []).Any(j => j.IncludeInOutput && !j.IsChainedJoin
            && !string.IsNullOrWhiteSpace(j.RelatedTypeName));

    private string GenerateOutputTypeName()
    {
        var primaryName = GetPrimaryType()?.Name ?? "Unknown";
        var joinNames = (joinDefinitions ?? [])
            .Where(j => j.IncludeInOutput && !j.IsChainedJoin && !string.IsNullOrWhiteSpace(j.RelatedTypeName))
            .Select(j =>
            {
                var tn = j.RelatedTypeName!;
                int dot = tn.LastIndexOf('.');
                return dot >= 0 ? tn[(dot + 1)..] : tn;
            })
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var fullName = joinNames.Count == 0
            ? primaryName
            : string.Join("_", new[] { primaryName }.Concat(joinNames));

        const int maxLength = 80;
        if (fullName.Length <= maxLength) return fullName;

        var hash = Convert.ToHexString(
            System.Security.Cryptography.MD5.HashData(
                System.Text.Encoding.UTF8.GetBytes(fullName)))[..8];
        return $"{fullName[..(maxLength - 9)]}_{hash}";
    }

    // --- ORM field helpers ---------------------------------------------------

    private static string[] GetOrmFieldNamesForType(Type? type)
    {
        if (type == null) return [];
        return DataUtilities.GetDataMemberAccessorsForClass(type, cache: true, publicOnly: false)
            .Where(a => ORMFieldAttribute.Of(a) != null
                     || a.Target?.GetCustomAttribute<ORMPrimaryKeyFieldAttribute>() != null)
            .Select(a => a.Name)
            .OrderBy(n => n)
            .ToArray();
    }

    private static string? GetOrmColumnName(Type entityType, string propertyName)
    {
        var accessor = DataUtilities
            .GetDataMemberAccessorsForClass(entityType, cache: true, publicOnly: false)
            .FirstOrDefault(a => string.Equals(a.Name, propertyName, StringComparison.OrdinalIgnoreCase));
        if (accessor == null) return null;

        var fieldAttr = ORMFieldAttribute.Of(accessor);
        if (fieldAttr != null) return fieldAttr.GetFieldName(accessor);

        var pkAcc  = ORMPrimaryKeyFieldAttribute.GetPrimaryKeyPropertyInfo(entityType);
        var pkAttr = ORMPrimaryKeyFieldAttribute.GetPrimaryKeyAttribute(entityType);
        if (pkAcc != null && string.Equals(pkAcc.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            return pkAttr?.GetFieldName(pkAcc);

        return propertyName;
    }

    /// <summary>
    /// Reads a raw DB column value for each entity in <paramref name="entities"/> and returns
    /// an entityId→value map. Used for inverse FK columns that exist in the table but have no
    /// corresponding C# property (e.g. _Maintype on the child side of ORMOneToManyRelationship).
    /// </summary>
    private static Dictionary<string, object?> FetchColumnValuesMap(
        Type entityType, string columnName, IORMEntity[] entities)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var tableAttr = ORMEntityAttribute.Of(entityType);
        if (tableAttr == null) return result;

        var tableName = tableAttr.GetTableName(entityType);
        var pkName    = tableAttr.GetKeyName(entityType) ?? "id";

        var idList = string.Join(",", entities
            .Select(e => e.GetPrimaryKeyValue())
            .Where(id => id != null)
            .Select(id => $"'{id!.Replace("'", "''")}'"));
        if (string.IsNullOrWhiteSpace(idList)) return result;

        var sql = $"SELECT {Q(pkName)}, {Q(columnName)} FROM {QT(tableName)} WHERE {Q(pkName)} IN ({idList})";
        try
        {
            var ds = new DynamicORM().RunQuery(sql);
            if (ds?.Tables.Count > 0)
            {
                foreach (System.Data.DataRow row in ds.Tables[0].Rows)
                {
                    var id  = row[0] == DBNull.Value ? null : row[0]?.ToString();
                    var val = row[1] == DBNull.Value ? null : row[1]?.ToString();
                    if (id != null) result[id] = val;
                }
            }
        }
        catch { /* Column may not exist — silently return empty map */ }
        return result;
    }

    private static object? GetOrmFieldValue(IORMEntity entity, string propertyName)
    {
        var accessor = DataUtilities
            .GetDataMemberAccessorsForClass(entity.GetType(), cache: true, publicOnly: false)
            .FirstOrDefault(a => string.Equals(a.Name, propertyName, StringComparison.OrdinalIgnoreCase));
        if (accessor == null) return null;
        var value = accessor.GetValue(entity);
        // ORMRelationship<T> stores a FK string in the DB column; return just the FK ID.
        if (value is IORMRelationship rel)
            return rel.GetType().GetProperty("MyPk")?.GetValue(rel) as string;
        return value;
    }

    // --- Field mapping SQL builders -----------------------------------------

    private static List<string> BuildMappingConditions(
        FieldMapping[] mappings,
        Type joinType,
        Type? sourceType,
        string joinAlias,
        string sourceAlias,
        StepStartData? stepData = null)
    {
        var conditions = new List<string>();
        foreach (var m in mappings)
        {
            if (m.JoinIsUnary)
            {
                var expr = ResolveMappingExprField(m.JoinFieldName, joinType, joinAlias);
                if (expr != null) conditions.Add(UnarySideToSql(m.JoinValueType, expr));
                continue;
            }
            if (m.SourceIsUnary)
            {
                var expr = ResolveMappingExprField(m.SourceFieldName, sourceType, sourceAlias);
                if (expr != null) conditions.Add(UnarySideToSql(m.SourceValueType, expr));
                continue;
            }

            string? leftExpr = m.JoinUseStepInput && FieldMapping.IsLiteralType(m.JoinValueType) && stepData != null
                ? ResolveStepInputExpr(m.JoinValueType, m.JoinInputName, stepData)
                : ResolveMappingExpr(m.JoinValueType, m.JoinFieldName, m.JoinStringValue, m.JoinNumberValue, m.JoinBoolValue, m.JoinDateTimeValue, m.JoinGuidValue, joinType, joinAlias);

            string? rightExpr = m.SourceUseStepInput && FieldMapping.IsLiteralType(m.SourceValueType) && stepData != null
                ? ResolveStepInputExpr(m.SourceValueType, m.SourceInputName, stepData)
                : ResolveMappingExpr(m.SourceValueType, m.SourceFieldName, m.SourceStringValue, m.SourceNumberValue, m.SourceBoolValue, m.SourceDateTimeValue, m.SourceGuidValue, sourceType, sourceAlias);

            if (leftExpr == null || rightExpr == null) continue;

            conditions.Add(ApplyOperator(leftExpr, m.Operator ?? JoinOperator.Equal, rightExpr));
        }
        return conditions;
    }

    private static string? ResolveMappingExprField(string? fieldName, Type? entityType, string alias)
    {
        if (string.IsNullOrWhiteSpace(fieldName) || entityType == null) return null;
        var col = GetOrmColumnName(entityType, fieldName) ?? fieldName;
        return $"{alias}.{Q(col!)}";
    }

    private static string? ResolveMappingExpr(
        string? sideType, string? fieldName, string? stringValue, double? numberValue,
        bool boolValue, DateTime? dateTimeValue, string? guidValue, Type? entityType, string alias)
    {
        return sideType switch
        {
            JoinSideType.Field         => ResolveMappingExprField(fieldName, entityType, alias),
            JoinSideType.StringValue   => $"'{(stringValue ?? string.Empty).Replace("'", "''")}'",
            JoinSideType.NumberValue   => (numberValue ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            JoinSideType.BoolValue     => SqlBool(boolValue),
            JoinSideType.DateTimeValue => dateTimeValue == null ? null : $"'{dateTimeValue.Value:yyyy-MM-dd HH:mm:ss}'",
            JoinSideType.GuidValue     => SafeGuidLit(guidValue),
            _ => null
        };
    }

    private static string UnarySideToSql(string? sideType, string expr) => sideType switch
    {
        JoinSideType.IsNull           => $"{expr} IS NULL",
        JoinSideType.IsNotNull        => $"{expr} IS NOT NULL",
        JoinSideType.IsEmpty          => $"{expr} = ''",
        JoinSideType.IsNotEmpty       => $"{expr} <> ''",
        JoinSideType.IsNullOrEmpty    => $"({expr} IS NULL OR {expr} = '')",
        JoinSideType.IsNotNullOrEmpty => $"({expr} IS NOT NULL AND {expr} <> '')",
        _                             => $"{expr} IS NULL"
    };

    private static Type InputTypeForSideType(string? sideType) => sideType switch
    {
        JoinSideType.NumberValue   => typeof(double),
        JoinSideType.BoolValue     => typeof(bool),
        JoinSideType.DateTimeValue => typeof(DateTime),
        _                          => typeof(string)
    };

    private static Type FilterInputType(Type? fieldType)
    {
        if (fieldType == typeof(bool))               return typeof(bool);
        if (OrmFieldHelper.IsNumericType(fieldType))     return typeof(double);
        if (OrmFieldHelper.IsDateTimeType(fieldType))    return typeof(DateTime);
        return typeof(string);
    }

    private static string? ResolveStepInputExpr(string? sideType, string? inputName, StepStartData stepData)
    {
        if (string.IsNullOrWhiteSpace(inputName)) return null;
        stepData.Data.TryGetValue(inputName, out var rawValue);
        if (rawValue == null) return null;
        return sideType switch
        {
            JoinSideType.StringValue   => $"'{rawValue.ToString()?.Replace("'", "''") ?? string.Empty}'",
            JoinSideType.NumberValue   => Convert.ToDouble(rawValue).ToString(System.Globalization.CultureInfo.InvariantCulture),
            JoinSideType.BoolValue     => SqlBool(Convert.ToBoolean(rawValue)),
            JoinSideType.DateTimeValue => rawValue is DateTime dt ? $"'{dt:yyyy-MM-dd HH:mm:ss}'" : null,
            JoinSideType.GuidValue     => SafeGuidLit(rawValue?.ToString()),
            _ => null
        };
    }

    // --- Sub-object event wiring ---------------------------------------------

    // Properties on FilterNode/FieldMapping whose changes require InputData to be refreshed.
    private static readonly HashSet<string> InputDataTriggers = new(StringComparer.Ordinal)
    {
        nameof(FilterNode.InputName),
        nameof(FilterNode.ShowStepInput),   // fires when ValueType switches to/from StepInput
        nameof(FieldMapping.JoinInputName),
        nameof(FieldMapping.SourceInputName),
        nameof(FieldMapping.ShowJoinInputName),   // fires when JoinUseStepInput toggles
        nameof(FieldMapping.ShowSourceInputName), // fires when SourceUseStepInput toggles
    };

    private void WireFilterNodeEvents(FilterNode[]? nodes, bool attach)
    {
        foreach (var node in nodes ?? [])
        {
            if (attach) node.PropertyChanged += OnSubObjectPropertyChanged;
            else        node.PropertyChanged -= OnSubObjectPropertyChanged;
            WireFilterNodeEvents(node.Children, attach);
        }
    }

    private void WireJoinMappingEvents(JoinDefinition[]? joins, bool attach)
    {
        foreach (var join in joins ?? [])
        foreach (var mapping in join.FieldMappings ?? [])
        {
            if (attach) mapping.PropertyChanged += OnSubObjectPropertyChanged;
            else        mapping.PropertyChanged -= OnSubObjectPropertyChanged;
        }
    }

    private void OnSubObjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FilterNode.Children))
        {
            // Children array replaced — rewire the full tree so new children are subscribed.
            WireFilterNodeEvents(filterNodes, attach: false);
            WireFilterNodeEvents(filterNodes, attach: true);
            OnPropertyChanged(nameof(InputData));
        }
        else if (InputDataTriggers.Contains(e.PropertyName ?? ""))
        {
            OnPropertyChanged(nameof(InputData));
        }
    }

    // --- Context updaters ----------------------------------------------------

    private void UpdateJoinContext()
    {
        if (joinDefinitions == null || joinDefinitions.Length == 0) return;

        var mainType = GetPrimaryType();
        if (mainType == null) return;

        string mainOption    = mainType.Name;
        var priorAliases     = new List<string>();

        for (int i = 0; i < joinDefinitions.Length; i++)
        {
            var jd = joinDefinitions[i];

            var sources = new List<string> { mainOption };
            sources.AddRange(priorAliases);
            jd.AvailableSources = sources.ToArray();

            if (string.IsNullOrEmpty(jd.SourceTable) || jd.SourceTable == MAIN_ALIAS)
                jd.SourceTable = mainOption;

            jd.MainTypeName   = mainOption;
            var sourceType    = ResolveSourceType(jd.SourceTable, mainType, joinDefinitions);
            jd.SourceTypeName = sourceType?.FullName;

            var effectiveName = jd.EffectiveName;
            if (effectiveName != null)
                priorAliases.Add(effectiveName);
        }
    }

    private void UpdateFilterContext()
    {
        if (filterNodes == null || filterNodes.Length == 0) return;

        var mainType = GetPrimaryType();
        if (mainType == null) return;

        var tableNames     = new List<string> { mainType.Name };
        var tableTypeNames = new List<string> { mainType.FullName! };

        foreach (var join in joinDefinitions ?? [])
        {
            var name = join.EffectiveName;
            if (name == null || string.IsNullOrWhiteSpace(join.RelatedTypeName)) continue;
            tableNames.Add(name);
            tableTypeNames.Add(join.RelatedTypeName);
        }

        var tablesArr    = tableNames.ToArray();
        var typeNamesArr = tableTypeNames.ToArray();

        foreach (var node in filterNodes)
            node.PushContext(tablesArr, typeNamesArr);
    }

    // --- Source table resolution for chained JOINs --------------------------

    private static Type? ResolveSourceType(string? sourceTable, Type? primaryType, JoinDefinition[] all)
    {
        if (string.IsNullOrEmpty(sourceTable) || sourceTable == MAIN_ALIAS)
            return primaryType;
        if (primaryType != null && string.Equals(sourceTable, primaryType.Name, StringComparison.OrdinalIgnoreCase))
            return primaryType;
        var match = all.FirstOrDefault(j =>
            string.Equals(j.EffectiveName, sourceTable, StringComparison.OrdinalIgnoreCase));
        return match == null ? null : TypeUtilities.FindTypeByFullName(match.RelatedTypeName!);
    }
}
