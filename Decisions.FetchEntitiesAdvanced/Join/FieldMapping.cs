using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using DecisionsFramework;
using DecisionsFramework.Data.ORMapper;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.Properties.Attributes;
using DecisionsFramework.Utilities;
using DecisionsFramework.Utilities.Data;

namespace Decisions.FetchEntitiesAdvanced.Join;

[Writable]
public class FieldMapping : IValidationSource, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void Notify(params string[] names)
    {
        if (PropertyChanged == null) return;
        foreach (var n in names)
            PropertyChanged.Invoke(this, new PropertyChangedEventArgs(n));
    }

    // -----------------------------------------------------------------------
    // Static tables
    // -----------------------------------------------------------------------

    private static readonly string[] AllOperators =
    [
        JoinOperator.Equal,     JoinOperator.NotEqual,
        JoinOperator.GreaterThan, JoinOperator.GreaterOrEqual,
        JoinOperator.LessThan,  JoinOperator.LessOrEqual,
        JoinOperator.Like
    ];

    private static readonly HashSet<string> AllOperatorsSet =
        new(AllOperators, StringComparer.Ordinal);

    private static readonly string[] AllSideTypes =
    [
        JoinSideType.Field,
        JoinSideType.StringValue,   JoinSideType.NumberValue,
        JoinSideType.BoolValue,     JoinSideType.DateTimeValue,
        JoinSideType.GuidValue,
        JoinSideType.IsNull,        JoinSideType.IsNotNull,
        JoinSideType.IsEmpty,       JoinSideType.IsNotEmpty,
        JoinSideType.IsNullOrEmpty, JoinSideType.IsNotNullOrEmpty
    ];

    private static readonly HashSet<string> AllUnaryTypes =
        new(StringComparer.Ordinal)
        {
            JoinSideType.IsNull,    JoinSideType.IsNotNull,
            JoinSideType.IsEmpty,   JoinSideType.IsNotEmpty,
            JoinSideType.IsNullOrEmpty, JoinSideType.IsNotNullOrEmpty
        };

    // -----------------------------------------------------------------------
    // Backing fields
    // -----------------------------------------------------------------------

    [WritableValue] private string? relatedTypeName;
    [WritableValue] private string? sourceTypeName;

    [WritableValue] private string? joinSideTypeValue   = JoinSideType.Field;
    [WritableValue] private string? sourceSideTypeValue = JoinSideType.Field;
    [WritableValue] private string? operatorValue       = JoinOperator.Equal;

    [WritableValue] private string? joinFieldName;
    [WritableValue] private string? sourceFieldName;

    [WritableValue] private bool    joinUseStepInput;
    [WritableValue] private string? joinInputName;
    [WritableValue] private bool    sourceUseStepInput;
    [WritableValue] private string? sourceInputName;

    // -----------------------------------------------------------------------
    // 1 Join section
    // -----------------------------------------------------------------------

    [WritableValue]
    [PropertyClassification(0, "Join Value Type", new[] { "1 Join" })]
    [SelectStringEditor("AvailableJoinSideTypes", SelectStringEditorType.DropdownList, false)]
    [BooleanPropertyHidden(nameof(SourceIsUnary), true)]
    public string? JoinValueType
    {
        get => joinSideTypeValue;
        set
        {
            joinSideTypeValue = value;
            Notify(
                nameof(JoinIsUnary),       nameof(IsBinaryCondition),
                nameof(JoinFieldVisible),
                nameof(JoinIsStringValue), nameof(JoinIsNumberValue), nameof(JoinIsBoolValue),
                nameof(JoinIsDateTimeValue), nameof(JoinIsGuidValue),
                nameof(ShowJoinUseStepInput), nameof(ShowJoinInputName),
                nameof(SourceFieldVisible),
                nameof(ShowSourceStringValue), nameof(ShowSourceNumberValue), nameof(ShowSourceBoolValue),
                nameof(ShowSourceDateTimeValue), nameof(ShowSourceGuidValue),
                nameof(ShowSourceUseStepInput), nameof(ShowSourceInputName),
                nameof(AvailableSourceSideTypes), nameof(SourceValueType),
                nameof(AvailableSourceFields),    nameof(SourceFieldName),
                nameof(AvailableOperators),       nameof(Operator));
        }
    }

    [WritableValue]
    [PropertyClassification(1, "Join Field", new[] { "1 Join" })]
    [SelectStringEditor("AvailableJoinFields", SelectStringEditorType.DropdownList, true)]
    [BooleanPropertyHidden(nameof(JoinFieldVisible), false)]
    public string? JoinFieldName
    {
        get => joinFieldName;
        set
        {
            joinFieldName = value;
            Notify(
                nameof(AvailableSourceSideTypes), nameof(SourceValueType),
                nameof(AvailableSourceFields),    nameof(SourceFieldName),
                nameof(AvailableOperators),       nameof(Operator));
        }
    }

    [WritableValue]
    [PropertyClassification(2, "Use Step Input", new[] { "1 Join" })]
    [BooleanPropertyHidden(nameof(ShowJoinUseStepInput), false)]
    public bool JoinUseStepInput
    {
        get => joinUseStepInput;
        set
        {
            joinUseStepInput = value;
            Notify(
                nameof(ShowJoinInputName),
                nameof(JoinIsStringValue), nameof(JoinIsNumberValue), nameof(JoinIsBoolValue),
                nameof(JoinIsDateTimeValue), nameof(JoinIsGuidValue));
        }
    }

    [WritableValue]
    [PropertyClassification(3, "Input Name", new[] { "1 Join" })]
    [BooleanPropertyHidden(nameof(ShowJoinInputName), false)]
    public string? JoinInputName
    {
        get => joinInputName;
        set => joinInputName = value;
    }

    [WritableValue]
    [PropertyClassification(4, "String Value", new[] { "1 Join" })]
    [BooleanPropertyHidden(nameof(JoinIsStringValue), false)]
    public string? JoinStringValue { get; set; }

    [WritableValue]
    [PropertyClassification(5, "Number Value", new[] { "1 Join" })]
    [BooleanPropertyHidden(nameof(JoinIsNumberValue), false)]
    public double? JoinNumberValue { get; set; }

    [WritableValue]
    [PropertyClassification(6, "Bool Value", new[] { "1 Join" })]
    [BooleanPropertyHidden(nameof(JoinIsBoolValue), false)]
    public bool JoinBoolValue { get; set; }

    [WritableValue]
    [PropertyClassification(7, "Date/Time Value", new[] { "1 Join" })]
    [BooleanPropertyHidden(nameof(JoinIsDateTimeValue), false)]
    public DateTime? JoinDateTimeValue { get; set; }

    [WritableValue]
    [PropertyClassification(8, "Guid Value", new[] { "1 Join" })]
    [BooleanPropertyHidden(nameof(JoinIsGuidValue), false)]
    public string? JoinGuidValue { get; set; }

    // -----------------------------------------------------------------------
    // 2 Condition section
    // -----------------------------------------------------------------------

    [WritableValue]
    [PropertyClassification(10, "Operator", new[] { "2 Condition" })]
    [SelectStringEditor("AvailableOperators", SelectStringEditorType.DropdownList, false)]
    [BooleanPropertyHidden(nameof(IsBinaryCondition), false)]
    public string? Operator
    {
        get => operatorValue;
        set
        {
            operatorValue = value;
            Notify(
                nameof(AvailableJoinSideTypes),   nameof(JoinValueType),
                nameof(AvailableSourceSideTypes), nameof(SourceValueType),
                nameof(AvailableJoinFields),      nameof(JoinFieldName),
                nameof(AvailableSourceFields),    nameof(SourceFieldName));
        }
    }

    // -----------------------------------------------------------------------
    // 3 Source section
    // -----------------------------------------------------------------------

    [WritableValue]
    [PropertyClassification(20, "Source Value Type", new[] { "3 Source" })]
    [SelectStringEditor("AvailableSourceSideTypes", SelectStringEditorType.DropdownList, false)]
    [BooleanPropertyHidden(nameof(JoinIsUnary), true)]
    public string? SourceValueType
    {
        get => sourceSideTypeValue;
        set
        {
            sourceSideTypeValue = value;
            Notify(
                nameof(SourceIsUnary),      nameof(IsBinaryCondition),
                nameof(SourceFieldVisible),
                nameof(ShowSourceStringValue), nameof(ShowSourceNumberValue), nameof(ShowSourceBoolValue),
                nameof(ShowSourceDateTimeValue), nameof(ShowSourceGuidValue),
                nameof(ShowSourceUseStepInput), nameof(ShowSourceInputName),
                nameof(JoinFieldVisible),
                nameof(JoinIsStringValue),  nameof(JoinIsNumberValue),  nameof(JoinIsBoolValue),
                nameof(JoinIsDateTimeValue), nameof(JoinIsGuidValue),
                nameof(ShowJoinUseStepInput), nameof(ShowJoinInputName),
                nameof(AvailableJoinSideTypes), nameof(JoinValueType),
                nameof(AvailableJoinFields),    nameof(JoinFieldName),
                nameof(AvailableOperators),     nameof(Operator));
        }
    }

    [WritableValue]
    [PropertyClassification(21, "Source Field", new[] { "3 Source" })]
    [SelectStringEditor("AvailableSourceFields", SelectStringEditorType.DropdownList, true)]
    [BooleanPropertyHidden(nameof(SourceFieldVisible), false)]
    public string? SourceFieldName
    {
        get => sourceFieldName;
        set
        {
            sourceFieldName = value;
            Notify(
                nameof(AvailableJoinSideTypes), nameof(JoinValueType),
                nameof(AvailableJoinFields),    nameof(JoinFieldName),
                nameof(AvailableOperators),     nameof(Operator));
        }
    }

    [WritableValue]
    [PropertyClassification(22, "Use Step Input", new[] { "3 Source" })]
    [BooleanPropertyHidden(nameof(ShowSourceUseStepInput), false)]
    public bool SourceUseStepInput
    {
        get => sourceUseStepInput;
        set
        {
            sourceUseStepInput = value;
            Notify(
                nameof(ShowSourceInputName),
                nameof(ShowSourceStringValue), nameof(ShowSourceNumberValue), nameof(ShowSourceBoolValue),
                nameof(ShowSourceDateTimeValue), nameof(ShowSourceGuidValue));
        }
    }

    [WritableValue]
    [PropertyClassification(23, "Input Name", new[] { "3 Source" })]
    [BooleanPropertyHidden(nameof(ShowSourceInputName), false)]
    public string? SourceInputName
    {
        get => sourceInputName;
        set => sourceInputName = value;
    }

    [WritableValue]
    [PropertyClassification(24, "String Value", new[] { "3 Source" })]
    [BooleanPropertyHidden(nameof(ShowSourceStringValue), false)]
    public string? SourceStringValue { get; set; }

    [WritableValue]
    [PropertyClassification(25, "Number Value", new[] { "3 Source" })]
    [BooleanPropertyHidden(nameof(ShowSourceNumberValue), false)]
    public double? SourceNumberValue { get; set; }

    [WritableValue]
    [PropertyClassification(26, "Bool Value", new[] { "3 Source" })]
    [BooleanPropertyHidden(nameof(ShowSourceBoolValue), false)]
    public bool SourceBoolValue { get; set; }

    [WritableValue]
    [PropertyClassification(27, "Date/Time Value", new[] { "3 Source" })]
    [BooleanPropertyHidden(nameof(ShowSourceDateTimeValue), false)]
    public DateTime? SourceDateTimeValue { get; set; }

    [WritableValue]
    [PropertyClassification(28, "Guid Value", new[] { "3 Source" })]
    [BooleanPropertyHidden(nameof(ShowSourceGuidValue), false)]
    public string? SourceGuidValue { get; set; }

    // -----------------------------------------------------------------------
    // Hidden context
    // -----------------------------------------------------------------------

    [PropertyHidden]
    public string? RelatedTypeName { get => relatedTypeName; set => relatedTypeName = value; }

    [PropertyHidden]
    public string? SourceTypeName  { get => sourceTypeName;  set => sourceTypeName  = value; }

    // -----------------------------------------------------------------------
    // Computed visibility bools
    // -----------------------------------------------------------------------

    [IgnoreDataMember][PropertyHidden] public bool JoinIsUnary    => AllUnaryTypes.Contains(joinSideTypeValue   ?? string.Empty);
    [IgnoreDataMember][PropertyHidden] public bool SourceIsUnary  => AllUnaryTypes.Contains(sourceSideTypeValue ?? string.Empty);
    [IgnoreDataMember][PropertyHidden] public bool IsBinaryCondition => !JoinIsUnary && !SourceIsUnary;

    // Join side
    [IgnoreDataMember][PropertyHidden] public bool JoinFieldVisible    => (joinSideTypeValue == JoinSideType.Field || JoinIsUnary) && !SourceIsUnary;
    [IgnoreDataMember][PropertyHidden] public bool ShowJoinUseStepInput => IsBinaryCondition && IsLiteralType(joinSideTypeValue);
    [IgnoreDataMember][PropertyHidden] public bool ShowJoinInputName    => ShowJoinUseStepInput && joinUseStepInput;
    [IgnoreDataMember][PropertyHidden] public bool JoinIsStringValue    => IsBinaryCondition && joinSideTypeValue == JoinSideType.StringValue   && !joinUseStepInput;
    [IgnoreDataMember][PropertyHidden] public bool JoinIsNumberValue    => IsBinaryCondition && joinSideTypeValue == JoinSideType.NumberValue   && !joinUseStepInput;
    [IgnoreDataMember][PropertyHidden] public bool JoinIsBoolValue      => IsBinaryCondition && joinSideTypeValue == JoinSideType.BoolValue     && !joinUseStepInput;
    [IgnoreDataMember][PropertyHidden] public bool JoinIsDateTimeValue  => IsBinaryCondition && joinSideTypeValue == JoinSideType.DateTimeValue && !joinUseStepInput;
    [IgnoreDataMember][PropertyHidden] public bool JoinIsGuidValue      => IsBinaryCondition && joinSideTypeValue == JoinSideType.GuidValue     && !joinUseStepInput;

    // Source side
    [IgnoreDataMember][PropertyHidden] public bool SourceFieldVisible      => (sourceSideTypeValue == JoinSideType.Field || SourceIsUnary) && !JoinIsUnary;
    [IgnoreDataMember][PropertyHidden] public bool ShowSourceUseStepInput  => IsBinaryCondition && IsLiteralType(sourceSideTypeValue);
    [IgnoreDataMember][PropertyHidden] public bool ShowSourceInputName     => ShowSourceUseStepInput && sourceUseStepInput;
    [IgnoreDataMember][PropertyHidden] public bool ShowSourceStringValue   => IsBinaryCondition && sourceSideTypeValue == JoinSideType.StringValue   && !sourceUseStepInput;
    [IgnoreDataMember][PropertyHidden] public bool ShowSourceNumberValue   => IsBinaryCondition && sourceSideTypeValue == JoinSideType.NumberValue   && !sourceUseStepInput;
    [IgnoreDataMember][PropertyHidden] public bool ShowSourceBoolValue     => IsBinaryCondition && sourceSideTypeValue == JoinSideType.BoolValue     && !sourceUseStepInput;
    [IgnoreDataMember][PropertyHidden] public bool ShowSourceDateTimeValue => IsBinaryCondition && sourceSideTypeValue == JoinSideType.DateTimeValue && !sourceUseStepInput;
    [IgnoreDataMember][PropertyHidden] public bool ShowSourceGuidValue     => IsBinaryCondition && sourceSideTypeValue == JoinSideType.GuidValue     && !sourceUseStepInput;

    // -----------------------------------------------------------------------
    // Dropdown sources
    // -----------------------------------------------------------------------

    [IgnoreDataMember][PropertyHidden]
    public string[] AvailableOperators
    {
        get
        {
            var joinType = GetTargetTypeFromSide(joinSideTypeValue, relatedTypeName, joinFieldName);
            var srcType  = GetTargetTypeFromSide(sourceSideTypeValue, sourceTypeName, sourceFieldName);
            var allowed  = new HashSet<string>(AllOperatorsSet, StringComparer.Ordinal);
            if (joinType != null) allowed.IntersectWith(OperatorsForType(joinType));
            if (srcType  != null) allowed.IntersectWith(OperatorsForType(srcType));
            return AllOperators.Where(op => allowed.Contains(op)).ToArray();
        }
    }

    [IgnoreDataMember][PropertyHidden]
    public string[] AvailableJoinFields
    {
        get
        {
            if (JoinIsUnary) return GetOrmFieldNamesForUnary(relatedTypeName, joinSideTypeValue);
            var filterType = GetTargetTypeFromSide(sourceSideTypeValue, sourceTypeName, sourceFieldName);
            return GetOrmFieldNames(relatedTypeName, filterType, operatorValue)
                .Concat(GetInverseFKFieldNames(relatedTypeName, sourceTypeName, filterType, operatorValue))
                .Distinct().OrderBy(n => n).ToArray();
        }
    }

    [IgnoreDataMember][PropertyHidden]
    public string[] AvailableSourceFields
    {
        get
        {
            if (SourceIsUnary) return GetOrmFieldNamesForUnary(sourceTypeName, sourceSideTypeValue);
            var filterType = GetTargetTypeFromSide(joinSideTypeValue, relatedTypeName, joinFieldName);
            return GetOrmFieldNames(sourceTypeName, filterType, operatorValue)
                .Concat(GetInverseFKFieldNames(sourceTypeName, relatedTypeName, filterType, operatorValue))
                .Distinct().OrderBy(n => n).ToArray();
        }
    }

    [IgnoreDataMember][PropertyHidden]
    public string[] AvailableJoinSideTypes =>
        ComputeAvailableSideTypes(GetJoinFieldEffectiveType(sourceTypeName, sourceFieldName), operatorValue);

    [IgnoreDataMember][PropertyHidden]
    public string[] AvailableSourceSideTypes =>
        ComputeAvailableSideTypes(GetJoinFieldEffectiveType(relatedTypeName, joinFieldName), operatorValue);

    // -----------------------------------------------------------------------
    // Validation
    // -----------------------------------------------------------------------

    public ValidationIssue[] GetValidationIssues()
    {
        var issues = new List<ValidationIssue>();

        if (JoinIsUnary)
        {
            if (string.IsNullOrWhiteSpace(joinFieldName))
                issues.Add(new ValidationIssue(this, "Join Field must be selected."));
        }
        else if (SourceIsUnary)
        {
            if (string.IsNullOrWhiteSpace(sourceFieldName))
                issues.Add(new ValidationIssue(this, "Source Field must be selected."));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(operatorValue))
                issues.Add(new ValidationIssue(this, "Operator must be selected."));

            // Join side
            switch (joinSideTypeValue)
            {
                case JoinSideType.Field when string.IsNullOrWhiteSpace(joinFieldName):
                    issues.Add(new ValidationIssue(this, "Join Field must be selected.")); break;
                case JoinSideType.DateTimeValue when !joinUseStepInput && JoinDateTimeValue == null:
                    issues.Add(new ValidationIssue(this, "Join Date/Time Value must be set.")); break;
                case JoinSideType.GuidValue when !joinUseStepInput && string.IsNullOrWhiteSpace(JoinGuidValue):
                    issues.Add(new ValidationIssue(this, "Join Guid Value must be entered.")); break;
            }
            if (joinUseStepInput && IsLiteralType(joinSideTypeValue) && string.IsNullOrWhiteSpace(joinInputName))
                issues.Add(new ValidationIssue(this, "Join Input Name must be entered when Use Step Input is enabled."));

            // Source side
            switch (sourceSideTypeValue)
            {
                case JoinSideType.Field when string.IsNullOrWhiteSpace(sourceFieldName):
                    issues.Add(new ValidationIssue(this, "Source Field must be selected.")); break;
                case JoinSideType.DateTimeValue when !sourceUseStepInput && SourceDateTimeValue == null:
                    issues.Add(new ValidationIssue(this, "Source Date/Time Value must be set.")); break;
                case JoinSideType.GuidValue when !sourceUseStepInput && string.IsNullOrWhiteSpace(SourceGuidValue):
                    issues.Add(new ValidationIssue(this, "Source Guid Value must be entered.")); break;
            }
            if (sourceUseStepInput && IsLiteralType(sourceSideTypeValue) && string.IsNullOrWhiteSpace(sourceInputName))
                issues.Add(new ValidationIssue(this, "Source Input Name must be entered when Use Step Input is enabled."));

            // Stale field detection: field was set but no longer exists on the type.
            // Guard with available.Length > 0 so we don't false-positive when the type hasn't loaded yet.
            if (joinSideTypeValue == JoinSideType.Field && !string.IsNullOrWhiteSpace(joinFieldName))
            {
                var available = AvailableJoinFields;
                if (available.Length > 0 && !available.Contains(joinFieldName))
                    issues.Add(new ValidationIssue(this,
                        $"Join Field '{joinFieldName}' no longer exists on the join type. Re-select it."));
            }
            if (sourceSideTypeValue == JoinSideType.Field && !string.IsNullOrWhiteSpace(sourceFieldName))
            {
                var available = AvailableSourceFields;
                if (available.Length > 0 && !available.Contains(sourceFieldName))
                    issues.Add(new ValidationIssue(this,
                        $"Source Field '{sourceFieldName}' no longer exists on the source type. Re-select it."));
            }
        }

        return issues.ToArray();
    }

    // -----------------------------------------------------------------------
    // Display
    // -----------------------------------------------------------------------

    public override string ToString()
    {
        if (JoinIsUnary)
            return $"{joinFieldName ?? "?"} {UnarySymbol(joinSideTypeValue)}";

        if (SourceIsUnary)
            return $"{sourceFieldName ?? "?"} {UnarySymbol(sourceSideTypeValue)}";

        string opSymbol = operatorValue switch
        {
            JoinOperator.Equal          => "=",
            JoinOperator.NotEqual       => "≠",
            JoinOperator.GreaterThan    => ">",
            JoinOperator.GreaterOrEqual => "≥",
            JoinOperator.LessThan       => "<",
            JoinOperator.LessOrEqual    => "≤",
            JoinOperator.Like           => "LIKE",
            _                           => operatorValue ?? "?"
        };

        string joinDisplay = joinUseStepInput && IsLiteralType(joinSideTypeValue)
            ? $"@{joinInputName ?? "?"}"
            : joinSideTypeValue switch
            {
                JoinSideType.Field         => joinFieldName ?? "?",
                JoinSideType.StringValue   => $"'{JoinStringValue}'",
                JoinSideType.NumberValue   => JoinNumberValue?.ToString(CultureInfo.InvariantCulture) ?? "0",
                JoinSideType.BoolValue     => JoinBoolValue ? "TRUE" : "FALSE",
                JoinSideType.DateTimeValue => JoinDateTimeValue?.ToString("yyyy-MM-dd") ?? "?",
                JoinSideType.GuidValue     => JoinGuidValue ?? "?",
                _                          => "?"
            };

        string srcDisplay = sourceUseStepInput && IsLiteralType(sourceSideTypeValue)
            ? $"@{sourceInputName ?? "?"}"
            : sourceSideTypeValue switch
            {
                JoinSideType.Field         => sourceFieldName ?? "?",
                JoinSideType.StringValue   => $"'{SourceStringValue}'",
                JoinSideType.NumberValue   => SourceNumberValue?.ToString(CultureInfo.InvariantCulture) ?? "0",
                JoinSideType.BoolValue     => SourceBoolValue ? "TRUE" : "FALSE",
                JoinSideType.DateTimeValue => SourceDateTimeValue?.ToString("yyyy-MM-dd") ?? "?",
                JoinSideType.GuidValue     => SourceGuidValue ?? "?",
                _                          => "?"
            };

        return $"{joinDisplay} {opSymbol} {srcDisplay}";
    }

    private static string UnarySymbol(string? t) => t switch
    {
        JoinSideType.IsNull           => "IS NULL",
        JoinSideType.IsNotNull        => "IS NOT NULL",
        JoinSideType.IsEmpty          => "IS EMPTY",
        JoinSideType.IsNotEmpty       => "IS NOT EMPTY",
        JoinSideType.IsNullOrEmpty    => "IS NULL OR EMPTY",
        JoinSideType.IsNotNullOrEmpty => "IS NOT NULL OR EMPTY",
        _                             => t ?? "?"
    };

    // -----------------------------------------------------------------------
    // Public helpers used by the step
    // -----------------------------------------------------------------------

    /// <summary>True when the side type is a typed literal (not Field and not a unary check).</summary>
    public static bool IsLiteralType(string? t) =>
        t is JoinSideType.StringValue or JoinSideType.NumberValue or JoinSideType.BoolValue
           or JoinSideType.DateTimeValue or JoinSideType.GuidValue;

    // -----------------------------------------------------------------------
    // Private helpers — type detection and filtering
    // -----------------------------------------------------------------------

    private static string[] GetOrmFieldNames(string? fullTypeName,
        Type? filterType = null, string? filterOp = null)
    {
        if (string.IsNullOrWhiteSpace(fullTypeName)) return [];
        var type = TypeUtilities.FindTypeByFullName(fullTypeName);
        if (type == null) return [];
        return DataUtilities.GetDataMemberAccessorsForClass(type, cache: true, publicOnly: false)
            .Where(a => ORMFieldAttribute.Of(a) != null
                     || a.Target?.GetCustomAttribute<ORMPrimaryKeyFieldAttribute>() != null
                     || typeof(IORMRelationship).IsAssignableFrom(a.DataType))
            .Where(a =>
            {
                // ORMRelationship<T> fields (e.g. _Maintype) store FK strings in the DB.
                // Navigation properties typed as IORMEntity also map to string FK columns.
                var t = typeof(IORMRelationship).IsAssignableFrom(a.DataType) ? typeof(string)
                      : IsEntityRefType(GetAccessorNetType(a))                ? typeof(string)
                      : GetAccessorNetType(a);
                return (filterType == null || IsTypeCompatible(t, filterType))
                    && FieldPassesOperatorFilter(t, filterOp);
            })
            .Select(a => a.Name)
            .OrderBy(n => n)
            .ToArray();
    }

    private static string[] GetOrmFieldNamesForUnary(string? typeName, string? unaryType)
    {
        bool stringOnly = unaryType is JoinSideType.IsEmpty or JoinSideType.IsNotEmpty
                       or JoinSideType.IsNullOrEmpty or JoinSideType.IsNotNullOrEmpty;
        return GetOrmFieldNames(typeName, stringOnly ? typeof(string) : null, null);
    }

    private static bool FieldPassesOperatorFilter(Type? fieldType, string? op)
    {
        if (op == null) return true;
        if (op == JoinOperator.Like)
            return fieldType == null || fieldType == typeof(string);
        if (op is JoinOperator.GreaterThan  or JoinOperator.GreaterOrEqual
               or JoinOperator.LessThan     or JoinOperator.LessOrEqual)
            return fieldType == null
                || fieldType == typeof(string)
                || IsNumericType(fieldType)
                || IsDateTimeType(fieldType);
        return true;
    }

    private static string[] ComputeAvailableSideTypes(Type? detected, string? op)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        result.Add(JoinSideType.Field);

        result.Add(JoinSideType.IsNull);
        result.Add(JoinSideType.IsNotNull);
        if (detected == null || detected == typeof(string))
        {
            result.Add(JoinSideType.IsEmpty);
            result.Add(JoinSideType.IsNotEmpty);
            result.Add(JoinSideType.IsNullOrEmpty);
            result.Add(JoinSideType.IsNotNullOrEmpty);
        }

        if (detected == null)
        {
            result.Add(JoinSideType.StringValue);
            result.Add(JoinSideType.NumberValue);
            result.Add(JoinSideType.BoolValue);
            result.Add(JoinSideType.DateTimeValue);
            result.Add(JoinSideType.GuidValue);
        }
        else if (detected == typeof(string))   result.Add(JoinSideType.StringValue);
        else if (IsNumericType(detected))       result.Add(JoinSideType.NumberValue);
        else if (detected == typeof(bool))      result.Add(JoinSideType.BoolValue);
        else if (IsDateTimeType(detected))      result.Add(JoinSideType.DateTimeValue);
        else if (detected == typeof(Guid))      result.Add(JoinSideType.GuidValue);
        else                                    result.Add(JoinSideType.StringValue);

        var byOp = SideTypesForOperator(op);
        return AllSideTypes
            .Where(t => result.Contains(t) && (AllUnaryTypes.Contains(t) || byOp.Contains(t)))
            .ToArray();
    }

    private static IReadOnlyCollection<string> SideTypesForOperator(string? op)
    {
        if (op == JoinOperator.Like)
            return new HashSet<string>(StringComparer.Ordinal)
                { JoinSideType.Field, JoinSideType.StringValue };

        if (op is JoinOperator.GreaterThan  or JoinOperator.GreaterOrEqual
               or JoinOperator.LessThan     or JoinOperator.LessOrEqual)
            return new HashSet<string>(StringComparer.Ordinal)
            {
                JoinSideType.Field,       JoinSideType.StringValue,
                JoinSideType.NumberValue, JoinSideType.DateTimeValue
            };

        var all = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in AllSideTypes)
            if (!AllUnaryTypes.Contains(t)) all.Add(t);
        return all;
    }

    private static IReadOnlyCollection<string> OperatorsForType(Type t)
    {
        if (t == typeof(bool) || t == typeof(Guid))
            return new HashSet<string>(StringComparer.Ordinal)
                { JoinOperator.Equal, JoinOperator.NotEqual };

        if (IsNumericType(t) || IsDateTimeType(t))
            return new HashSet<string>(StringComparer.Ordinal)
            {
                JoinOperator.Equal,        JoinOperator.NotEqual,
                JoinOperator.GreaterThan,  JoinOperator.GreaterOrEqual,
                JoinOperator.LessThan,     JoinOperator.LessOrEqual
            };

        return AllOperatorsSet;
    }

    private static Type? GetTargetTypeFromSide(string? sideType, string? typeName, string? fieldName) =>
        sideType switch
        {
            JoinSideType.Field         => GetJoinFieldEffectiveType(typeName, fieldName),
            JoinSideType.StringValue   => typeof(string),
            JoinSideType.NumberValue   => typeof(double),
            JoinSideType.BoolValue     => typeof(bool),
            JoinSideType.DateTimeValue => typeof(DateTime),
            JoinSideType.GuidValue     => typeof(Guid),
            _                          => null
        };

    private static bool IsTypeCompatible(Type? fieldType, Type? targetType)
    {
        if (fieldType == null || targetType == null) return true;
        if (IsNumericType(fieldType)  && IsNumericType(targetType))  return true;
        if (IsDateTimeType(fieldType) && IsDateTimeType(targetType)) return true;
        return fieldType == targetType;
    }

    private static Type? GetFieldNetType(string? typeName, string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(fieldName)) return null;
        var type = TypeUtilities.FindTypeByFullName(typeName);
        if (type == null) return null;

        var accessor = DataUtilities.GetDataMemberAccessorsForClass(type, cache: true, publicOnly: false)
            .FirstOrDefault(a => a.Name == fieldName);
        if (accessor != null)
        {
            var rawType = (accessor.Target as PropertyInfo)?.PropertyType
                       ?? (accessor.Target as FieldInfo)?.FieldType;
            if (rawType != null)
                return Nullable.GetUnderlyingType(rawType) ?? rawType;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                                 | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
        {
            var prop = t.GetProperty(fieldName, flags)
                    ?? t.GetProperties(flags).FirstOrDefault(p =>
                           string.Equals(p.Name, fieldName, StringComparison.OrdinalIgnoreCase));
            if (prop != null) return Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;

            var fld = t.GetField(fieldName, flags)
                   ?? t.GetFields(flags).FirstOrDefault(f =>
                          string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase));
            if (fld != null) return Nullable.GetUnderlyingType(fld.FieldType) ?? fld.FieldType;
        }
        return null;
    }

    private static Type? GetAccessorNetType(object accessor)
    {
        var targetProp = accessor.GetType().GetProperty("Target");
        var target = targetProp?.GetValue(accessor);
        var rawType = (target as PropertyInfo)?.PropertyType ?? (target as FieldInfo)?.FieldType;
        return rawType == null ? null : Nullable.GetUnderlyingType(rawType) ?? rawType;
    }

    // Returns FK column names that exist in entityTypeName's table but are managed by
    // ORMOneToManyRelationship<entityType> fields on otherTypeName (the join partner).
    // These columns have no C# property on entityType but are valid join targets.
    private static string[] GetInverseFKFieldNames(
        string? entityTypeName, string? otherTypeName,
        Type? filterType, string? filterOp)
    {
        if (string.IsNullOrWhiteSpace(entityTypeName) || string.IsNullOrWhiteSpace(otherTypeName)) return [];
        var entityType = TypeUtilities.FindTypeByFullName(entityTypeName);
        var otherType  = TypeUtilities.FindTypeByFullName(otherTypeName);
        if (entityType == null || otherType == null) return [];

        var result = new List<string>();
        foreach (var a in DataUtilities.GetDataMemberAccessorsForClass(otherType, cache: true, publicOnly: false))
        {
            if (!typeof(IORMOneToManyRelationship).IsAssignableFrom(a.DataType)) continue;
            // Generic arg must be entityType or a base type of it.
            var args = a.DataType.GetGenericArguments();
            if (args.Length == 0 || !args[0].IsAssignableFrom(entityType)) continue;
            // Get the FK column name from the relationship instance.
            try
            {
                var owner = Activator.CreateInstance(otherType);
                if (owner == null) continue;
                if (a.GetValue(owner) is IORMOneToManyRelationship rel)
                {
                    var fk = rel.FieldName;
                    if (!string.IsNullOrWhiteSpace(fk)
                        && (filterType == null || IsTypeCompatible(typeof(string), filterType))
                        && FieldPassesOperatorFilter(typeof(string), filterOp))
                        result.Add(fk);
                }
            }
            catch { /* skip inaccessible types */ }
        }
        return result.ToArray();
    }

    // True when the .NET type is a single ORM entity reference (the DB column is an FK string).
    private static bool IsEntityRefType(Type? t) =>
        t != null && !t.IsArray && !t.IsGenericType &&
        (typeof(IORMEntity).IsAssignableFrom(t) || ORMEntityAttribute.Of(t) != null);

    // Returns the effective type for join compatibility: string for entity ref / ORMRelationship fields (FK), raw type otherwise.
    private static Type? GetJoinFieldEffectiveType(string? typeName, string? fieldName)
    {
        var t = GetFieldNetType(typeName, fieldName);
        if (t == null) return null;
        if (IsEntityRefType(t) || typeof(IORMRelationship).IsAssignableFrom(t)) return typeof(string);
        return t;
    }

    private static bool IsNumericType(Type t) =>
        t == typeof(int)   || t == typeof(long)    || t == typeof(short)   || t == typeof(byte)  ||
        t == typeof(float) || t == typeof(double)  || t == typeof(decimal) ||
        t == typeof(uint)  || t == typeof(ulong)   || t == typeof(ushort);

    private static bool IsDateTimeType(Type t) =>
        t == typeof(DateTime) || t == typeof(DateTimeOffset);
}
