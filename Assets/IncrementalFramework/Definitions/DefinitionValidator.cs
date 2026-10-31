using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.Core;

namespace IncrementalFramework.Definitions
{
    public static class DefinitionValidator
    {
        public static void Validate(ActionDefinition action,IEnumerable<Entity> schemas,ExtensionRegistry extensions=null)
        {
            var candidates=schemas.ToArray();extensions=extensions??new ExtensionRegistry();
            foreach(var id in action.Requirement.RequiredExtensions.Concat(action.Effects.SelectMany(x=>x.ExtensionIds)))
                if(!extensions.Contains(id))throw new ArgumentException(action.Id+": missing extension "+id);
            foreach(var reference in action.Requirement.References.Concat(action.Effects.SelectMany(x=>x.References)))
                if(!candidates.Any(entity=>entity.Values.TryGetValue(reference.Name,out var value)&&value.DataType==reference.Type))
                    throw new ArgumentException(action.Id+": unknown/incompatible property "+reference.Name);
        }
    }
}
