using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using IncrementalFramework.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace IncrementalFramework.Persistence
{
    public sealed class EntityContractResolver : CamelCasePropertyNamesContractResolver
    {
        public EntityContractResolver() { NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false, OverrideSpecifiedNames = false }; }
        protected override IList<JsonProperty> CreateProperties(Type type,MemberSerialization serialization)
        {
            var properties=base.CreateProperties(type,serialization);
            if(typeof(Entity).IsAssignableFrom(type))
                return properties.Where(x=>x.UnderlyingName!="Context"&&x.UnderlyingName!="Values"&&x.UnderlyingName!="TemplateId"&&x.UnderlyingName!="Kind").ToList();
            return properties;
        }
    }
}
