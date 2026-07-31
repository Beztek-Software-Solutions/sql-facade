// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using System.Text.Json;

    /// <summary>
    /// Maps query rows that include <see cref="NestedList"/> JSON columns onto parent DTOs with typed list properties.
    /// </summary>
    internal static class NestedListMapper
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public static List<T> Map<T>(IEnumerable<dynamic> rows, IList<NestedList> nestedLists)
        {
            var results = new List<T>();
            if (rows == null)
                return results;

            PropertyInfo[] properties = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.CanWrite)
                .ToArray();
            Dictionary<string, NestedList> byAlias = (nestedLists ?? Array.Empty<NestedList>())
                .Where(a => a != null && !string.IsNullOrWhiteSpace(a.ResultAlias))
                .ToDictionary(a => a.ResultAlias, a => a, StringComparer.OrdinalIgnoreCase);

            foreach (dynamic row in rows)
            {
                if (row is not IDictionary<string, object> dict)
                    throw new InvalidOperationException("NestedList mapping requires dictionary-style query rows.");

                T item = Activator.CreateInstance<T>();
                var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (NestedList nested in byAlias.Values)
                {
                    PropertyInfo prop = FindProperty(properties, nested.ResultAlias)
                        ?? throw new InvalidOperationException(
                            $"NestedList alias '{nested.ResultAlias}' has no matching writable property on {typeof(T).Name}.");
                    object jsonValue = FindColumnValue(dict, nested.ResultAlias);
                    object list = ParseList(nested.ElementType, jsonValue);
                    prop.SetValue(item, CoerceCollection(list, prop.PropertyType, nested.ElementType));
                    consumed.Add(nested.ResultAlias);
                }

                foreach (PropertyInfo prop in properties)
                {
                    if (consumed.Contains(prop.Name))
                        continue;
                    if (!TryFindColumnValue(dict, prop.Name, out object raw) || raw == null || raw is DBNull)
                        continue;
                    prop.SetValue(item, ConvertValue(raw, prop.PropertyType));
                }

                results.Add(item);
            }

            return results;
        }

        internal static object ParseList(Type elementType, object jsonValue)
        {
            if (elementType == null)
                throw new InvalidOperationException("NestedList.ElementType is required for typed list mapping.");

            Type listType = typeof(List<>).MakeGenericType(elementType);
            string json = jsonValue?.ToString();
            if (string.IsNullOrWhiteSpace(json) || json == "[]")
                return Activator.CreateInstance(listType);

            return JsonSerializer.Deserialize(json, listType, JsonOptions) ?? Activator.CreateInstance(listType);
        }

        private static object CoerceCollection(object list, Type propertyType, Type elementType)
        {
            if (propertyType.IsInstanceOfType(list))
                return list;

            if (propertyType.IsArray)
            {
                var asList = (IList)list;
                Array array = Array.CreateInstance(elementType, asList.Count);
                asList.CopyTo(array, 0);
                return array;
            }

            if (propertyType.IsAssignableFrom(typeof(List<>).MakeGenericType(elementType)))
                return list;

            throw new InvalidOperationException(
                $"Cannot assign List<{elementType.Name}> to property type {propertyType.Name}. Use List<T>, IList<T>, ICollection<T>, IEnumerable<T>, or T[].");
        }

        private static PropertyInfo FindProperty(PropertyInfo[] properties, string name) =>
            properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        private static object FindColumnValue(IDictionary<string, object> dict, string name)
        {
            TryFindColumnValue(dict, name, out object value);
            return value;
        }

        private static bool TryFindColumnValue(IDictionary<string, object> dict, string name, out object value)
        {
            foreach (KeyValuePair<string, object> pair in dict)
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value;
                    return true;
                }
            }
            value = null;
            return false;
        }

        private static object ConvertValue(object raw, Type targetType)
        {
            Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (raw == null || raw is DBNull)
                return null;

            if (underlying.IsInstanceOfType(raw))
                return raw;

            if (underlying == typeof(Guid) && raw is string guidText)
                return Guid.Parse(guidText);

            if (underlying.IsEnum)
            {
                if (raw is string enumText)
                    return Enum.Parse(underlying, enumText, ignoreCase: true);
                return Enum.ToObject(underlying, raw);
            }

            if (raw is string s && underlying == typeof(DateTime))
                return DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

            return Convert.ChangeType(raw, underlying, CultureInfo.InvariantCulture);
        }
    }
}
