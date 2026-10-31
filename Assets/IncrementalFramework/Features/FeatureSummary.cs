using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;

namespace IncrementalFramework.Features
{
    public static class FeatureSummary
    {
        public static Dictionary<string, BigDouble> Capture(IFeatureModule module)
        {
            var result = new Dictionary<string,BigDouble>();
            void Inventory(string prefix, Inventory inventory) { foreach(var item in inventory.Items) result[prefix+item.Key]=item.Value; }
            if(module is ProductionModule p) Inventory("",p.State.Depot);
            else if(module is LogisticsModule l)
            {
                var state=l.State;result["funds"]=state.Cash;
                foreach(var station in state.Stations) Inventory(station.Key+"/",station.Value);
                foreach(var train in state.Trains) Inventory(train.Id+"/",train.Cargo);
            }
            else if(module is EcologyModule e)
            {
                var state=e.State;result["funds"]=state.Funds;
                foreach(var zone in state.Habitats)foreach(var population in zone.Populations)result[zone.Id+"/"+population.Key]=population.Value;
            }
            else if(module is MarketModule m){result["cash"]=m.State.Cash;Inventory("",m.State.Warehouse);}
            return result;
        }
        public static string Describe(IDictionary<string,BigDouble> before, IFeatureModule module)
        {
            var after=Capture(module);
            var changes=before.Keys.Union(after.Keys).OrderBy(x=>x,StringComparer.Ordinal).Select(key=>
            {
                before.TryGetValue(key,out var old);after.TryGetValue(key,out var value);var delta=value-old;
                return delta==0?null:key+" "+(delta>0?"+":"")+delta.ToString("F1");
            }).Where(x=>x!=null).ToArray();
            return changes.Length==0?"No net inventory or population changes.":string.Join(" · ",changes);
        }
    }
}
