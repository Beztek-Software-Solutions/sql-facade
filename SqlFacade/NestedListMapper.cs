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

        /// <summary>Exposed for unit tests that round-trip NestedList JSON converters (incl. Write).</summary>
        internal static JsonSerializerOptions SharedJsonOptions => JsonOptions;

        public static List<T> Map<T>(IEnumerable<dynamic> rows, IList<NestedList> nestedLists)
        {
            var results = new List<T>();
            if (rows == null)
                return results;

            PropertyInfo[] properties = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.CanWrite)
                .ToArray();
            Dictionary<string, NestedList> byAlias = IndexNestedLists(nestedLists);

            foreach (dynamic row in rows)
                results.Add(MapRow<T>((object)row, properties, byAlias));

            return results;
        }

        private static Dictionary<string, NestedList> IndexNestedLists(IList<NestedList> nestedLists) =>
            (nestedLists ?? Array.Empty<NestedList>())
                .Where(a => a != null && !string.IsNullOrWhiteSpace(a.ResultAlias))
                .ToDictionary(a => a.ResultAlias, a => a, StringComparer.OrdinalIgnoreCase);

        private static T MapRow<T>(
            object row, PropertyInfo[] properties, Dictionary<string, NestedList> byAlias)
        {
            if (row is not IDictionary<string, object> dict)
                throw new InvalidOperationException("NestedList mapping requires dictionary-style query rows.");

            T item = Activator.CreateInstance<T>();
            var consumed = MapNestedProperties(item, dict, properties, byAlias);
            MapScalarProperties(item, dict, properties, consumed);
            return item;
        }

        private static HashSet<string> MapNestedProperties<T>(
            T item,
            IDictionary<string, object> dict,
            PropertyInfo[] properties,
            Dictionary<string, NestedList> byAlias)
        {
            var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (NestedList nested in byAlias.Values)
            {
                PropertyInfo prop = FindProperty(properties, nested.ResultAlias)
                    ?? throw new InvalidOperationException(
                        $"NestedList alias '{nested.ResultAlias}' has no matching writable property on {typeof(T).Name}.");
                object list = ParseList(nested.ElementType, FindColumnValue(dict, nested.ResultAlias));
                prop.SetValue(item, CoerceCollection(list, prop.PropertyType, nested.ElementType));
                consumed.Add(nested.ResultAlias);
            }
            return consumed;
        }

        private static void MapScalarProperties<T>(
            T item,
            IDictionary<string, object> dict,
            PropertyInfo[] properties,
            HashSet<string> consumed)
        {
            foreach (PropertyInfo prop in properties)
            {
                if (consumed.Contains(prop.Name))
                    continue;
                if (!TryFindColumnValue(dict, prop.Name, out object raw) || raw == null || raw is DBNull)
                    continue;
                prop.SetValue(item, ConvertValue(raw, prop.PropertyType));
            }
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
            if (TryConvertKnownType(raw, underlying, out object converted))
                return converted;
            if (raw is string s)
                return ConvertStringToPrimitive(s, underlying);
            return Convert.ChangeType(raw, underlying, CultureInfo.InvariantCulture);
        }

        private static bool TryConvertKnownType(object raw, Type underlying, out object converted)
        {
            converted = null;
            if (underlying == typeof(Guid))
            {
                converted = ConvertToGuid(raw);
                return true;
            }
            if (underlying.IsEnum)
            {
                converted = ConvertToEnum(raw, underlying);
                return true;
            }
            if (underlying == typeof(DateTime))
            {
                converted = ParseDateTime(raw);
                return true;
            }
            if (underlying == typeof(DateOnly))
            {
                converted = ParseDateOnly(raw);
                return true;
            }
            if (underlying == typeof(TimeOnly))
            {
                converted = ParseTimeOnly(raw);
                return true;
            }
            if (underlying == typeof(bool))
            {
                converted = ParseBool(raw);
                return true;
            }
            return false;
        }

        private static Guid ConvertToGuid(object raw)
        {
            if (raw is string guidText)
                return Guid.Parse(guidText);
            if (raw is Guid g)
                return g;
            return (Guid)Convert.ChangeType(raw, typeof(Guid), CultureInfo.InvariantCulture);
        }

        private static object ConvertToEnum(object raw, Type enumType)
        {
            if (raw is string enumText)
                return Enum.Parse(enumType, enumText, ignoreCase: true);
            return Enum.ToObject(enumType, raw);
        }

        private static object ConvertStringToPrimitive(string s, Type underlying)
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
            return Convert.ChangeType(s, underlying, CultureInfo.InvariantCulture);
        }

        private static readonly string[] DateTimeExactFormats =
        [
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss.fffffff",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss.fff",
            "yyyy-MM-ddTHH:mm:ss.fffffff",
            "yyyy-MM-ddTHH:mm:ssZ",
            "yyyy-MM-ddTHH:mm:ss.fffZ",
            "yyyy-MM-ddTHH:mm:ss.fffffffZ",
            "yyyy-MM-dd HH:mm:sszzz",
            "yyyy-MM-dd HH:mm:ss.fffzzz",
            "yyyy-MM-ddTHH:mm:sszzz",
            "yyyy-MM-ddTHH:mm:ss.fffzzz",
            "yyyy-MM-dd"
        ];

        private static DateTime ParseDateTime(object raw)
        {
            if (TryParseDateTimeBinary(raw, out DateTime binary))
                return binary;

            string text = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim();
            if (string.IsNullOrEmpty(text))
                throw new FormatException("Empty DateTime value.");
            if (TryParseDateTimeText(text, out DateTime parsed))
                return parsed;
            throw new FormatException($"Unrecognized DateTime value: '{text}'.");
        }

        private static bool TryParseDateTimeBinary(object raw, out DateTime value)
        {
            value = default;
            if (raw is DateTime dt)
            {
                value = NormalizeUtcDateTime(dt);
                return true;
            }
            if (raw is DateTimeOffset dto)
            {
                value = dto.UtcDateTime;
                return true;
            }
            return false;
        }

        private static DateTime NormalizeUtcDateTime(DateTime dt) =>
            DateTime.SpecifyKind(
                dt.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                    : dt.ToUniversalTime(),
                DateTimeKind.Utc);

        private static bool TryParseBoolNumeric(object raw, out bool value)
        {
            value = false;
            if (raw is bool b)
            {
                value = b;
                return true;
            }
            if (raw is long or int)
            {
                value = Convert.ToInt64(raw) != 0;
                return true;
            }
            if (raw is double d)
            {
                value = Math.Abs(d) > double.Epsilon;
                return true;
            }
            return false;
        }

        private static bool TryParseDateTimeText(string text, out DateTime value)
        {
            // Offset-less SQLite / Postgres text is UTC wall-clock. Do not call DateTimeOffset.TryParse
            // first — without a designator it assumes local time and shifts the instant.
            if (DateTime.TryParseExact(
                    text,
                    DateTimeExactFormats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTime parsed))
            {
                value = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                return true;
            }

            if (HasExplicitTimezoneDesignator(text)
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset offsetParsed))
            {
                value = offsetParsed.UtcDateTime;
                return true;
            }

            if (DateTime.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out parsed))
            {
                value = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                return true;
            }

            value = default;
            return false;
        }

        private static bool HasExplicitTimezoneDesignator(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;
            if (text.EndsWith("Z", StringComparison.OrdinalIgnoreCase))
                return true;
            // Only inspect the time portion so YYYY-MM-DD dashes are not treated as offsets.
            int sep = text.IndexOf('T');
            if (sep < 0)
                sep = text.IndexOf(' ');
            if (sep < 0)
                return false;
            string timePart = text.Substring(sep + 1);
            return timePart.Contains('+')
                || timePart.Contains('-')
                || timePart.Contains("UTC", StringComparison.OrdinalIgnoreCase)
                || timePart.Contains("GMT", StringComparison.OrdinalIgnoreCase);
        }

        private static DateOnly ParseDateOnly(object raw)
        {
            if (TryParseDateOnlyBinary(raw, out DateOnly binary))
                return binary;
            string text = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim();
            if (string.IsNullOrEmpty(text))
                throw new FormatException("Empty DateOnly value.");
            return ParseDateOnlyText(text);
        }

        private static bool TryParseDateOnlyBinary(object raw, out DateOnly value)
        {
            value = default;
            if (raw is DateOnly d)
            {
                value = d;
                return true;
            }
            if (raw is DateTime dt)
            {
                value = DateOnly.FromDateTime(dt);
                return true;
            }
            return false;
        }

        private static DateOnly ParseDateOnlyText(string text)
        {
            if (DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly dateOnly))
                return dateOnly;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed))
                return DateOnly.FromDateTime(parsed);
            return DateOnly.Parse(text, CultureInfo.InvariantCulture);
        }

        private static TimeOnly ParseTimeOnly(object raw)
        {
            if (TryParseTimeOnlyBinary(raw, out TimeOnly binary))
                return binary;
            string text = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim();
            if (string.IsNullOrEmpty(text))
                throw new FormatException("Empty TimeOnly value.");
            if (TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly timeOnly))
                return timeOnly;
            return TimeOnly.Parse(text, CultureInfo.InvariantCulture);
        }

        private static bool TryParseTimeOnlyBinary(object raw, out TimeOnly value)
        {
            value = default;
            if (raw is TimeOnly t)
            {
                value = t;
                return true;
            }
            if (raw is TimeSpan ts)
            {
                value = TimeOnly.FromTimeSpan(ts);
                return true;
            }
            return false;
        }

        private static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            options.Converters.Add(new FlexibleBoolConverter());
            options.Converters.Add(new FlexibleNullableBoolConverter());
            options.Converters.Add(new FlexibleDecimalConverter());
            options.Converters.Add(new FlexibleNullableDecimalConverter());
            options.Converters.Add(new FlexibleDateTimeConverter());
            options.Converters.Add(new FlexibleNullableDateTimeConverter());
            options.Converters.Add(new FlexibleDateOnlyConverter());
            options.Converters.Add(new FlexibleNullableDateOnlyConverter());
            // Postgres historically cast NestedList aggregates to text; grandchild arrays then
            // appear as JSON strings inside parent objects. Accept both arrays and stringified arrays.
            options.Converters.Add(new FlexibleObjectListConverterFactory());
            return options;
        }

        private sealed class FlexibleObjectListConverterFactory : JsonConverterFactory
        {
            public override bool CanConvert(Type typeToConvert) =>
                typeToConvert.IsGenericType
                && typeToConvert.GetGenericTypeDefinition() == typeof(List<>);

            public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
            {
                Type elementType = typeToConvert.GetGenericArguments()[0];
                return (JsonConverter)Activator.CreateInstance(
                    typeof(FlexibleObjectListConverter<>).MakeGenericType(elementType));
            }
        }

        private sealed class FlexibleObjectListConverter<T> : JsonConverter<List<T>>
        {
            // ElementOptions includes this factory so nested list properties can accept stringified JSON arrays.
            private static readonly JsonSerializerOptions ElementOptions = CreateElementOptions(includeListFactory: true);
            private static readonly JsonSerializerOptions WriteOptions = CreateElementOptions(includeListFactory: false);

            private static JsonSerializerOptions CreateElementOptions(bool includeListFactory)
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                options.Converters.Add(new FlexibleBoolConverter());
                options.Converters.Add(new FlexibleNullableBoolConverter());
                options.Converters.Add(new FlexibleDecimalConverter());
                options.Converters.Add(new FlexibleNullableDecimalConverter());
                options.Converters.Add(new FlexibleDateTimeConverter());
                options.Converters.Add(new FlexibleNullableDateTimeConverter());
                options.Converters.Add(new FlexibleDateOnlyConverter());
                options.Converters.Add(new FlexibleNullableDateOnlyConverter());
                if (includeListFactory)
                    options.Converters.Add(new FlexibleObjectListConverterFactory());
                return options;
            }

            public override List<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                reader.TokenType switch
                {
                    JsonTokenType.Null => null,
                    JsonTokenType.String => ReadStringifiedList(reader.GetString()),
                    JsonTokenType.StartArray => ReadArrayElements(ref reader),
                    _ => throw new JsonException($"Unexpected token {reader.TokenType} for List<{typeof(T).Name}>.")
                };

            private static List<T> ReadStringifiedList(string s)
            {
                if (string.IsNullOrWhiteSpace(s) || s == "[]")
                    return new List<T>();
                return JsonSerializer.Deserialize<List<T>>(s, ElementOptions) ?? new List<T>();
            }

            private static List<T> ReadArrayElements(ref Utf8JsonReader reader)
            {
                // Manually walk the array so nested List<> properties still use this converter
                // (JsonSerializer.Deserialize<List<T>> would re-enter and recurse).
                var list = new List<T>();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                        break;
                    list.Add(JsonSerializer.Deserialize<T>(ref reader, ElementOptions));
                }
                return list;
            }

            public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options) =>
                JsonSerializer.Serialize(writer, value, WriteOptions);
        }

        private static bool ParseBool(object raw)
        {
            if (TryParseBoolNumeric(raw, out bool numeric))
                return numeric;
            return ParseBoolText(Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim());
        }

        private static bool ParseBoolText(string text)
        {
            if (string.IsNullOrEmpty(text))
                throw new FormatException("Empty Boolean value.");
            if (text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (text == "0" || text.Equals("false", StringComparison.OrdinalIgnoreCase))
                return false;
            return bool.Parse(text);
        }

        private sealed class FlexibleBoolConverter : JsonConverter<bool>
        {
            public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                reader.TokenType switch
                {
                    JsonTokenType.True => true,
                    JsonTokenType.False => false,
                    JsonTokenType.Number => reader.TryGetInt64(out long n) ? n != 0 : reader.GetDouble() != 0,
                    JsonTokenType.String => ParseBool(reader.GetString()),
                    _ => throw new JsonException($"Unexpected token {reader.TokenType} for Boolean.")
                };

            public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) =>
                writer.WriteBooleanValue(value);
        }

        private sealed class FlexibleNullableBoolConverter : JsonConverter<bool?>
        {
            // Route JSON null through Read (STJ otherwise skips converters for nullable nulls).
            public override bool HandleNull => true;

            public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null)
                    return null;
                return new FlexibleBoolConverter().Read(ref reader, typeof(bool), options);
            }

            public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
            {
                if (value.HasValue)
                    writer.WriteBooleanValue(value.Value);
                else
                    writer.WriteNullValue();
            }
        }

        private sealed class FlexibleDecimalConverter : JsonConverter<decimal>
        {
            public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                reader.TokenType switch
                {
                    JsonTokenType.Number => reader.GetDecimal(),
                    JsonTokenType.String => decimal.Parse(reader.GetString()!, CultureInfo.InvariantCulture),
                    _ => throw new JsonException($"Unexpected token {reader.TokenType} for Decimal.")
                };

            public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
                writer.WriteNumberValue(value);
        }

        private sealed class FlexibleNullableDecimalConverter : JsonConverter<decimal?>
        {
            public override bool HandleNull => true;

            public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null)
                    return null;
                return new FlexibleDecimalConverter().Read(ref reader, typeof(decimal), options);
            }

            public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
            {
                if (value.HasValue)
                    writer.WriteNumberValue(value.Value);
                else
                    writer.WriteNullValue();
            }
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
            public override bool HandleNull => true;

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
            public override bool HandleNull => true;

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
