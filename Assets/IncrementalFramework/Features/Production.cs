using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;

namespace IncrementalFramework.Features
{
    public sealed class RecipeDefinition
    {
        public string Id { get; set; }
        public string Machine { get; set; }
        public string RequiredSection { get; set; }
        public double Seconds { get; set; }
        public Dictionary<string, BigDouble> Inputs { get; set; } = new Dictionary<string, BigDouble>();
        public Dictionary<string, BigDouble> Outputs { get; set; } = new Dictionary<string, BigDouble>();
    }
    public sealed class ShipSectionDefinition
    {
        public string Id { get; set; }
        public Dictionary<string, BigDouble> Costs { get; set; }
    }
    public sealed class ProductionDefinition
    {
        public RecipeDefinition[] Recipes { get; set; }
        public ShipSectionDefinition[] Sections { get; set; }
        public string[] Machines { get; set; }
        public BigDouble MachineCost { get; set; } = 15;
        public string ConstructionResource { get; set; } = "plate";
        public double Capacity { get; set; } = 5000;
    }
    public sealed class MachineState
    {
        public string Id { get; set; }
        public string Kind { get; set; }
        public string Recipe { get; set; }
        public string ActiveRecipe { get; set; }
        public bool Enabled { get; set; } = true;
        public int Priority { get; set; }
        public string Blocked { get; set; }
    }
    public sealed class ProductionState : FeatureState
    {
        public Inventory Depot { get; set; } = new Inventory();
        public List<MachineState> Machines { get; set; } = new List<MachineState>();
        public List<string> Delivered { get; set; } = new List<string>();
        public int Launches { get; set; }
        public int NextMachine { get; set; } = 1;
    }
    public sealed class ProductionModule : FeatureModule<ProductionState>
    {
        public override string Id => "production";
        public ProductionDefinition Definition { get; }
        public ProductionModule(ProductionDefinition content) : base(Initial(content), content)
        { Definition = FeatureJson.Copy(content); ValidateState(State); }
        private static ProductionState Initial(ProductionDefinition d)
        {
            FeatureJson.Require(d?.Recipes != null && d.Machines?.Length > 0 && d.Sections?.Length > 0, "Missing production content.");
            FeatureJson.Require(d.Recipes.Select(r => r.Id).Distinct().Count() == d.Recipes.Length && d.Recipes.All(r => !string.IsNullOrEmpty(r.Id)
                && d.Machines.Contains(r.Machine) && FeatureJson.Finite(r.Seconds) && r.Seconds > 0 && r.Outputs.Count > 0
                && r.Inputs.Values.All(n => n > 0) && r.Outputs.Values.All(n => n > 0)), "Invalid recipes.");
            FeatureJson.Require(d.Sections.Select(s => s.Id).Distinct().Count() == d.Sections.Length && d.Sections.All(s => s.Costs.Count > 0 && s.Costs.Values.All(v => v > 0)), "Invalid ship sections.");
            var state = new ProductionState(); state.Depot.Capacity = d.Capacity;
            foreach (var kind in d.Machines.Take(2)) state.Machines.Add(new MachineState
            { Id = "machine:" + state.NextMachine++, Kind = kind, Recipe = d.Recipes.First(r => r.Machine == kind).Id });
            state.Schedule(0, "start"); return state;
        }
        public Dictionary<string,BigDouble> Costs(string sectionId,int launches) => Definition.Sections.Single(p=>p.Id==sectionId).Costs
            .ToDictionary(p=>p.Key,p=>p.Value*(launches%2==1?1.2:1));
        protected override string Act(ProductionState s, FeatureCommand c, double time)
        {
            if (c.Action == "buy")
            {
                if (!Definition.Machines.Contains(c.Target)) return "Unknown machine type.";
                BigDouble cost = Definition.MachineCost * BigDouble.Pow(1.5, s.Machines.Count(m => m.Kind == c.Target));
                if (!s.Depot.Take(new Dictionary<string, BigDouble> { [Definition.ConstructionResource] = cost })) return "Insufficient construction plates.";
                s.Machines.Add(new MachineState { Id = "machine:" + s.NextMachine++, Kind = c.Target,
                    Recipe = Definition.Recipes.First(r => r.Machine == c.Target).Id });
            }
            else if (c.Action == "deliver")
            {
                var part = Definition.Sections.FirstOrDefault(p => p.Id == c.Target);
                if (part == null || s.Delivered.Contains(part.Id)) return "Section unavailable or already delivered.";
                var costs = Costs(part.Id,s.Launches);
                if (!s.Depot.Take(costs)) return "Missing ship components.";
                s.Delivered.Add(part.Id); s.Won = s.Delivered.Count == Definition.Sections.Length;
            }
            else if (c.Action == "replay")
            {
                if (!s.Won) return "Launch the spacecraft first.";
                var initial = Initial(Definition);
                s.Machines = initial.Machines; s.Depot = initial.Depot; s.NextMachine = initial.NextMachine;
                s.Delivered.Clear(); s.Events.Clear(); s.Won = false; s.Launches++;
            }
            else
            {
                var machine = s.Machines.FirstOrDefault(m => m.Id == c.Target);
                if (machine == null) return "Unknown machine.";
                if (c.Action == "recipe")
                {
                    var recipe = Definition.Recipes.FirstOrDefault(r => r.Id == c.Argument && r.Machine == machine.Kind);
                    if (recipe == null || recipe.RequiredSection != null && !s.Delivered.Contains(recipe.RequiredSection)) return "Recipe is locked.";
                    machine.Recipe = recipe.Id;
                }
                else if (c.Action == "toggle") machine.Enabled = !machine.Enabled;
                else if (c.Action == "priority")
                { if(c.Amount<0 || c.Amount>10000 || c.Amount!=Math.Floor(c.Amount)) return "Priority must be a whole number from 0 to 10000."; machine.Priority=(int)c.Amount; }
                else return "Unknown production action.";
            }
            Start(s, time); s.Message = s.Won ? "Spacecraft launched! A new blueprint is available." : "Factory plan updated."; return null;
        }
        protected override void OnEvent(ProductionState s, FeatureEvent e, double time)
        {
            if (e.Kind == "complete")
            {
                var machine = s.Machines.Single(m => m.Id == e.Target);
                var recipe = Definition.Recipes.Single(r => r.Id == machine.ActiveRecipe);
                foreach (var output in recipe.Outputs)
                { s.Depot.Reserved -= output.Value; if (!s.Depot.Put(output.Key, output.Value)) throw new InvalidOperationException("Lost reserved factory output."); }
                machine.ActiveRecipe = null;
            }
            Start(s, time);
        }
        private void Start(ProductionState s, double time)
        {
            foreach (var m in s.Machines.OrderByDescending(m => m.Priority).ThenBy(m => m.Id, StringComparer.Ordinal))
            {
                m.Blocked = null;
                if (m.ActiveRecipe != null) continue;
                if (!m.Enabled) { m.Blocked = "Disabled"; continue; }
                var r = Definition.Recipes.Single(x => x.Id == m.Recipe);
                if (r.RequiredSection != null && !s.Delivered.Contains(r.RequiredSection)) { m.Blocked = "Deliver " + r.RequiredSection; continue; }
                if (!s.Depot.CanTake(r.Inputs)) { m.Blocked = "Missing " + string.Join(", ", r.Inputs.Where(p => s.Depot.Get(p.Key) < p.Value).Select(p => p.Key)); continue; }
                var incoming = r.Outputs.Values.Aggregate(BigDouble.Zero, (a, b) => a + b);
                var outgoing = r.Inputs.Values.Aggregate(BigDouble.Zero, (a, b) => a + b);
                if (s.Depot.Used - outgoing + s.Depot.Reserved + incoming > s.Depot.Capacity) { m.Blocked = "Storage full"; continue; }
                s.Depot.Take(r.Inputs); s.Depot.Reserved += incoming; m.ActiveRecipe = r.Id;
                s.Schedule(time + r.Seconds / (1 + s.Launches * 0.1), "complete", m.Id);
            }
        }
        protected override void ValidateState(ProductionState s)
        {
            base.ValidateState(s); s.Depot.Validate();
            FeatureJson.Require(s.Machines != null && s.Machines.All(m=>m!=null) && s.Machines.Select(m => m.Id).Distinct().Count() == s.Machines.Count && s.Delivered != null && s.Launches >= 0, "Invalid production state.");
            if(Definition==null)return;
            FeatureJson.Require(s.Machines.All(m=>Definition.Machines.Contains(m.Kind) && Definition.Recipes.Any(r=>r.Id==m.Recipe && r.Machine==m.Kind)
                && (m.ActiveRecipe==null || Definition.Recipes.Any(r=>r.Id==m.ActiveRecipe && r.Machine==m.Kind)))
                && s.Delivered.Distinct().Count()==s.Delivered.Count && s.Delivered.All(id=>Definition.Sections.Any(p=>p.Id==id)),"Unknown production references.");
            var reserved=s.Machines.Where(m=>m.ActiveRecipe!=null).SelectMany(m=>Definition.Recipes.Single(r=>r.Id==m.ActiveRecipe).Outputs.Values).Aggregate(BigDouble.Zero,(a,b)=>a+b);
            FeatureJson.Require(reserved==s.Depot.Reserved && s.Machines.All(m=>s.Events.Count(e=>e.Payload.Kind=="complete" && e.Payload.Target==m.Id)==(m.ActiveRecipe==null?0:1)),"Invalid batch reservations.");
        }
    }
}
