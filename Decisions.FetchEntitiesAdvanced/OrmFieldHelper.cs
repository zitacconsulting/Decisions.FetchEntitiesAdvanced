using System.Globalization;
using System.Reflection;
using DecisionsFramework.Utilities;
using DecisionsFramework.Utilities.Data;

namespace Decisions.FetchEntitiesAdvanced;

/// <summary>
/// Shared ORM field reflection helpers used by both FilterNode and FieldMapping.
/// Centralises the four methods that were previously duplicated verbatim in both classes.
/// </summary>
internal static class OrmFieldHelper
{
    internal static Type? GetFieldNetType(string? typeName, string? fieldName)
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
            if (rawType != null) return Nullable.GetUnderlyingType(rawType) ?? rawType;
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

    internal static Type? GetAccessorNetType(object accessor)
    {
        var targetProp = accessor.GetType().GetProperty("Target");
        var target     = targetProp?.GetValue(accessor);
        var rawType    = (target as PropertyInfo)?.PropertyType ?? (target as FieldInfo)?.FieldType;
        return rawType == null ? null : Nullable.GetUnderlyingType(rawType) ?? rawType;
    }

    internal static bool IsNumericType(Type? t) =>
        t != null && (t == typeof(int)     || t == typeof(long)    || t == typeof(short)   || t == typeof(byte)   ||
                      t == typeof(float)   || t == typeof(double)  || t == typeof(decimal) ||
                      t == typeof(uint)    || t == typeof(ulong)   || t == typeof(ushort));

    internal static bool IsDateTimeType(Type? t) =>
        t != null && (t == typeof(DateTime) || t == typeof(DateTimeOffset));

    /// <summary>
    /// Converts one string entry of an In List / Not In List value to the compared field's type
    /// (double for numerics, DateTime, Guid, otherwise string). Returns false for null or unparsable entries.
    /// </summary>
    internal static bool TryConvertListEntry(string? entry, Type? fieldType, out object value)
    {
        value = string.Empty;
        if (entry == null) return false;
        if (IsNumericType(fieldType))
        {
            // TryParse accepts "NaN"/"Infinity", which can't be embedded as SQL numbers
            if (!double.TryParse(entry, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                || !double.IsFinite(d)) return false;
            value = d;
            return true;
        }
        if (IsDateTimeType(fieldType))
        {
            if (!DateTime.TryParse(entry, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) return false;
            value = dt;
            return true;
        }
        if (fieldType == typeof(Guid))
        {
            if (!Guid.TryParse(entry, out var g)) return false;
            value = g;
            return true;
        }
        value = entry;
        return true;
    }
}
