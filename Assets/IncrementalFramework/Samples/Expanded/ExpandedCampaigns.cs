using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;
using IncrementalFramework.Features;
using IncrementalFramework.Features.Narrative;

namespace IncrementalFramework.Samples.Expanded
{
    public static partial class ExpandedCampaigns
    {
        public static readonly string[] Ids = { "OrbitalFoundry", "LastlightRailway", "MarshlightReserve", "ThirteenthBell", "TideboundExchange", "Floodwall" };
        public static readonly string[] Titles = { "Orbital Foundry", "Lastlight Railway", "Marshlight Reserve", "The Thirteenth Bell", "Tidebound Exchange", "Letters from the Floodwall" };
        public static IFeatureModule Create(string id)
        {
            switch (id)
            {
                case "OrbitalFoundry": return new ProductionModule(Foundry());
                case "LastlightRailway": return new LogisticsModule(Railway());
                case "MarshlightReserve": return new EcologyModule(Reserve());
                case "ThirteenthBell": return new TimeLoopModule(Bell(), Investigations());
                case "TideboundExchange": return new MarketModule(Exchange());
                case "Floodwall": return new NarrativeModule(Floodwall());
                default: throw new ArgumentException("Unknown sample: " + id);
            }
        }
        public static Dictionary<string, BigDouble> Goods(params object[] pairs)
        {
            var result = new Dictionary<string, BigDouble>();
            for (int i = 0; i < pairs.Length; i += 2) result.Add((string)pairs[i], new BigDouble(Convert.ToDouble(pairs[i + 1])));
            return result;
        }
        public static ProductionDefinition Foundry()
        {
            RecipeDefinition R(string id, string machine, double seconds, Dictionary<string, BigDouble> input, Dictionary<string, BigDouble> output, string gate = null)
                => new RecipeDefinition { Id = id, Machine = machine, Seconds = seconds, Inputs = input, Outputs = output, RequiredSection = gate };
            return new ProductionDefinition
            {
                Machines = new[] { "extractor", "smelter", "drawbench", "electronics", "assembler" },
                Recipes = new[] {
                    R("mine", "extractor", 10, Goods(), Goods("ore", 6)),
                    R("smelt", "smelter", 15, Goods("ore", 4), Goods("plate", 2)),
                    R("draw-wire", "drawbench", 20, Goods("plate", 2), Goods("wire", 4)),
                    R("circuit", "electronics", 30, Goods("plate", 1, "wire", 4), Goods("circuit", 2)),
                    R("cell", "electronics", 25, Goods("ore", 4, "wire", 2), Goods("cell", 2), "hull"),
                    R("frame", "assembler", 40, Goods("plate", 6, "wire", 2), Goods("frame", 1)),
                    R("fast-smelt", "smelter", 10, Goods("ore", 8), Goods("plate", 5), "hull"),
                    R("precision-wire", "drawbench", 35, Goods("plate", 1), Goods("wire", 5), "avionics") },
                Sections = new[] {
                    new ShipSectionDefinition { Id = "hull", Costs = Goods("plate", 60, "frame", 6) },
                    new ShipSectionDefinition { Id = "avionics", Costs = Goods("circuit", 30, "wire", 30) },
                    new ShipSectionDefinition { Id = "drive", Costs = Goods("cell", 40, "circuit", 30, "frame", 12) } }
            };
        }
        public static LogisticsDefinition Railway()
        {
            TrackDefinition T(string id, string a, string b, double seconds, int cost) => new TrackDefinition { Id = id, A = a, B = b, Seconds = seconds, Cost = cost };
            DeliveryContract C(string id, string station, string requires, int reward, params object[] goods) => new DeliveryContract
            { Id = id, Station = station, Requires = requires, Reward = reward, Cargo = Goods(goods) };
            return new LogisticsDefinition
            {
                Stations = new[] {
                    new StationDefinition { Id = "valley", Product = "food", RatePerTick = 8 },
                    new StationDefinition { Id = "crossing", Product = "timber", RatePerTick = 6 },
                    new StationDefinition { Id = "quarry", Product = "tools", RatePerTick = 4 },
                    new StationDefinition { Id = "ridge", Product = "timber", RatePerTick = 3 },
                    new StationDefinition { Id = "observatory", Product = "tools", RatePerTick = 0 } },
                Tracks = new[] { T("valley-crossing", "valley", "crossing", 20, 0), T("crossing-quarry", "crossing", "quarry", 40, 35),
                    T("crossing-ridge", "crossing", "ridge", 45, 45), T("quarry-ridge", "quarry", "ridge", 25, 35),
                    T("ridge-observatory", "ridge", "observatory", 50, 60), T("valley-quarry", "valley", "quarry", 55, 50) },
                InitialTrack = "valley-crossing", FinalContract = "observatory",
                AlternateContractOrder = new[] { "crossing-food", "quarry-rations", "valley-repairs", "ridge-supplies", "ridge-repairs", "observatory" },
                Contracts = new[] { C("crossing-food", "crossing", null, 80, "food", 40), C("valley-repairs", "valley", "crossing-food", 100, "timber", 40),
                    C("quarry-rations", "quarry", "valley-repairs", 100, "food", 60), C("ridge-repairs", "ridge", "quarry-rations", 120, "tools", 40),
                    C("ridge-supplies", "ridge", "ridge-repairs", 100, "food", 60, "timber", 40), C("observatory", "observatory", "ridge-supplies", 200, "food", 100, "timber", 100, "tools", 80) }
            };
        }
        public static EcologyDefinition Reserve() => new EcologyDefinition
        {
            Zones = new[] { "reedbank", "oxbow", "saltmarsh" }, InitialPopulations = new[] { 35d, 12d, 0d, 0d },
            Species = new[] {
                new SpeciesDefinition { Id = "reeds", Growth = 0.18, Capacity = 200, HealthyMin = 50, HealthyMax = 180 },
                new SpeciesDefinition { Id = "voles", Growth = 0.12, Capacity = 60, Food = "reeds", Consumption = 0.15, HealthyMin = 10, HealthyMax = 50 },
                new SpeciesDefinition { Id = "herons", Growth = 0.08, Capacity = 20, Food = "voles", Consumption = 0.08, HealthyMin = 3, HealthyMax = 18 },
                new SpeciesDefinition { Id = "otters", Growth = 0.06, Capacity = 12, Food = "voles", Consumption = 0.06, HealthyMin = 2, HealthyMax = 10 } }
        };
        public static MarketDefinition Exchange()
        {
            CivicContract C(string id, string requires, int reward, params object[] goods) => new CivicContract { Id = id, Requires = requires, Reward = reward, Goods = Goods(goods),
                AvailableAfterSeconds = id=="relief"?180:id=="pier"?480:id=="sails"?900:id=="beacon"?1320:id=="winter"?1800:2400 };
            return new MarketDefinition
            {
                Markets = new[] { "harbor", "islands", "capital" }, FinalContract = "lighthouse",
                Commodities = new[] { new CommodityDefinition { Id = "grain", BasePrice = 3 }, new CommodityDefinition { Id = "timber", BasePrice = 5 },
                    new CommodityDefinition { Id = "cloth", BasePrice = 7 }, new CommodityDefinition { Id = "oil", BasePrice = 9, Restock = 3 } },
                Contracts = new[] { C("relief", null, 180, "grain", 20), C("pier", "relief", 240, "timber", 20),
                    C("sails", "pier", 300, "cloth", 20), C("beacon", "sails", 350, "oil", 20),
                    C("winter", "beacon", 400, "grain", 30, "cloth", 20), C("lighthouse", "winter", 800, "timber", 30, "oil", 30) }
            };
        }
        private static StoryVariable V(string id, StoryVariableKind kind, int initial = 0, bool persistent = false) => new StoryVariable
        { Id = id, Kind = kind, Initial = new StoryValue { Integer = initial }, SurvivesLoop = persistent };
        private static StoryCondition At(string id, int value) => new StoryCondition { Kind = ConditionKind.AtLeast, Variable = id, Integer = value };
        private static StoryCondition Has(string id, string member) => new StoryCondition { Kind = ConditionKind.Contains, Variable = id, Member = member };
        private static StoryEffect Add(string id, int value, string disclosure = null) => new StoryEffect { Kind = StoryEffectKind.AddInteger, Variable = id, Integer = value, DisclosureKey = disclosure };
        private static StoryEffect Include(string id, string member, string disclosure = null) => new StoryEffect { Kind = StoryEffectKind.Include, Variable = id, Member = member, DisclosureKey = disclosure };
        private static StoryNode Node(StoryDefinition d, string id, StoryNodeKind kind, int chapter, string speaker, string subject, string body, string next = null)
        {
            d.Text[id + ".subject"] = subject; d.Text[id + ".body"] = body;
            var n = new StoryNode { Id = id, Kind = kind, Chapter = chapter, Speaker = speaker, SubjectKey = id + ".subject", TextKey = id + ".body", Next = next };
            d.Nodes.Add(n); return n;
        }
        private static StoryChoice Choice(StoryDefinition d, StoryNode n, string id, string text, string target, params StoryEffect[] effects)
        {
            d.Text[id + ".text"] = text;
            var c = new StoryChoice { Id = id, TextKey = id + ".text", Target = target, Effects = effects.ToList() }; n.Choices.Add(c); return c;
        }
        private static void Character(StoryDefinition d, string id, string name, string profile)
        { d.Text[id + ".profile"] = profile; d.Characters.Add(new StoryCharacter { Id = id, Name = name, ProfileKey = id + ".profile" }); }
        private static void Evidence(StoryDefinition d, string id, string title, string text)
        {
            d.Text[id + ".title"] = title; d.Text[id + ".entry"] = text;
            d.Journal.Add(new StoryJournalEntry { Id = id, TitleKey = id + ".title", TextKey = id + ".entry" });
        }
    }
}
