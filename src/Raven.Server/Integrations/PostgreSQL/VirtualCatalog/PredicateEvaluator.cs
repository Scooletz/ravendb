using System;
using System.Collections.Generic;
using System.Globalization;
using Raven.Server.Integrations.PostgreSQL.Translation;

namespace Raven.Server.Integrations.PostgreSQL.VirtualCatalog
{
    // Walks the ParsedWhere IR produced by SqlWhereParser and evaluates it against a row
    // (object[]) interpreted via the table's column schema.
    internal static class PredicateEvaluator
    {
        public static bool TryEvaluate(ParsedWhere where, IReadOnlyList<PgVirtualColumn> columns, object[] row, out bool result)
        {
            result = false;

            switch (where)
            {
                case ParsedAnd and:
                    foreach (var child in and.Children)
                    {
                        if (TryEvaluate(child, columns, row, out var childResult) == false)
                            return false;
                        if (childResult == false)
                        {
                            result = false;
                            return true;
                        }
                    }
                    result = true;
                    return true;

                case ParsedOr or:
                    foreach (var child in or.Children)
                    {
                        if (TryEvaluate(child, columns, row, out var childResult) == false)
                            return false;
                        if (childResult)
                        {
                            result = true;
                            return true;
                        }
                    }
                    result = false;
                    return true;

                case ParsedNot not:
                    if (TryEvaluate(not.Child, columns, row, out var inner) == false)
                        return false;
                    result = !inner;
                    return true;

                case ParsedBinary binary:
                    return TryEvaluateBinary(binary, columns, row, out result);

                case ParsedIn inList:
                    return TryEvaluateIn(inList, columns, row, out result);

                case ParsedBetween between:
                    return TryEvaluateBetween(between, columns, row, out result);

                case ParsedIsNull isNull:
                    return TryEvaluateIsNull(isNull, columns, row, out result);

                default:
                    return false;
            }
        }

        private static bool TryEvaluateBinary(ParsedBinary binary, IReadOnlyList<PgVirtualColumn> columns, object[] row, out bool result)
        {
            result = false;

            if (TryResolveColumnValue(binary.FieldPath, columns, row, out var lhs) == false)
                return false;

            var cmp = VirtualValueComparer.Compare(lhs, binary.Value);
            if (cmp == null)
                return false;

            result = binary.Operator switch
            {
                "="  => cmp.Value == 0,
                "!=" => cmp.Value != 0,
                "<>" => cmp.Value != 0,
                "<"  => cmp.Value < 0,
                "<=" => cmp.Value <= 0,
                ">"  => cmp.Value > 0,
                ">=" => cmp.Value >= 0,
                _ => false,
            };
            return true;
        }

        private static bool TryEvaluateIn(ParsedIn inList, IReadOnlyList<PgVirtualColumn> columns, object[] row, out bool result)
        {
            result = false;

            if (TryResolveColumnValue(inList.FieldPath, columns, row, out var lhs) == false)
                return false;

            foreach (var candidate in inList.Values)
            {
                var cmp = VirtualValueComparer.Compare(lhs, candidate);
                if (cmp == null)
                    return false;
                if (cmp.Value == 0)
                {
                    result = inList.Negated ? false : true;
                    return true;
                }
            }

            result = inList.Negated;
            return true;
        }

        private static bool TryEvaluateBetween(ParsedBetween between, IReadOnlyList<PgVirtualColumn> columns, object[] row, out bool result)
        {
            result = false;

            if (TryResolveColumnValue(between.FieldPath, columns, row, out var lhs) == false)
                return false;

            var lowerCmp = VirtualValueComparer.Compare(lhs, between.Lower);
            var upperCmp = VirtualValueComparer.Compare(lhs, between.Upper);

            if (lowerCmp == null || upperCmp == null)
                return false;

            result = lowerCmp.Value >= 0 && upperCmp.Value <= 0;
            return true;
        }

        private static bool TryEvaluateIsNull(ParsedIsNull isNull, IReadOnlyList<PgVirtualColumn> columns, object[] row, out bool result)
        {
            result = false;

            if (TryResolveColumnValue(isNull.FieldPath, columns, row, out var lhs) == false)
                return false;

            var rowIsNull = lhs is null;
            result = isNull.Negated ? !rowIsNull : rowIsNull;
            return true;
        }

        private static bool TryResolveColumnValue(IReadOnlyList<string> fieldPath, IReadOnlyList<PgVirtualColumn> columns, object[] row, out object value)
        {
            value = null;
            if (fieldPath == null || fieldPath.Count == 0)
                return false;

            // Use the last segment as the column name. The IR strips outer table aliases up front;
            // anything left is either a bare column or "<alias>.<column>" and we only care about
            // the column portion for virtual tables.
            var name = fieldPath[^1];

            for (int i = 0; i < columns.Count; i++)
            {
                if (string.Equals(columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = row[i];
                    return true;
                }
            }

            return false;
        }
    }

    internal static class VirtualValueComparer
    {
        // Returns -1/0/+1 if comparable, null if not (caller treats null as "unable to evaluate").
        public static int? Compare(object lhs, ParsedValue rhs)
        {
            if (rhs == null)
                return null;

            if (lhs == null)
                return null;

            switch (rhs.Kind)
            {
                case ParsedValueKind.Null:
                    return null;

                case ParsedValueKind.String:
                case ParsedValueKind.Timestamp:
                {
                    var l = lhs.ToString();
                    var r = rhs.Raw?.ToString();
                    return string.CompareOrdinal(l, r);
                }

                case ParsedValueKind.Long:
                {
                    if (TryToLong(lhs, out var l) && rhs.Raw is long r)
                        return l.CompareTo(r);
                    return null;
                }

                case ParsedValueKind.Double:
                {
                    if (TryToDouble(lhs, out var l) && rhs.Raw is double r)
                        return l.CompareTo(r);
                    return null;
                }

                case ParsedValueKind.Bool:
                {
                    if (lhs is bool lb && rhs.Raw is bool rb)
                        return lb.CompareTo(rb);
                    return null;
                }

                default:
                    return null;
            }
        }

        private static bool TryToLong(object value, out long result)
        {
            switch (value)
            {
                case long l: result = l; return true;
                case int i: result = i; return true;
                case short s: result = s; return true;
                case byte b: result = b; return true;
                case string str when long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                    result = parsed;
                    return true;
                default:
                    result = 0;
                    return false;
            }
        }

        private static bool TryToDouble(object value, out double result)
        {
            switch (value)
            {
                case double d: result = d; return true;
                case float f: result = f; return true;
                case long l: result = l; return true;
                case int i: result = i; return true;
                case string str when double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                    result = parsed;
                    return true;
                default:
                    result = 0;
                    return false;
            }
        }
    }
}
