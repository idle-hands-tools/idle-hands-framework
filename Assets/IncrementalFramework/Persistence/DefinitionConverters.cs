using System;
using System.Linq;
using IncrementalFramework.Economy;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IncrementalFramework.Persistence
{
    internal sealed class DefinitionConverter:JsonConverter
    {
        public override bool CanWrite=>false;
        public override bool CanConvert(Type type)=>type==typeof(ModifierDefinition)||type==typeof(ConditionDefinition);
        public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer)=>throw new NotSupportedException();
        public override object ReadJson(JsonReader reader,Type type,object existing,JsonSerializer serializer)
        {
            var row=JObject.Load(reader);
            if(type==typeof(ModifierDefinition))return new ModifierDefinition(row.Value<string>("id"),row["target"].ToObject<ModifierTarget>(serializer),row.Value<string>("targetId"),row["value"].ToObject<BreakInfinity.BigDouble>(serializer),row["kind"].ToObject<ModifierKind>(serializer),row.Value<int>("priority"));
            var kind=row["kind"].ToObject<ConditionKind>(serializer);
            switch(kind)
            {
                case ConditionKind.Requirement:return ConditionDefinition.Require(row["requirement"].ToObject<RequirementDefinition>(serializer));
                case ConditionKind.Flag:return ConditionDefinition.Flag(row.Value<string>("flagId"),row.Value<bool>("expected"));
                case ConditionKind.And:return ConditionDefinition.All(((JArray)row["children"]).Select(x=>x.ToObject<ConditionDefinition>(serializer)).ToArray());
                case ConditionKind.Or:return ConditionDefinition.Any(((JArray)row["children"]).Select(x=>x.ToObject<ConditionDefinition>(serializer)).ToArray());
                case ConditionKind.Not:return ConditionDefinition.Not(row["children"][0].ToObject<ConditionDefinition>(serializer));
                default:throw new JsonSerializationException("Unknown condition kind.");
            }
        }
    }
}
