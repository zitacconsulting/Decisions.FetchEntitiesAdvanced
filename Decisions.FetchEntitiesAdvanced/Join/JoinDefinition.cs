using System.Runtime.Serialization;
using DecisionsFramework;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.Properties.Attributes;

namespace Decisions.FetchEntitiesAdvanced.Join;

/// <summary>
/// Defines an explicit JOIN between the primary entity (or a previously joined type) and
/// any other database-backed ORM entity type.
///
/// Join conditions are expressed as a list of <see cref="FieldMappings"/>.
/// Each mapping can compare field-to-field, field-to-literal, field-to-expression,
/// or use IS NULL / IS NOT NULL operators.
///
/// When <c>IncludeInOutput = false</c> (or when <c>IsChainedJoin</c> is true):
///   joins with <c>RequireMatch = true</c> are translated into correlated EXISTS subqueries.
/// When <c>IncludeInOutput = true</c>:
///   the join is executed as a batch query and the results appear on the output DTO.
/// </summary>
[Writable]
public class JoinDefinition : IValidationSource
{
    // -----------------------------------------------------------------------
    // Backing fields
    // -----------------------------------------------------------------------

    [WritableValue] private string[]? availableSources;
    [WritableValue] private string? relatedTypeName;
    [WritableValue] private string? sourceTypeName;
    [WritableValue] private string? mainTypeName;
    [WritableValue] private FieldMapping[]? fieldMappings;
    [WritableValue] private bool includeInOutput = true;

    // -----------------------------------------------------------------------
    // Visible properties
    // -----------------------------------------------------------------------

    /// <summary>
    /// Which table/alias supplies the left side of the join.
    /// Dropdown shows the primary type's short name (e.g. "Account") plus output aliases
    /// of preceding joins for chained joins.
    /// </summary>
    [WritableValue]
    [PropertyClassification(0, "Source", new[] { "Join" })]
    [SelectStringEditor("AvailableSources", SelectStringEditorType.DropdownList, true)]
    public string? SourceTable { get; set; }

    /// <summary>
    /// Fully qualified name of the related ORM entity type to join.
    /// Changing this immediately updates the Join Field dropdowns in all child mappings.
    /// </summary>
    [PropertyClassification(1, "Join Datatype", new[] { "Join" })]
    [TypePickerEditor(TypePick.ORMEntity)]
    public string? RelatedTypeName
    {
        get => relatedTypeName;
        set
        {
            relatedTypeName = value;
            foreach (var m in fieldMappings ?? [])
                m.RelatedTypeName = value;
        }
    }

    /// <summary>
    /// One or more join conditions (ANDed together in the ON clause).
    /// When a new mapping is added it automatically receives the current Join Datatype
    /// and Source type context so its dropdowns are populated.
    /// </summary>
    [PropertyClassification(2, "Join Conditions", new[] { "Join" })]
    public FieldMapping[]? FieldMappings
    {
        get => fieldMappings;
        set
        {
            fieldMappings = value;
            foreach (var m in fieldMappings ?? [])
            {
                m.RelatedTypeName = relatedTypeName;
                m.SourceTypeName  = sourceTypeName;
            }
        }
    }

    /// <summary>
    /// Serves two purposes:
    /// 1. Property name on the generated output type when <c>IncludeInOutput = true</c>.
    /// 2. Reference key used as <c>Source</c> in subsequent join definitions (chaining).
    /// Must be a valid C# identifier.
    /// </summary>
    [WritableValue]
    [PropertyClassification(3, "Output Alias", new[] { "Join" })]
    public string? OutputAlias { get; set; }

    /// <summary>
    /// <c>true</c>  → source rows with no matching related rows are excluded (INNER semantics).<br/>
    /// <c>false</c> → source rows with no matches are kept (LEFT semantics).
    /// When <c>IncludeInOutput = false</c> and this is <c>false</c>, the join has no effect.
    /// </summary>
    [WritableValue]
    [PropertyClassification(5, "Require Match (Inner Join)", new[] { "Join" })]
    public bool RequireMatch { get; set; }

    /// <summary>
    /// Whether the joined data should appear on the output DTO.
    /// Automatically hidden and treated as <c>false</c> when this is a chained join
    /// (Source is not the primary entity).
    /// </summary>
    [PropertyClassification(6, "Include In Output", new[] { "Join" })]
    [BooleanPropertyHidden(nameof(IsChainedJoin), true)]
    public bool IncludeInOutput
    {
        get => includeInOutput;
        set => includeInOutput = value;
    }

    // -----------------------------------------------------------------------
    // Hidden context — set by the parent step via UpdateJoinContext()
    // -----------------------------------------------------------------------

    /// <summary>
    /// Available options for the Source dropdown.
    /// First entry is the primary entity's short type name; subsequent entries are
    /// output aliases of preceding joins.
    /// </summary>
    [PropertyHidden]
    public string[] AvailableSources
    {
        get => availableSources ?? [];
        set => availableSources = value;
    }

    /// <summary>
    /// Full type name of the source entity — drives Source Field dropdowns in child mappings.
    /// Setting this propagates immediately to all existing <see cref="FieldMappings"/>.
    /// </summary>
    [PropertyHidden]
    public string? SourceTypeName
    {
        get => sourceTypeName;
        set
        {
            sourceTypeName = value;
            foreach (var m in fieldMappings ?? [])
                m.SourceTypeName = value;
        }
    }

    /// <summary>
    /// Short name of the primary entity type (e.g. "Account").
    /// Used to determine <see cref="IsChainedJoin"/>.
    /// </summary>
    [PropertyHidden]
    public string? MainTypeName
    {
        get => mainTypeName;
        set => mainTypeName = value;
    }

    // -----------------------------------------------------------------------
    // Display
    // -----------------------------------------------------------------------

    public ValidationIssue[] GetValidationIssues()
    {
        var issues = new List<ValidationIssue>();

        if (string.IsNullOrWhiteSpace(relatedTypeName))
            issues.Add(new ValidationIssue(this, "Join Datatype must be selected."));

        // Source may become invalid when the primary entity type changes.
        if (!string.IsNullOrWhiteSpace(SourceTable)
            && availableSources != null && availableSources.Length > 0
            && !availableSources.Contains(SourceTable, StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(new ValidationIssue(this,
                $"Source '{SourceTable}' is no longer available — the primary type may have changed. Re-select the Source."));
        }

        if (fieldMappings == null || fieldMappings.Length == 0)
            issues.Add(new ValidationIssue(this, "At least one Join Condition is required."));

        foreach (var mapping in fieldMappings ?? [])
            issues.AddRange(mapping.GetValidationIssues());

        return issues.ToArray();
    }

    // -----------------------------------------------------------------------
    // Display / computed state
    // -----------------------------------------------------------------------

    /// <summary>
    /// The name used to identify this join in the output DTO, Source dropdowns, and table lookups.
    /// Returns <see cref="OutputAlias"/> when set; otherwise the short name of <see cref="RelatedTypeName"/>.
    /// Returns <c>null</c> when neither is available (join not yet configured).
    /// </summary>
    [IgnoreDataMember][PropertyHidden]
    public string? EffectiveName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(OutputAlias)) return OutputAlias;
            if (!string.IsNullOrWhiteSpace(relatedTypeName))
            {
                int dot = relatedTypeName!.LastIndexOf('.');
                return dot >= 0 ? relatedTypeName[(dot + 1)..] : relatedTypeName;
            }
            return null;
        }
    }

    public override string ToString() => EffectiveName ?? "Join";

    /// <summary>
    /// <c>true</c> when the Source is a preceding join's alias rather than the primary entity.
    /// Chained joins are used for filtering only and cannot be included in output.
    /// </summary>
    [IgnoreDataMember][PropertyHidden]
    public bool IsChainedJoin =>
        !string.IsNullOrEmpty(SourceTable)
        && !string.Equals(SourceTable, "main", StringComparison.OrdinalIgnoreCase)
        && (mainTypeName == null || !string.Equals(SourceTable, mainTypeName, StringComparison.OrdinalIgnoreCase));
}
