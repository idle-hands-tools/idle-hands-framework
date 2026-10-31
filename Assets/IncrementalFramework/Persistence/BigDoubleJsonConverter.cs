using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using System;
using BreakInfinity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IncrementalFramework.Persistence
{
    /// <summary>Never converts a BigDouble through a double-sized magnitude or a formatted decimal string.</summary>
    public sealed class BigDoubleJsonConverter : JsonConverter<BigDouble>
    {
        public override void WriteJson(JsonWriter writer, BigDouble value, JsonSerializer serializer)
        {
            if (!Number.IsFinite(value)) throw new JsonSerializationException("Cannot save a nonfinite BigDouble.");
            writer.WriteStartObject();
            writer.WritePropertyName("mantissa"); writer.WriteValue(value.Mantissa);
            writer.WritePropertyName("exponent"); writer.WriteValue(value.Exponent);
            writer.WriteEndObject();
        }
        public override BigDouble ReadJson(JsonReader reader, Type objectType, BigDouble existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType != JsonToken.StartObject) throw new JsonSerializationException("Expected mantissa/exponent object.");
            var json = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            var mantissa = json["mantissa"]; var exponent = json["exponent"];
            if (json.Count != 2 || mantissa == null || exponent == null ||
                (mantissa.Type != JTokenType.Float && mantissa.Type != JTokenType.Integer) || exponent.Type != JTokenType.Integer)
                throw new JsonSerializationException("Invalid BigDouble representation.");
            double m;
            long e;
            try { m = mantissa.Value<double>(); e = exponent.Value<long>(); }
            catch (Exception error) when (error is OverflowException || error is FormatException || error is InvalidCastException)
            { throw new JsonSerializationException("Invalid BigDouble components.", error); }
            if (double.IsNaN(m) || double.IsInfinity(m) || (m != 0 && (Math.Abs(m) < 1 || Math.Abs(m) >= 10)))
                throw new JsonSerializationException("BigDouble mantissa is not normalized.");
            return BigDouble.FromMantissaExponentNoNormalize(m, e);
        }
    }
}
