using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.Serialization;
using Decisions.FetchEntitiesAdvanced;
using DecisionsFramework;
using DecisionsFramework.Data.ORMapper;
using DecisionsFramework.Design.ConfigurationStorage.Attributes;
using DecisionsFramework.Design.Properties;
using DecisionsFramework.Design.Properties.Attributes;
using DecisionsFramework.Utilities;
using DecisionsFramework.Utilities.Data;
using Decisions.FetchEntitiesAdvanced.Join;

namespace Decisions.FetchEntitiesAdvanced.Conditions;

/// <summary>
/// A recursive filter node. Can be a leaf condition (Type = Filter) or a logical
/// group (Type = And / Or) that contains child nodes of the same type.
/// </summary>
[Writable]
public class FilterNode : IValidationSource, INotifyPropertyChanged
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

    private static readonly string[] AllNodeTypes =
        [FilterNodeType.Filter, FilterNodeType.And, FilterNodeType.Or];

    [PropertyHidden]
    public string[] AvailableNodeTypes => AllNodeTypes;

    private static readonly string[] AllValueTypes =
    [
        FilterValueType.StepInput,
        FilterValueType.StringValue,   FilterValueType.NumberValue,
        FilterValueType.BoolValue,     FilterValueType.DateTimeValue,
        FilterValueType.GuidValue,
        FilterValueType.IsNull,        FilterValueType.IsNotNull,
        FilterValueType.IsEmpty,       FilterValueType.IsNotEmpty,
        FilterValueType.IsNullOrEmpty, FilterValueType.IsNotNullOrEmpty
    ];

    internal static readonly HashSet<string> UnaryValueTypes = new(StringComparer.Ordinal)
    {
        FilterValueType.IsNull,        FilterValueType.IsNotNull,
        FilterValueType.IsEmpty,       FilterValueType.IsNotEmpty,
        FilterValueType.IsNullOrEmpty, FilterValueType.IsNotNullOrEmpty
    };

    private static readonly string[] AllPrimitiveOperators =
    [
        JoinOperator.Equal,     JoinOperator.NotEqual,
        JoinOperator.GreaterThan, JoinOperator.GreaterOrEqual,
        JoinOperator.LessThan,  JoinOperator.LessOrEqual,
        JoinOperator.Like
    ];

    private static readonly string[] AllListOperators =
    [
        ListOperator.Contains,       ListOperator.DoesNotContain,
        ListOperator.FirstInList,    ListOperator.LastInList,
        FilterValueType.IsEmpty,     FilterValueType.IsNotEmpty,
        ListOperator.CountEq,        ListOperator.CountNe,
        ListOperator.CountGt,        ListOperator.CountGte,
        ListOperator.CountLt,        ListOperator.CountLte,
        ListOperator.SumOf,          ListOperator.AvgOf,
        ListOperator.MinOf,          ListOperator.MaxOf
    ];

    internal static readonly HashSet<string> ListOpsNeedingSubField = new(StringComparer.Ordinal)
    {
        ListOperator.Contains, ListOperator.DoesNotContain,
        ListOperator.FirstInList, ListOperator.LastInList,
        ListOperator.SumOf, ListOperator.AvgOf,
        ListOperator.MinOf, ListOperator.MaxOf
    };

    internal static readonly HashSet<string> CountOperators = new(StringComparer.Ordinal)
    {
        ListOperator.CountEq, ListOperator.CountNe,
        ListOperator.CountGt, ListOperator.CountGte,
        ListOperator.CountLt, ListOperator.CountLte
    };

    // -----------------------------------------------------------------------
    // Backing fields
    // -----------------------------------------------------------------------

    [WritableValue] private string    nodeType = FilterNodeType.Filter;

    // Leaf (Filter) fields
    [WritableValue] private string?   sourceTable;
    [WritableValue] private string?   fieldName;
    [WritableValue] private string?   operatorValue = JoinOperator.Equal;
    [WritableValue] private string?   subField;           // property on the collection element type
    [WritableValue] private string?   subFieldOperator;   // comparison operator for subField
    [WritableValue] private string?   elementTypeName;    // FullName of element type when field is a collection
    [WritableValue] private bool      isEntityRefField;   // true when field is a single ORMRelationship<T> reference
    [WritableValue] private string?   valueType;
    [WritableValue] private string?   inputName;
    [WritableValue] private string?   stringValue;
    [WritableValue] private double?   numberValue;
    [WritableValue] private bool      boolValue;
    [WritableValue] private DateTime? dateTimeValue;
    [WritableValue] private string?   guidValue;

    // Composite (And/Or) field
    [WritableValue] private FilterNode[]? children;

    // Context — pushed by parent node or by the step
    [WritableValue] private string[]? availableTables;
    [WritableValue] private string[]? availableTableTypeNames;
    [WritableValue] private string?   selectedTypeFullName;

    // -----------------------------------------------------------------------
    // Type — always visible
    // -----------------------------------------------------------------------

    [PropertyClassification(0, "Type", new[] { "Details" })]
    [SelectStringEditor(nameof(AvailableNodeTypes), SelectStringEditorType.DropdownList, false)]
    public string NodeType
    {
        get => nodeType;
        set
        {
            nodeType = value ?? FilterNodeType.Filter;
            Notify(
                nameof(IsFilterNode),    nameof(IsCompositeNode),
                nameof(ShowOperator),    nameof(ShowSubField),   nameof(ShowSubFieldOperator),
                nameof(ShowValueType),   nameof(ShowStepInput),
                nameof(ShowStringValue), nameof(ShowNumberValue),
                nameof(ShowBoolValue),   nameof(ShowDateTimeValue), nameof(ShowGuidValue));
        }
    }

    // -----------------------------------------------------------------------
    // Leaf (Filter) properties
    // -----------------------------------------------------------------------

    [PropertyClassification(1, "Source Table", new[] { "Details" })]
    [SelectStringEditor("AvailableTables", SelectStringEditorType.DropdownList, true)]
    [BooleanPropertyHidden(nameof(IsFilterNode), false)]
    public string? SourceTable
    {
        get => sourceTable;
        set
        {
            sourceTable        = value;
            fieldName          = null;
            subField           = null;
            subFieldOperator   = null;
            elementTypeName    = null;
            isEntityRefField   = false;
            selectedTypeFullName = ResolveTypeName(value);
            Notify(
                nameof(SourceTable),         nameof(FieldName),
                nameof(SubField),            nameof(SubFieldOperator),
                nameof(IsCollectionField),   nameof(ElementTypeName),
                nameof(AvailableFields),     nameof(AvailableSubFields), nameof(AvailableSubFieldOperators),
                nameof(AvailableOperators),  nameof(Operator),
                nameof(AvailableValueTypes), nameof(ValueType),
                nameof(ShowOperator),        nameof(ShowSubField),    nameof(ShowSubFieldOperator),
                nameof(ShowValueType),       nameof(ShowStepInput),
                nameof(ShowStringValue),     nameof(ShowNumberValue),
                nameof(ShowBoolValue),       nameof(ShowDateTimeValue), nameof(ShowGuidValue));
        }
    }

    [PropertyClassification(2, "Field", new[] { "Details" })]
    [SelectStringEditor("AvailableFields", SelectStringEditorType.DropdownList, true)]
    [BooleanPropertyHidden(nameof(IsFilterNode), false)]
    public string? FieldName
    {
        get => fieldName;
        set
        {
            // Detect collection type or entity ref (simple name or dot-path) before committing
            string? newElemType  = null;
            bool    newEntityRef = false;
            if (value != null && selectedTypeFullName != null)
            {
                if (value.Contains('.'))
                {
                    // Dot-path (e.g. "SubtypeData.SomeField"): entity ref if first segment resolves to one
                    var firstSeg = value[..value.IndexOf('.')];
                    var ft = OrmFieldHelper.GetFieldNetType(selectedTypeFullName, firstSeg);
                    var baseType = ft != null ? (Nullable.GetUnderlyingType(ft) ?? ft) : null;
                    if (baseType != null && !baseType.IsArray && !baseType.IsGenericType
                        && (typeof(IORMEntity).IsAssignableFrom(baseType) || ORMEntityAttribute.Of(baseType) != null))
                        newEntityRef = true;
                }
                else
                {
                    var ft = OrmFieldHelper.GetFieldNetType(selectedTypeFullName, value);
                    if (ft != null && IsORMCollectionType(ft, out var et) && et != null)
                        newElemType = et.FullName;
                    else
                    {
                        var baseType = ft != null ? (Nullable.GetUnderlyingType(ft) ?? ft) : null;
                        if (baseType != null && !baseType.IsArray && !baseType.IsGenericType
                            && (typeof(IORMEntity).IsAssignableFrom(baseType) || ORMEntityAttribute.Of(baseType) != null))
                            newEntityRef = true;
                    }
                }
            }

            bool wasCollection = !string.IsNullOrWhiteSpace(elementTypeName);
            bool nowCollection = newElemType != null;
            bool wasEntityRef  = isEntityRefField;
            bool wasDotPath    = fieldName?.Contains('.') == true;
            bool nowDotPath    = value?.Contains('.') == true;

            fieldName        = value;
            elementTypeName  = newElemType;
            isEntityRefField = newEntityRef;
            subField         = null;
            subFieldOperator = null;

            // Reset operator when switching field kind
            bool fieldKindChanged = wasCollection != nowCollection
                || wasEntityRef != newEntityRef
                || (newEntityRef && wasDotPath != nowDotPath);
            if (fieldKindChanged)
                operatorValue = nowCollection  ? ListOperator.Contains
                              : (newEntityRef && !nowDotPath) ? FilterValueType.IsNull  // bare entity ref → IS NULL
                              :                                  JoinOperator.Equal;     // primitive or dot-path

            Notify(
                nameof(FieldName),           nameof(SubField),            nameof(SubFieldOperator),
                nameof(IsCollectionField),   nameof(IsEntityRefField),    nameof(ElementTypeName),
                nameof(IsSerializedField),   nameof(SerializedFieldNote),
                nameof(AvailableSubFields),  nameof(AvailableSubFieldOperators),
                nameof(AvailableOperators),  nameof(Operator),
                nameof(AvailableValueTypes), nameof(ValueType),
                nameof(ShowOperator),        nameof(ShowSubField),    nameof(ShowSubFieldOperator),
                nameof(ShowValueType),       nameof(ShowStepInput),
                nameof(ShowStringValue),     nameof(ShowNumberValue),
                nameof(ShowBoolValue),       nameof(ShowDateTimeValue), nameof(ShowGuidValue));
        }
    }

    [PropertyClassification(3, "Field Note", new[] { "Details" })]
    [InfoOrWarningEditor(false, null, true)]
    [BooleanPropertyHidden(nameof(IsSerializedField), false)]
    public string SerializedFieldNote
    {
        get => "This field stores non-ORM data serialized as JSON or binary. " +
               "Filtering is performed against the raw serialized text, so string " +
               "operators such as '=' or 'LIKE' work, but results may be unexpected.";
        set { }
    }

    [PropertyClassification(4, "Operator", new[] { "Details" })]
    [SelectStringEditor("AvailableOperators", SelectStringEditorType.DropdownList, false)]
    [BooleanPropertyHidden(nameof(ShowOperator), false)]
    public string? Operator
    {
        get => operatorValue;
        set
        {
            operatorValue    = value;
            subField         = null;
            subFieldOperator = null;
            Notify(
                nameof(SubField),            nameof(SubFieldOperator),
                nameof(AvailableFields),     nameof(FieldName),
                nameof(AvailableSubFields),  nameof(AvailableSubFieldOperators),
                nameof(AvailableValueTypes), nameof(ValueType),
                nameof(ShowLikeNote),        nameof(LikeNote),
                nameof(ShowSubField),        nameof(ShowSubFieldOperator),
                nameof(ShowValueType),       nameof(ShowStepInput),
                nameof(ShowStringValue),     nameof(ShowNumberValue),
                nameof(ShowBoolValue),       nameof(ShowDateTimeValue), nameof(ShowGuidValue));
        }
    }

    [PropertyClassification(5, "Operator Note", new[] { "Details" })]
    [InfoOrWarningEditor(false, null, true)]
    [BooleanPropertyHidden(nameof(ShowLikeNote), false)]
    public string LikeNote
    {
        get => "Use % to match any sequence of characters (e.g. 'Smith%', '%@domain.com'). " +
               "Use _ to match exactly one character (e.g. 'J_n' matches 'Jan', 'Jon'). " +
               "To match a literal % or _, escape it with a backslash: \\% or \\_";
        set { }
    }

    [PropertyClassification(6, "Sub Field", new[] { "Details" })]
    [SelectStringEditor("AvailableSubFields", SelectStringEditorType.DropdownList, true)]
    [BooleanPropertyHidden(nameof(ShowSubField), false)]
    public string? SubField
    {
        get => subField;
        set
        {
            subField         = value;
            subFieldOperator = null;
            Notify(
                nameof(SubField),            nameof(SubFieldOperator),
                nameof(AvailableSubFieldOperators),
                nameof(ShowSubFieldOperator),
                nameof(AvailableValueTypes), nameof(ValueType),
                nameof(ShowValueType),       nameof(ShowStepInput),
                nameof(ShowStringValue),     nameof(ShowNumberValue),
                nameof(ShowBoolValue),       nameof(ShowDateTimeValue), nameof(ShowGuidValue));
        }
    }

    [PropertyClassification(7, "Sub Field Operator", new[] { "Details" })]
    [SelectStringEditor("AvailableSubFieldOperators", SelectStringEditorType.DropdownList, false)]
    [BooleanPropertyHidden(nameof(ShowSubFieldOperator), false)]
    public string? SubFieldOperator
    {
        get => subFieldOperator;
        set
        {
            subFieldOperator = value;
            Notify(
                nameof(SubFieldOperator),
                nameof(AvailableValueTypes), nameof(ValueType),
                nameof(ShowValueType));
        }
    }

    [PropertyClassification(8, "Value Type", new[] { "Details" })]
    [SelectStringEditor("AvailableValueTypes", SelectStringEditorType.DropdownList, false)]
    [BooleanPropertyHidden(nameof(ShowValueType), false)]
    public string? ValueType
    {
        get => valueType;
        set
        {
            valueType = value;
            Notify(
                nameof(IsUnary),
                nameof(ShowOperator),
                nameof(ShowStepInput),
                nameof(ShowStringValue),     nameof(ShowNumberValue),
                nameof(ShowBoolValue),       nameof(ShowDateTimeValue), nameof(ShowGuidValue),
                nameof(AvailableFields),     nameof(FieldName),
                nameof(AvailableOperators),  nameof(Operator));
        }
    }

    [PropertyClassification(9, "Input Name", new[] { "Details" })]
    [BooleanPropertyHidden(nameof(ShowStepInput), false)]
    public string? InputName
    {
        get => inputName;
        set { inputName = value; Notify(nameof(InputName)); }
    }

    [PropertyClassification(10, "String Value", new[] { "Details" })]
    [BooleanPropertyHidden(nameof(ShowStringValue), false)]
    public string? StringValue
    {
        get => stringValue;
        set { stringValue = value; Notify(nameof(StringValue)); }
    }

    [PropertyClassification(11, "Number Value", new[] { "Details" })]
    [BooleanPropertyHidden(nameof(ShowNumberValue), false)]
    public double? NumberValue
    {
        get => numberValue;
        set { numberValue = value; Notify(nameof(NumberValue)); }
    }

    [PropertyClassification(12, "Bool Value", new[] { "Details" })]
    [BooleanPropertyHidden(nameof(ShowBoolValue), false)]
    public bool BoolValue
    {
        get => boolValue;
        set { boolValue = value; Notify(nameof(BoolValue)); }
    }

    [PropertyClassification(13, "Date/Time Value", new[] { "Details" })]
    [BooleanPropertyHidden(nameof(ShowDateTimeValue), false)]
    public DateTime? DateTimeValue
    {
        get => dateTimeValue;
        set { dateTimeValue = value; Notify(nameof(DateTimeValue)); }
    }

    [PropertyClassification(14, "Guid Value", new[] { "Details" })]
    [BooleanPropertyHidden(nameof(ShowGuidValue), false)]
    public string? GuidValue
    {
        get => guidValue;
        set { guidValue = value; Notify(nameof(GuidValue)); }
    }

    // -----------------------------------------------------------------------
    // Composite (And / Or) property
    // -----------------------------------------------------------------------

    [PropertyClassification(15, "Conditions", new[] { "Details" })]
    [BooleanPropertyHidden(nameof(IsFilterNode), true)]
    public FilterNode[]? Children
    {
        get => children;
        set
        {
            children = value;
            if (availableTables != null)
                foreach (var child in children ?? [])
                    child.PushContext(availableTables, availableTableTypeNames ?? []);
            Notify(nameof(Children));
        }
    }

    // -----------------------------------------------------------------------
    // Hidden context
    // -----------------------------------------------------------------------

    [PropertyHidden] public string[]  AvailableTables       { get => availableTables ?? [];     set => availableTables = value; }
    [PropertyHidden] public string[]  AvailableTableTypeNames{ get => availableTableTypeNames ?? []; set => availableTableTypeNames = value; }
    [PropertyHidden] public string?   SelectedTypeFullName  => selectedTypeFullName;
    [PropertyHidden] public string?   ElementTypeName       => elementTypeName;

    // -----------------------------------------------------------------------
    // Computed visibility bools
    // -----------------------------------------------------------------------

    [IgnoreDataMember][PropertyHidden] public bool IsFilterNode    => nodeType == FilterNodeType.Filter;
    [IgnoreDataMember][PropertyHidden] public bool IsCompositeNode => nodeType == FilterNodeType.And || nodeType == FilterNodeType.Or;
    [IgnoreDataMember][PropertyHidden] public bool IsCollectionField  => !string.IsNullOrWhiteSpace(elementTypeName);
    [IgnoreDataMember][PropertyHidden] public bool IsEntityRefField  => isEntityRefField;

    // True when the selected field stores non-ORM data serialized as JSON/binary (e.g. string[], List<int>).
    // These appear as regular fields in the picker but filtering is against raw serialized text.
    [IgnoreDataMember][PropertyHidden] public bool IsSerializedField
    {
        get
        {
            if (!IsFilterNode || string.IsNullOrWhiteSpace(fieldName) || string.IsNullOrWhiteSpace(selectedTypeFullName))
                return false;
            if (IsCollectionField || isEntityRefField) return false;
            var ft = OrmFieldHelper.GetFieldNetType(selectedTypeFullName, fieldName);
            if (ft == null) return false;
            if (!ft.IsArray && (!ft.IsGenericType || (
                    ft.GetGenericTypeDefinition() != typeof(List<>) &&
                    ft.GetGenericTypeDefinition() != typeof(IList<>) &&
                    ft.GetGenericTypeDefinition() != typeof(IEnumerable<>) &&
                    ft.GetGenericTypeDefinition() != typeof(ICollection<>))))
                return false;
            return !IsORMCollectionType(ft, out _);
        }
    }

    // IsUnary applies to primitive fields AND dot-path entity refs (where the terminal field comparison is unary).
    // It does NOT apply to bare entity ref fields — their IsNull/IsNotNull are operators, not value types.
    [IgnoreDataMember][PropertyHidden] public bool IsUnary =>
        !IsCollectionField
        && !(isEntityRefField && fieldName?.Contains('.') != true)
        && UnaryValueTypes.Contains(valueType ?? string.Empty);

    [IgnoreDataMember][PropertyHidden] public bool IsStepInput =>
        IsFilterNode && !IsUnary && valueType == FilterValueType.StepInput;

    [IgnoreDataMember][PropertyHidden] public bool ShowOperator =>
        IsFilterNode && !string.IsNullOrWhiteSpace(fieldName) && (IsCollectionField || !IsUnary);

    [IgnoreDataMember][PropertyHidden] public bool ShowLikeNote =>
        IsFilterNode && operatorValue == JoinOperator.Like;

    // Sub Field is only for collection elements now; entity ref navigation lives in the Field dot-path.
    [IgnoreDataMember][PropertyHidden] public bool ShowSubField =>
        IsFilterNode && IsCollectionField && ListOpsNeedingSubField.Contains(operatorValue ?? string.Empty);

    [IgnoreDataMember][PropertyHidden] public bool ShowSubFieldOperator =>
        ShowSubField && !string.IsNullOrWhiteSpace(subField);

    [IgnoreDataMember][PropertyHidden] public bool ShowValueType =>
        IsFilterNode && (
            // Primitives and dot-path entity refs (but not bare entity refs — those use operators only)
            (!IsCollectionField
             && !(isEntityRefField && fieldName?.Contains('.') != true)
             && !IsUnary
             && !string.IsNullOrWhiteSpace(fieldName)) ||
            // Collection sub-field comparisons or count operators (which need a value but no sub-field)
            (IsCollectionField && (ShowSubFieldOperator || CountOperators.Contains(operatorValue ?? string.Empty)))
        );

    [IgnoreDataMember][PropertyHidden] public bool ShowStepInput     => ShowValueType && !IsUnary && valueType == FilterValueType.StepInput;
    [IgnoreDataMember][PropertyHidden] public bool ShowStringValue   => ShowValueType && !IsUnary && valueType == FilterValueType.StringValue;
    [IgnoreDataMember][PropertyHidden] public bool ShowNumberValue   => ShowValueType && !IsUnary && valueType == FilterValueType.NumberValue;
    [IgnoreDataMember][PropertyHidden] public bool ShowBoolValue     => ShowValueType && !IsUnary && valueType == FilterValueType.BoolValue;
    [IgnoreDataMember][PropertyHidden] public bool ShowDateTimeValue => ShowValueType && !IsUnary && valueType == FilterValueType.DateTimeValue;
    [IgnoreDataMember][PropertyHidden] public bool ShowGuidValue     => ShowValueType && !IsUnary && valueType == FilterValueType.GuidValue;

    // -----------------------------------------------------------------------
    // Dropdown sources
    // -----------------------------------------------------------------------

    [IgnoreDataMember][PropertyHidden]
    public string[] AvailableFields
    {
        get
        {
            if (string.IsNullOrWhiteSpace(selectedTypeFullName)) return [];
            var type = TypeUtilities.FindTypeByFullName(selectedTypeFullName);
            if (type == null) return [];

            var result = new List<string>();

            // Primitive ORM fields and single entity references via data member accessors
            result.AddRange(
                DataUtilities.GetDataMemberAccessorsForClass(type, cache: true, publicOnly: false)
                    .Where(a =>
                    {
                        var accessorType = OrmFieldHelper.GetAccessorNetType(a);

                        // Single entity reference (ORMRelationship<T>): included regardless of ORM attribute
                        // because the FK column (_PropName) can still be compared.
                        if (accessorType != null && !accessorType.IsArray && !accessorType.IsGenericType
                            && (typeof(IORMEntity).IsAssignableFrom(accessorType) || ORMEntityAttribute.Of(accessorType) != null))
                            return true;

                        // Primitive fields require [ORMField] or [ORMPrimaryKeyField]
                        if (ORMFieldAttribute.Of(a) == null
                            && a.Target?.GetCustomAttribute<ORMPrimaryKeyFieldAttribute>() == null)
                            return false;
                        if (accessorType != null && IsORMCollectionType(accessorType, out _)) return false;
                        return FieldPassesOperatorFilter(accessorType, IsCollectionField ? null : operatorValue)
                            && FieldPassesValueTypeFilter(accessorType, IsCollectionField ? null : valueType);
                    })
                    .Select(a => a.Name));

            // Dot-paths through entity ref fields to their reachable primitive sub-fields.
            // These allow filtering on nested types without a separate Sub Field picker.
            foreach (var a in DataUtilities.GetDataMemberAccessorsForClass(type, cache: true, publicOnly: false))
            {
                var at = OrmFieldHelper.GetAccessorNetType(a);
                if (at == null) continue;
                var baseType = Nullable.GetUnderlyingType(at) ?? at;
                if (baseType.IsArray || baseType.IsGenericType) continue;
                if (!typeof(IORMEntity).IsAssignableFrom(baseType) && ORMEntityAttribute.Of(baseType) == null) continue;
                // baseType is an entity ref type — add "FieldName.SubField" paths
                result.AddRange(GetSubFieldPaths(baseType, a.Name, 4, new HashSet<string>()));
            }

            // Collection properties (IORMEntity arrays/lists): use direct reflection because
            // navigation properties are typically excluded from GetDataMemberAccessorsForClass
            // (they lack [DataMember]/[WritableValue]/[ORMField] attributes).
            var seen = new HashSet<string>(result, StringComparer.OrdinalIgnoreCase);
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var prop in t.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (!seen.Add(prop.Name)) continue;
                    if (IsORMCollectionType(prop.PropertyType, out _))
                        result.Add(prop.Name);
                }
            }

            // Inverse FK fields: FK columns in this table that are owned by an
            // ORMOneToManyRelationship<T> on another entity type (e.g. _parent_id when
            // ParentType declares ORMOneToManyRelationship<ThisType>).
            // These have no C# property on this type but are valid string FK targets.
            foreach (var fk in GetAllInverseFKFieldNames(type))
                if (seen.Add(fk)) result.Add(fk);

            return result.OrderBy(n => n).ToArray();
        }
    }

    [IgnoreDataMember][PropertyHidden]
    public string[] AvailableSubFields
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(elementTypeName))
            {
                // Collection element type: flat list of primitive ORM fields (non-recursive)
                var refType = TypeUtilities.FindTypeByFullName(elementTypeName);
                if (refType == null) return [];
                bool numericOnly = operatorValue == ListOperator.SumOf || operatorValue == ListOperator.AvgOf;
                return DataUtilities.GetDataMemberAccessorsForClass(refType, cache: true, publicOnly: false)
                    .Where(a =>
                    {
                        if (ORMFieldAttribute.Of(a) == null
                            && a.Target?.GetCustomAttribute<ORMPrimaryKeyFieldAttribute>() == null)
                            return false;
                        return !numericOnly || OrmFieldHelper.IsNumericType(OrmFieldHelper.GetAccessorNetType(a));
                    })
                    .Select(a => a.Name)
                    .OrderBy(n => n)
                    .ToArray();
            }

            if (isEntityRefField && !string.IsNullOrWhiteSpace(fieldName))
            {
                // Entity ref: recursively enumerate all primitive fields reachable through entity ref chains
                var refType = OrmFieldHelper.GetFieldNetType(selectedTypeFullName, fieldName);
                if (refType == null) return [];
                return GetSubFieldPaths(refType, string.Empty, 5, new HashSet<string>())
                    .OrderBy(p => p)
                    .ToArray();
            }

            return [];
        }
    }

    [IgnoreDataMember][PropertyHidden]
    public string[] AvailableOperators
    {
        get
        {
            if (string.IsNullOrWhiteSpace(fieldName)) return [];
            if (IsCollectionField) return AllListOperators;
            if (IsEntityRefField)
            {
                if (fieldName.Contains('.'))
                {
                    // Dot-path: operators for the terminal primitive type
                    var primaryTyp = TypeUtilities.FindTypeByFullName(selectedTypeFullName);
                    var ft = primaryTyp != null ? ResolveEntityRefPathTerminalType(primaryTyp, fieldName) : null;
                    if (ft == typeof(bool) || ft == typeof(Guid))
                        return [JoinOperator.Equal, JoinOperator.NotEqual];
                    if (OrmFieldHelper.IsNumericType(ft) || OrmFieldHelper.IsDateTimeType(ft))
                        return [JoinOperator.Equal, JoinOperator.NotEqual,
                                JoinOperator.GreaterThan, JoinOperator.GreaterOrEqual,
                                JoinOperator.LessThan,    JoinOperator.LessOrEqual];
                    return AllPrimitiveOperators;
                }
                // Bare entity ref: only null checks (as operators, not value types)
                return [FilterValueType.IsNull, FilterValueType.IsNotNull];
            }
            if (IsUnary) return [];
            var fieldType = OrmFieldHelper.GetFieldNetType(selectedTypeFullName, fieldName);
            if (fieldType == typeof(bool) || fieldType == typeof(Guid))
                return [JoinOperator.Equal, JoinOperator.NotEqual];
            if (OrmFieldHelper.IsNumericType(fieldType) || OrmFieldHelper.IsDateTimeType(fieldType))
                return [JoinOperator.Equal, JoinOperator.NotEqual,
                        JoinOperator.GreaterThan, JoinOperator.GreaterOrEqual,
                        JoinOperator.LessThan,    JoinOperator.LessOrEqual];
            return AllPrimitiveOperators;
        }
    }

    [IgnoreDataMember][PropertyHidden]
    public string[] AvailableSubFieldOperators
    {
        get
        {
            if (string.IsNullOrWhiteSpace(subField)) return [];

            Type? ft;
            if (!string.IsNullOrWhiteSpace(elementTypeName))
                ft = OrmFieldHelper.GetFieldNetType(elementTypeName, subField);
            else if (isEntityRefField && !string.IsNullOrWhiteSpace(fieldName))
            {
                var refType = OrmFieldHelper.GetFieldNetType(selectedTypeFullName, fieldName);
                ft = refType != null ? ResolveEntityRefPathTerminalType(refType, subField) : null;
            }
            else
                return [];

            if (ft == typeof(bool) || ft == typeof(Guid))
                return [JoinOperator.Equal, JoinOperator.NotEqual];
            if (OrmFieldHelper.IsNumericType(ft) || OrmFieldHelper.IsDateTimeType(ft))
                return [JoinOperator.Equal, JoinOperator.NotEqual,
                        JoinOperator.GreaterThan, JoinOperator.GreaterOrEqual,
                        JoinOperator.LessThan,    JoinOperator.LessOrEqual];
            return AllPrimitiveOperators;
        }
    }

    [IgnoreDataMember][PropertyHidden]
    public string[] AvailableValueTypes
    {
        get
        {
            if (IsCollectionField)
            {
                // Count operators: result is always an integer, no sub-field involved
                if (CountOperators.Contains(operatorValue ?? string.Empty))
                    return [FilterValueType.StepInput, FilterValueType.NumberValue];
                if (string.IsNullOrWhiteSpace(subField)) return [];
                // Sum/Avg: aggregate result is always numeric regardless of sub-field type
                if (operatorValue == ListOperator.SumOf || operatorValue == ListOperator.AvgOf)
                    return [FilterValueType.StepInput, FilterValueType.NumberValue];
                // Min/Max, Contains, First/Last: based on the sub-field type
                var ft = OrmFieldHelper.GetFieldNetType(elementTypeName, subField);
                return BuildValueTypeList(ft, subFieldOperator);
            }
            if (IsEntityRefField)
            {
                if (fieldName?.Contains('.') != true) return []; // bare entity ref: no value type (operators only)
                // Dot-path: value types for the terminal primitive field
                var primaryTyp = TypeUtilities.FindTypeByFullName(selectedTypeFullName);
                var ft         = primaryTyp != null ? ResolveEntityRefPathTerminalType(primaryTyp, fieldName) : null;
                return BuildValueTypeList(ft, operatorValue);
            }
            if (string.IsNullOrWhiteSpace(fieldName)) return [];
            // Serialized fields (List<string>, string[], etc.) are stored as text in the DB;
            // treat as string so the user can filter against the serialized representation.
            if (IsSerializedField)
                return BuildValueTypeList(typeof(string), operatorValue);
            var fieldType = OrmFieldHelper.GetFieldNetType(selectedTypeFullName, fieldName);
            return BuildValueTypeList(fieldType, operatorValue);
        }
    }

    private static string[] BuildValueTypeList(Type? ft, string? op)
    {
        var result = new List<string> { FilterValueType.StepInput };
        if (ft == null || ft == typeof(string))  result.Add(FilterValueType.StringValue);
        if (ft == null || OrmFieldHelper.IsNumericType(ft))      result.Add(FilterValueType.NumberValue);
        if (ft == null || ft == typeof(bool))     result.Add(FilterValueType.BoolValue);
        if (ft == null || OrmFieldHelper.IsDateTimeType(ft))     result.Add(FilterValueType.DateTimeValue);
        if (ft == null || ft == typeof(Guid))     result.Add(FilterValueType.GuidValue);
        result.Add(FilterValueType.IsNull);
        result.Add(FilterValueType.IsNotNull);
        if (ft == null || ft == typeof(string))
        {
            result.Add(FilterValueType.IsEmpty);
            result.Add(FilterValueType.IsNotEmpty);
            result.Add(FilterValueType.IsNullOrEmpty);
            result.Add(FilterValueType.IsNotNullOrEmpty);
        }
        if (op == JoinOperator.Like)
            result.RemoveAll(t => t != FilterValueType.StepInput
                               && t != FilterValueType.StringValue
                               && !UnaryValueTypes.Contains(t));
        return AllValueTypes.Where(t => result.Contains(t)).ToArray();
    }

    // -----------------------------------------------------------------------
    // Context propagation
    // -----------------------------------------------------------------------

    public void PushContext(string[] tables, string[] typeNames)
    {
        availableTables          = tables;
        availableTableTypeNames  = typeNames;
        selectedTypeFullName     = ResolveTypeName(sourceTable);

        // Recompute elementTypeName and isEntityRefField after context is available
        if (!string.IsNullOrWhiteSpace(fieldName) && selectedTypeFullName != null)
        {
            if (fieldName.Contains('.'))
            {
                // Dot-path: entity ref if first segment resolves to one
                elementTypeName = null;
                var firstSeg = fieldName[..fieldName.IndexOf('.')];
                var ft = OrmFieldHelper.GetFieldNetType(selectedTypeFullName, firstSeg);
                var baseType = ft != null ? (Nullable.GetUnderlyingType(ft) ?? ft) : null;
                isEntityRefField = baseType != null && !baseType.IsArray && !baseType.IsGenericType
                    && (typeof(IORMEntity).IsAssignableFrom(baseType) || ORMEntityAttribute.Of(baseType) != null);
            }
            else
            {
                var ft = OrmFieldHelper.GetFieldNetType(selectedTypeFullName, fieldName);
                if (ft != null && IsORMCollectionType(ft, out var et) && et != null)
                {
                    elementTypeName  = et.FullName;
                    isEntityRefField = false;
                }
                else
                {
                    elementTypeName = null;
                    var baseType = ft != null ? (Nullable.GetUnderlyingType(ft) ?? ft) : null;
                    isEntityRefField = baseType != null && !baseType.IsArray && !baseType.IsGenericType
                        && (typeof(IORMEntity).IsAssignableFrom(baseType) || ORMEntityAttribute.Of(baseType) != null);
                }
            }
        }

        Notify(
            nameof(AvailableTables),     nameof(AvailableFields),
            nameof(AvailableSubFields),  nameof(AvailableOperators),
            nameof(AvailableSubFieldOperators), nameof(AvailableValueTypes),
            nameof(IsCollectionField),   nameof(IsEntityRefField),    nameof(ElementTypeName),
            nameof(IsSerializedField),   nameof(SerializedFieldNote),
            nameof(Operator),            nameof(ValueType),
            nameof(ShowOperator),        nameof(ShowSubField),    nameof(ShowSubFieldOperator),
            nameof(ShowValueType),       nameof(ShowStepInput),
            nameof(ShowStringValue),     nameof(ShowNumberValue),
            nameof(ShowBoolValue),       nameof(ShowDateTimeValue), nameof(ShowGuidValue));
        foreach (var child in children ?? [])
            child.PushContext(tables, typeNames);
    }

    private string? ResolveTypeName(string? table)
    {
        if (string.IsNullOrWhiteSpace(table)) return null;
        var tables = availableTables ?? [];
        var idx = Array.FindIndex(tables, t => string.Equals(t, table, StringComparison.OrdinalIgnoreCase));
        return idx >= 0 ? availableTableTypeNames?[idx] : null;
    }

    // -----------------------------------------------------------------------
    // Validation
    // -----------------------------------------------------------------------

    public ValidationIssue[] GetValidationIssues()
    {
        var issues = new List<ValidationIssue>();

        if (IsCompositeNode)
        {
            if (children == null || children.Length == 0)
                issues.Add(new ValidationIssue(this, $"{nodeType} group must contain at least one condition."));
            foreach (var child in children ?? [])
                issues.AddRange(child.GetValidationIssues());
            return issues.ToArray();
        }

        // Leaf (Filter)
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            issues.Add(new ValidationIssue(this, "Field must be selected."));
            return issues.ToArray();
        }

        if (string.IsNullOrWhiteSpace(operatorValue))
        {
            issues.Add(new ValidationIssue(this, "Operator must be selected."));
            return issues.ToArray();
        }

        if (IsCollectionField)
        {
            if (CountOperators.Contains(operatorValue))
            {
                ValidateValue(issues); // count value required; no sub-field needed
            }
            else if (ListOpsNeedingSubField.Contains(operatorValue))
            {
                if (string.IsNullOrWhiteSpace(subField))
                    issues.Add(new ValidationIssue(this, "Sub Field must be selected."));
                else if (string.IsNullOrWhiteSpace(subFieldOperator))
                    issues.Add(new ValidationIssue(this, "Sub Field Operator must be selected."));
                else
                    ValidateValue(issues);
            }
            return issues.ToArray();
        }

        if (IsEntityRefField)
        {
            if (fieldName?.Contains('.') != true)
                return issues.ToArray(); // bare entity ref: IsNull/IsNotNull operators, no value needed
            // Dot-path: validate like a primitive
            if (IsUnary) return issues.ToArray();
            ValidateValue(issues);
            return issues.ToArray();
        }

        // Primitive leaf
        if (IsUnary) return issues.ToArray();

        ValidateValue(issues);
        return issues.ToArray();
    }

    private void ValidateValue(List<ValidationIssue> issues)
    {
        if (valueType == FilterValueType.StepInput && string.IsNullOrWhiteSpace(inputName))
            issues.Add(new ValidationIssue(this, "Input Name must be entered when Value Type is Step Input."));
        if (valueType == FilterValueType.DateTimeValue && dateTimeValue == null)
            issues.Add(new ValidationIssue(this, "Date/Time Value must be set."));
        if (valueType == FilterValueType.GuidValue && string.IsNullOrWhiteSpace(guidValue))
            issues.Add(new ValidationIssue(this, "Guid Value must be entered."));
    }

    // -----------------------------------------------------------------------
    // Display
    // -----------------------------------------------------------------------

    public override string ToString()
    {
        if (IsCompositeNode)
        {
            int count = children?.Length ?? 0;
            return count == 0
                ? $"{nodeType} (empty)"
                : $"{nodeType} ({count} condition{(count == 1 ? "" : "s")})";
        }

        if (string.IsNullOrWhiteSpace(fieldName)) return "Filter (unconfigured)";

        string tablePrefix = !string.IsNullOrWhiteSpace(sourceTable) ? $"{sourceTable}." : string.Empty;
        string field = $"{tablePrefix}{fieldName}";

        if (IsCollectionField)
        {
            // Count operators: no sub-field, value is the threshold
            if (CountOperators.Contains(operatorValue ?? string.Empty))
            {
                string countVal = valueType == FilterValueType.StepInput
                    ? $"@{inputName ?? "?"}"
                    : numberValue?.ToString(CultureInfo.InvariantCulture) ?? "?";
                return $"{field} {operatorValue} {countVal}";
            }

            string opLabel = operatorValue switch
            {
                ListOperator.Contains       => "contains",
                ListOperator.DoesNotContain => "does not contain",
                ListOperator.FirstInList    => "first →",
                ListOperator.LastInList     => "last →",
                ListOperator.SumOf          => "sum of",
                ListOperator.AvgOf          => "avg of",
                ListOperator.MinOf          => "min of",
                ListOperator.MaxOf          => "max of",
                FilterValueType.IsEmpty     => "is empty",
                FilterValueType.IsNotEmpty  => "is not empty",
                _                           => operatorValue ?? "?"
            };
            if (!ListOpsNeedingSubField.Contains(operatorValue ?? string.Empty))
                return $"{field} {opLabel}";
            if (string.IsNullOrWhiteSpace(subField))
                return $"{field} {opLabel} (sub field not set)";
            string subVal = valueType == FilterValueType.StepInput
                ? $"@{inputName ?? "?"}"
                : $"[{valueType ?? "?"}]";
            return $"{field} {opLabel} {subField} {subFieldOperator ?? "?"} {subVal}";
        }

        if (IsEntityRefField && fieldName?.Contains('.') != true)
        {
            // Bare entity ref: only null-check display
            if (operatorValue == FilterValueType.IsNull)    return $"{field} IS NULL";
            if (operatorValue == FilterValueType.IsNotNull) return $"{field} IS NOT NULL";
            return $"{field} (operator not set)";
        }
        // Dot-path entity refs fall through to the primitive display below

        if (IsUnary)
            return $"{field} {UnaryLabel(valueType)}";

        string opSym = operatorValue switch
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

        string val = valueType switch
        {
            FilterValueType.StepInput     => $"@{inputName ?? "?"}",
            FilterValueType.StringValue   => $"'{stringValue}'",
            FilterValueType.NumberValue   => numberValue?.ToString(CultureInfo.InvariantCulture) ?? "0",
            FilterValueType.BoolValue     => boolValue ? "TRUE" : "FALSE",
            FilterValueType.DateTimeValue => dateTimeValue?.ToString("yyyy-MM-dd") ?? "?",
            FilterValueType.GuidValue     => guidValue ?? "?",
            _                             => "?"
        };

        return $"{field} {opSym} {val}";
    }

    private static string UnaryLabel(string? t) => t switch
    {
        FilterValueType.IsNull           => "IS NULL",
        FilterValueType.IsNotNull        => "IS NOT NULL",
        FilterValueType.IsEmpty          => "IS EMPTY",
        FilterValueType.IsNotEmpty       => "IS NOT EMPTY",
        FilterValueType.IsNullOrEmpty    => "IS NULL OR EMPTY",
        FilterValueType.IsNotNullOrEmpty => "IS NOT NULL OR EMPTY",
        _                                => t ?? "?"
    };

    // -----------------------------------------------------------------------
    // Internal helpers (also used by the step)
    // -----------------------------------------------------------------------

    /// <summary>Returns true when <paramref name="t"/> is T[]/List&lt;T&gt; where T is an ORM entity
    /// (implements IORMEntity or carries [ORMEntity] attribute — the latter covers Database Structure types).</summary>
    internal static bool IsORMCollectionType(Type? t, out Type? elementType)
    {
        elementType = null;
        if (t == null) return false;
        Type? et = null;
        if (t.IsArray)
            et = t.GetElementType();
        else if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            if (def == typeof(List<>)        || def == typeof(IList<>)
             || def == typeof(IEnumerable<>) || def == typeof(ICollection<>))
                et = t.GetGenericArguments()[0];
        }
        if (et == null || (!typeof(IORMEntity).IsAssignableFrom(et) && ORMEntityAttribute.Of(et) == null)) return false;
        elementType = et;
        return true;
    }

    private static bool FieldPassesOperatorFilter(Type? t, string? op)
    {
        if (op == null) return true;
        if (op == JoinOperator.Like) return t == null || t == typeof(string);
        if (op is JoinOperator.GreaterThan or JoinOperator.GreaterOrEqual
               or JoinOperator.LessThan    or JoinOperator.LessOrEqual)
            return t == null || t == typeof(string) || OrmFieldHelper.IsNumericType(t) || OrmFieldHelper.IsDateTimeType(t);
        return true;
    }

    private static bool FieldPassesValueTypeFilter(Type? t, string? vt)
    {
        if (vt == null || vt == FilterValueType.StepInput) return true;
        if (UnaryValueTypes.Contains(vt))
        {
            bool strOnly = vt is FilterValueType.IsEmpty    or FilterValueType.IsNotEmpty
                                or FilterValueType.IsNullOrEmpty or FilterValueType.IsNotNullOrEmpty;
            return !strOnly || t == null || t == typeof(string);
        }
        return vt switch
        {
            FilterValueType.StringValue   => t == null || t == typeof(string),
            FilterValueType.NumberValue   => t == null || OrmFieldHelper.IsNumericType(t),
            FilterValueType.BoolValue     => t == null || t == typeof(bool),
            FilterValueType.DateTimeValue => t == null || OrmFieldHelper.IsDateTimeType(t),
            FilterValueType.GuidValue     => t == null || t == typeof(Guid),
            _                             => true
        };
    }

    /// <summary>
    /// Walks a dot-separated field path from <paramref name="startType"/> and returns the terminal field's .NET type.
    /// E.g. startType=Type2, path="NestedRef.SomeField" resolves Type2.NestedRef → Type3, then returns the type of Type3.SomeField.
    /// </summary>
    public static Type? ResolveEntityRefPathTerminalType(Type startType, string? dotPath)
    {
        if (dotPath == null) return null;
        var current = startType;
        foreach (var part in dotPath.Split('.'))
        {
            var next = OrmFieldHelper.GetFieldNetType(current.FullName, part);
            if (next == null) return null;
            current = next;
        }
        return current;
    }

    /// <summary>
    /// Recursively enumerates all primitive ORM-field paths reachable from <paramref name="refType"/>
    /// by following entity-ref navigations, using dot notation for multi-hop paths.
    /// </summary>
    private static IEnumerable<string> GetSubFieldPaths(Type refType, string prefix, int depth, HashSet<string> visited)
    {
        if (depth <= 0 || !visited.Add(refType.FullName ?? refType.Name)) yield break;

        foreach (var a in DataUtilities.GetDataMemberAccessorsForClass(refType, cache: true, publicOnly: false))
        {
            var at = OrmFieldHelper.GetAccessorNetType(a);
            if (at == null) continue;
            if (IsORMCollectionType(at, out _)) continue;

            var baseType = Nullable.GetUnderlyingType(at) ?? at;
            bool isEntityRef = !baseType.IsArray && !baseType.IsGenericType
                && (typeof(IORMEntity).IsAssignableFrom(baseType) || ORMEntityAttribute.Of(baseType) != null);

            string path = string.IsNullOrEmpty(prefix) ? a.Name : $"{prefix}.{a.Name}";

            if (isEntityRef)
            {
                foreach (var sub in GetSubFieldPaths(baseType, path, depth - 1, new HashSet<string>(visited)))
                    yield return sub;
            }
            else if (ORMFieldAttribute.Of(a) != null || a.Target?.GetCustomAttribute<ORMPrimaryKeyFieldAttribute>() != null)
            {
                yield return path;
            }
        }
    }

    // Cache: entity type → FK column names defined on OTHER types via ORMOneToManyRelationship<T>.
    private static readonly ConcurrentDictionary<Type, string[]> InverseFKFieldCache = new();

    /// <summary>
    /// Scans all loaded assemblies for ORM entity types that declare an
    /// <c>ORMOneToManyRelationship&lt;entityType&gt;</c> and returns the FK column
    /// names those relationships write into <paramref name="entityType"/>'s table.
    /// Results are cached per entity type.
    /// </summary>
    private static string[] GetAllInverseFKFieldNames(Type entityType)
    {
        return InverseFKFieldCache.GetOrAdd(entityType, t =>
        {
            var result = new List<string>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch { continue; }

                foreach (var ownerType in types)
                {
                    if (ORMEntityAttribute.Of(ownerType) == null) continue;
                    foreach (var acc in DataUtilities.GetDataMemberAccessorsForClass(
                        ownerType, cache: true, publicOnly: false))
                    {
                        if (!typeof(IORMOneToManyRelationship).IsAssignableFrom(acc.DataType)) continue;
                        var args = acc.DataType.GetGenericArguments();
                        if (args.Length == 0 || !args[0].IsAssignableFrom(t)) continue;
                        try
                        {
                            if (ownerType.GetConstructor(Type.EmptyTypes) == null) continue;
                            var owner = Activator.CreateInstance(ownerType);
                            if (acc.GetValue(owner) is IORMOneToManyRelationship rel
                                && !string.IsNullOrWhiteSpace(rel.FieldName))
                                result.Add(rel.FieldName);
                        }
                        catch { /* skip inaccessible types */ }
                    }
                }
            }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        });
    }

}
