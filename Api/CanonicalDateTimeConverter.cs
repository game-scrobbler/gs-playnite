using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using GsPlugin.Services;

namespace GsPlugin.Api {
    /// <summary>
    /// Serializes a <see cref="DateTime"/> as the exact string
    /// <see cref="GsHashUtils.FormatDateForHash"/> feeds into the snapshot hash:
    /// UTC, second precision, `Z`-suffixed.
    ///
    /// WHY THIS EXISTS. The snapshot hash is a claim about the payload, so the
    /// receiver has to be able to recompute it from the payload alone. With the
    /// default System.Text.Json behaviour it could not: STJ preserves
    /// <see cref="DateTimeKind"/> and emits round-trip format, so a Playnite
    /// timestamp serialized as `2025-02-19T14:51:26.897-08:00` while the value fed
    /// into the hash was `2025-02-19T22:51:26Z`. Any recipient then has to infer a
    /// normalization the payload does not state, and an offset-bearing timestamp
    /// is ambiguous the moment that inference differs by even one rule.
    ///
    /// Emitting the canonical form removes the inference: what the plugin hashes
    /// is what it sends, byte for byte, and the hash becomes independently
    /// verifiable.
    ///
    /// Scoped deliberately to the library DTO's date fields via
    /// [JsonConverter] attributes rather than registered globally, so no other
    /// endpoint's wire format changes.
    /// </summary>
    internal sealed class CanonicalDateTimeConverter : JsonConverter<DateTime?> {
        public override DateTime? Read(
            ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            if (reader.TokenType == JsonTokenType.Null) {
                return null;
            }
            var raw = reader.GetString();
            if (string.IsNullOrEmpty(raw)) {
                return null;
            }
            return DateTime.Parse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        public override void Write(
            Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options) {
            if (!value.HasValue) {
                writer.WriteNullValue();
                return;
            }
            writer.WriteStringValue(GsHashUtils.FormatDateForHash(value));
        }
    }
}
