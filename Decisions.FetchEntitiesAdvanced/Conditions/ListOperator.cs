namespace Decisions.FetchEntitiesAdvanced.Conditions;

public static class ListOperator
{
    public const string Contains       = "List Contains";
    public const string DoesNotContain = "List Does Not Contain";
    public const string FirstInList    = "First In List";
    public const string LastInList     = "Last In List";

    // Aggregate comparisons — no sub-field required
    public const string CountEq  = "Count =";
    public const string CountNe  = "Count ≠";
    public const string CountGt  = "Count >";
    public const string CountGte = "Count ≥";
    public const string CountLt  = "Count <";
    public const string CountLte = "Count ≤";

    // Aggregate comparisons — sub-field required
    public const string SumOf = "Sum Of";
    public const string AvgOf = "Average Of";
    public const string MinOf = "Min Of";
    public const string MaxOf = "Max Of";
}
