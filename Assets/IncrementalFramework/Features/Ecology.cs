using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;

namespace IncrementalFramework.Features
{
    public sealed class SpeciesDefinition
    {
        public string Id { get; set; }
        public double Growth { get; set; }
        public double Capacity { get; set; }
        public string Food { get; set; }
        public double Consumption { get; set; }
        public double HealthyMin { get; set; }
        public double HealthyMax { get; set; }
    }
    public sealed class EcologyDefinition
    {
        public SpeciesDefinition[] Species { get; set; }
        public string[] Zones { get; set; }
        public double[] InitialPopulations { get; set; }
        public double StepSeconds { get; set; } = 10;
        public double VictorySeconds { get; set; } = 300;
    }
    public sealed class HabitatState
    {
        public string Id { get; set; }
        public double Water { get; set; } = 0.35;
        public double Purity { get; set; } = 0.4;
        public double Shelter { get; set; } = 0.3;
        public Dictionary<string, double> Populations { get; set; } = new Dictionary<string, double>();
        public Dictionary<string, double> Delta { get; set; } = new Dictionary<string, double>();
        public List<double[]> History { get; set; } = new List<double[]>();
    }
    public sealed class EcologyState : FeatureState
    {
        public BigDouble Funds { get; set; } = 90;
        public List<HabitatState> Habitats { get; set; } = new List<HabitatState>();
        public double StableSeconds { get; set; }
        public int FieldNotes { get; set; }
    }
    public sealed class EcologyModule : FeatureModule<EcologyState>
    {
        public override string Id => "ecology";
        public EcologyDefinition Definition { get; }
        private readonly double stepSeconds;
        public EcologyModule(EcologyDefinition d) : base(Initial(d), d) { Definition = FeatureJson.Copy(d); stepSeconds=d.StepSeconds; }
        private static EcologyState Initial(EcologyDefinition d)
        {
            FeatureJson.Require(d?.Species?.Length > 0 && d.Zones?.Length > 0 && d.InitialPopulations?.Length == d.Species.Length
                && d.StepSeconds > 0 && FeatureJson.Finite(d.StepSeconds), "Invalid ecology definition.");
            FeatureJson.Require(d.Species.Select(x => x.Id).Distinct().Count() == d.Species.Length && d.Species.All(x => x.Capacity > 0 && x.Growth >= 0
                && x.HealthyMin >= 0 && x.HealthyMax >= x.HealthyMin && (x.Food == null || d.Species.Any(p => p.Id == x.Food))), "Invalid species definition.");
            var state = new EcologyState();
            foreach (var zone in d.Zones)
            {
                var h = new HabitatState { Id = zone };
                for (int i = 0; i < d.Species.Length; i++) h.Populations[d.Species[i].Id] = d.InitialPopulations[i];
                state.Habitats.Add(h);
            }
            state.Schedule(d.StepSeconds, "ecology"); return state;
        }
        protected override string Act(EcologyState s, FeatureCommand c, double time)
        {
            if (c.Action == "replay")
            {
                if (!s.Won) return "Restore the reserve first.";
                s.FieldNotes++; var initial = Initial(Definition); s.Habitats = initial.Habitats;
                foreach (var h in s.Habitats) { h.Water += s.FieldNotes % 2 * 0.1; h.Populations[Definition.Species[0].Id] *= 0.7; }
                s.StableSeconds = 0; s.Won = false; s.Funds = 90; return null;
            }
            var habitat = s.Habitats.FirstOrDefault(x => x.Id == c.Target);
            if (habitat == null) return "Unknown habitat.";
            double cost = c.Action == "restore" ? 0 : c.Action == "introduce" ? 20 : 10;
            if (s.Funds < cost) return "Insufficient restoration funds.";
            if (c.Action == "water") habitat.Water = Math.Min(1, habitat.Water + 0.15);
            else if (c.Action == "filter") habitat.Purity = Math.Min(1, habitat.Purity + 0.15);
            else if (c.Action == "shelter") habitat.Shelter = Math.Min(1, habitat.Shelter + 0.15);
            else if (c.Action == "drain") habitat.Water = Math.Max(0.1, habitat.Water - 0.15);
            else if (c.Action == "restore")
            { habitat.Water = Math.Max(0.55, habitat.Water); habitat.Purity = Math.Max(0.55, habitat.Purity); habitat.Shelter = Math.Max(0.55, habitat.Shelter); }
            else if (c.Action == "introduce")
            {
                if (!habitat.Populations.ContainsKey(c.Argument)) return "Unknown species.";
                habitat.Populations[c.Argument] += 8;
            }
            else if (c.Action == "relocate")
            {
                if (!habitat.Populations.ContainsKey(c.Argument)) return "Unknown species.";
                habitat.Populations[c.Argument] = Math.Max(0, habitat.Populations[c.Argument] - 10);
            }
            else return "Unknown restoration action.";
            s.Funds -= cost; s.Message = "Habitat intervention applied."; return null;
        }
        protected override void OnEvent(EcologyState s, FeatureEvent e, double time)
        {
            s.Funds += 5; bool allHealthy = true;
            foreach (var h in s.Habitats)
            {
                var before = new Dictionary<string, double>(h.Populations);
                var next = new Dictionary<string, double>();
                double quality = (h.Water + h.Purity + h.Shelter) / 3;
                foreach (var species in Definition.Species)
                {
                    double population = before[species.Id], capacity = species.Capacity * quality;
                    double growth = population * species.Growth * (1 - population / Math.Max(1, capacity));
                    if (species.Food != null)
                    {
                        double food = before[species.Food];
                        growth *= Math.Min(1, food / Math.Max(1, population * 2));
                        growth -= population * 0.08 * Math.Max(0, 1 - food / Math.Max(1, population));
                    }
                    double consumed = Definition.Species.Where(p => p.Food == species.Id).Sum(p => before[p.Id] * p.Consumption * population / (population + 20));
                    double value = Math.Max(0, Math.Min(species.Capacity, population + growth - consumed));
                    if (value < 0.01) value = 0;
                    next[species.Id] = value; h.Delta[species.Id] = value - population;
                    allHealthy &= value >= species.HealthyMin && value <= species.HealthyMax;
                }
                h.Populations = next; allHealthy &= quality >= 0.55 && h.Water >= 0.4 && h.Water <= 0.85;
                h.History.Add(Definition.Species.Select(x => next[x.Id]).ToArray());
                if (h.History.Count > 60) h.History.RemoveAt(0);
            }
            s.StableSeconds = allHealthy ? s.StableSeconds + stepSeconds : 0;
            if (s.StableSeconds >= Definition.VictorySeconds) s.Won = true;
            s.Message = s.Won ? "The wetland is thriving. Field notes recorded." : "Stable habitat: " + s.StableSeconds + "/" + Definition.VictorySeconds + "s.";
            s.Schedule(time + stepSeconds, "ecology");
        }
        protected override void ValidateState(EcologyState s)
        {
            base.ValidateState(s);
            FeatureJson.Require(s.Habitats != null && s.Funds >= 0 && s.Habitats.All(h => h.Populations != null && h.Populations.Values.All(v => FeatureJson.Finite(v) && v >= 0)
                && new[] { h.Water, h.Purity, h.Shelter }.All(v => FeatureJson.Finite(v) && v >= 0 && v <= 1)), "Invalid ecology state.");
        }
    }
}
