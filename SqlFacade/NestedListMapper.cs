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
    using System.Text.Json.Serialization;

    /// <summary>
    /// Maps query rows that include <see cref="NestedList"/> JSON columns onto parent DTOs with typed list properties.
    /// </summary>
    internal static class NestedListMapper
    {
        private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

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

            if (underlying == typeof(Guid))
            {
                if (raw is string guidText)
                    return Guid.Parse(guidText);
                if (raw is Guid g)
                    return g;
            }

            if (underlying.IsEnum)
            {
                if (raw is string enumText)
                    return Enum.Parse(underlying, enumText, ignoreCase: true);
                return Enum.ToObject(underlying, raw);
            }

            if (underlying == typeof(DateTime))
                return ParseDateTime(raw);

            if (underlying == typeof(DateOnly))
                return ParseDateOnly(raw);

            if (underlying == typeof(TimeOnly))
                return ParseTimeOnly(raw);

            if (underlying == typeof(bool))
            {
                if (raw is bool b)
                    return b;
                if (raw is long l)
                    return l != 0;
                if (raw is int i)
                    return i != 0;
                if (raw is string bs)
                {
                    if (bs == "1" || bs.Equals("true", StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (bs == "0" || bs.Equals("false", StringComparison.OrdinalIgnoreCase))
                        return false;
                    return bool.Parse(bs);
                }
            }

            if (raw is string s)
            {
                if (underlying == typeof(decimal))
                    return decimal.Parse(s, CultureInfo.InvariantCulture);
                if (underlying == typeof(double))
                    return double.Parse(s, CultureInfo.InvariantCulture);
                if (underlying == typeof(float))
                    return float.Parse(s, CultureInfo.InvariantCulture);
                if (underlying == typeof(int))
                    return int.Parse(s, CultureInfo.InvariantCulture);
                if (underlying == typeof(long))
                    return long.Parse(s, CultureInfo.InvariantCulture);
            }

            return Convert.ChangeType(raw, underlying, CultureInfo.InvariantCulture);
        }

        private static DateTime ParseDateTime(object raw)
        {
            if (raw is DateTime dt)
                return dt;
            if (raw is DateTimeOffset dto)
                return dto.UtcDateTime;
            string text = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim();
            if (string.IsNullOrEmpty(text))
                throw new FormatException("Empty DateTime value.");
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
                return parsed;
            // SQLite often emits "yyyy-MM-dd HH:mm:ss" without a T separator.
            if (DateTime.TryParseExact(
                    text,
                    new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.fff", "yyyy-MM-dd" },
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out parsed))
                return parsed;
            return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        private static DateOnly ParseDateOnly(object raw)
        {
            if (raw is DateOnly d)
                return d;
            if (raw is DateTime dt)
                return DateOnly.FromDateTime(dt);
            string text = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim();
            if (string.IsNullOrEmpty(text))
                throw new FormatException("Empty DateOnly value.");
            if (DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly dateOnly))
                return dateOnly;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
                return DateOnly.FromDateTime(parsed);
            return DateOnly.Parse(text, CultureInfo.InvariantCulture);
        }

        private static TimeOnly ParseTimeOnly(object raw)
        {
            if (raw is TimeOnly t)
                return t;
            if (raw is TimeSpan ts)
                return TimeOnly.FromTimeSpan(ts);
            string text = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim();
            if (string.IsNullOrEmpty(text))
                throw new FormatException("Empty TimeOnly value.");
            if (TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly timeOnly))
                return timeOnly;
            return TimeOnly.Parse(text, CultureInfo.InvariantCulture);
        }

        private static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            options.Converters.Add(new FlexibleDateTimeConverter());
            options.Converters.Add(new FlexibleNullableDateTimeConverter());
            options.Converters.Add(new FlexibleDateOnlyConverter());
            options.Converters.Add(new FlexibleNullableDateOnlyConverter());
            return options;
        }

        private sealed class FlexibleDateTimeConverter : JsonConverter<DateTime>
        {
            public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.String)
                    return ParseDateTime(reader.GetString());
                if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out long unix))
                    return DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
                throw new JsonException($"Unexpected token {reader.TokenType} for DateTime.");
            }

            public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
                writer.WriteStringValue(value.ToString("O", CultureInfo.InvariantCulture));
        }

        private sealed class FlexibleNullableDateTimeConverter : JsonConverter<DateTime?>
        {
            public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null)
                    return null;
                if (reader.TokenType == JsonTokenType.String)
                {
                    string s = reader.GetString();
                    return string.IsNullOrWhiteSpace(s) ? null : ParseDateTime(s);
                }
                if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out long unix))
                    return DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
                throw new JsonException($"Unexpected token {reader.TokenType} for DateTime?.");
            }

            public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
            {
                if (value.HasValue)
                    writer.WriteStringValue(value.Value.ToString("O", CultureInfo.InvariantCulture));
                else
                    writer.WriteNullValue();
            }
        }

        private sealed class FlexibleDateOnlyConverter : JsonConverter<DateOnly>
        {
            public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.String)
                    return ParseDateOnly(reader.GetString());
                throw new JsonException($"Unexpected token {reader.TokenType} for DateOnly.");
            }

            public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options) =>
                writer.WriteStringValue(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        private sealed class FlexibleNullableDateOnlyConverter : JsonConverter<DateOnly?>
        {
            public override DateOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null)
                    return null;
                if (reader.TokenType == JsonTokenType.String)
                {
                    string s = reader.GetString();
                    return string.IsNullOrWhiteSpace(s) ? null : ParseDateOnly(s);
                }
                throw new JsonException($"Unexpected token {reader.TokenType} for DateOnly?.");
            }

            public override void Write(Utf8JsonWriter writer, DateOnly? value, JsonSerializerOptions options)
            {
                if (value.HasValue)
                    writer.WriteStringValue(value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                else
                    writer.WriteNullValue();
            }
        }
    }
}
