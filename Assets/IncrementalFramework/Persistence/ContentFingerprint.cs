using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IncrementalFramework
{
    public static class ContentFingerprint
    {
        public const string CurrentVersion = "1";

        public static string Compute(ContentDatabase content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            var serializer = JsonSerializer.Create(SnapshotService.CreateSettings());
            var root = JObject.FromObject(content, serializer);
            // Rule actions are a closed discriminated union, not CLR type metadata.
            var rules = (JArray)root["rules"];
            for (int i = 0; i < content.Rules.Count; i++)
            {
                var actions = (JArray)rules[i]["actions"];
                for (int j = 0; j < content.Rules[i].Actions.Count; j++)
                {
                    RuleAction action = content.Rules[i].Actions[j];
                    string kind = action is GrantResourceAction ? "grantResource"
                        : action is UnlockProducerAction ? "unlockProducer"
                        : action is UnlockUpgradeAction ? "unlockUpgrade"
                        : action is SpendResourceAction ? "spendResource"
                        : action is SetFlagAction ? "setFlag"
                        : action is ScheduleGrantResourceAction ? "scheduleGrantResource"
                        : action is SetModifierAction ? "setModifier"
                        : throw new ArgumentException("Unknown rule action.");
                    ((JObject)actions[j]).Add("kind", kind);
                }
            }
            string canonical = Canonicalize(root).ToString(Formatting.None);
            using (var hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(CurrentVersion + "\n" + canonical));
                var result = new StringBuilder(bytes.Length * 2);
                foreach (byte value in bytes) result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        private static JToken Canonicalize(JToken token)
        {
            if (token is JObject obj)
            {
                var result = new JObject();
                foreach (var property in obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                    result.Add(property.Name, Canonicalize(property.Value));
                return result;
            }
            if (token is JArray array) return new JArray(array.Select(Canonicalize));
            return token.DeepClone();
        }
    }
}
