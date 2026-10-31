using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;

namespace IncrementalFramework.Features
{
    public sealed class StationDefinition
    {
        public string Id { get; set; }
        public string Product { get; set; }
        public BigDouble RatePerTick { get; set; }
    }
    public sealed class TrackDefinition
    {
        public string Id { get; set; }
        public string A { get; set; }
        public string B { get; set; }
        public double Seconds { get; set; }
        public BigDouble Cost { get; set; }
    }
    public sealed class DeliveryContract
    {
        public string Id { get; set; }
        public string Station { get; set; }
        public string Requires { get; set; }
        public Dictionary<string, BigDouble> Cargo { get; set; }
        public BigDouble Reward { get; set; }
    }
    public sealed class LogisticsDefinition
    {
        public StationDefinition[] Stations { get; set; }
        public TrackDefinition[] Tracks { get; set; }
        public DeliveryContract[] Contracts { get; set; }
        public string FinalContract { get; set; }
        public string InitialTrack { get; set; }
        [System.ComponentModel.DefaultValue(30d), Newtonsoft.Json.JsonProperty(DefaultValueHandling=Newtonsoft.Json.DefaultValueHandling.Ignore)]
        public double ProductionTickSeconds { get; set; } = 30;
        public string[] AlternateContractOrder { get; set; } = Array.Empty<string>();
    }
    public sealed class RouteStop
    {
        public string Station { get; set; }
        public Dictionary<string, BigDouble> Load { get; set; } = new Dictionary<string, BigDouble>();
        public bool Unload { get; set; } = true;
    }
    public sealed class TrainState
    {
        public string Id { get; set; }
        public string Station { get; set; }
        public string Destination { get; set; }
        public Inventory Cargo { get; set; } = new Inventory { Capacity = 40 };
        public List<RouteStop> Route { get; set; } = new List<RouteStop>();
        public List<RouteStop> PendingRoute { get; set; }
        public int Stop { get; set; }
    }
    public sealed class LogisticsState : FeatureState
    {
        public Dictionary<string, Inventory> Stations { get; set; } = new Dictionary<string, Inventory>();
        public List<string> Tracks { get; set; } = new List<string>();
        public List<TrainState> Trains { get; set; } = new List<TrainState>();
        public List<string> Completed { get; set; } = new List<string>();
        public BigDouble Cash { get; set; } = 80;
        public int Season { get; set; }
    }
    public sealed class LogisticsModule : FeatureModule<LogisticsState>
    {
        public override string Id => "logistics";
        public LogisticsDefinition Definition { get; }
        private readonly double productionTickSeconds;
        public LogisticsModule(LogisticsDefinition d) : base(Initial(d), d) { Definition = FeatureJson.Copy(d); productionTickSeconds=d.ProductionTickSeconds; }
        private static LogisticsState Initial(LogisticsDefinition d)
        {
            FeatureJson.Require(d?.Stations?.Length > 1 && d.Tracks?.Length > 0 && d.Contracts?.Length > 0, "Missing logistics content.");
            FeatureJson.Require(d.Stations.Select(x => x.Id).Distinct().Count() == d.Stations.Length && d.Tracks.All(t => t.Seconds > 0 && FeatureJson.Finite(t.Seconds)
                && d.Stations.Any(s => s.Id == t.A) && d.Stations.Any(s => s.Id == t.B)), "Invalid logistics graph.");
            FeatureJson.Require(d.AlternateContractOrder != null && (d.AlternateContractOrder.Length==0 || d.AlternateContractOrder.Length==d.Contracts.Length
                && d.AlternateContractOrder.Distinct().Count()==d.Contracts.Length && d.AlternateContractOrder.All(id=>d.Contracts.Any(c=>c.Id==id))), "Invalid alternate contract order.");
            FeatureJson.Require(d.ProductionTickSeconds>0 && FeatureJson.Finite(d.ProductionTickSeconds),"Invalid logistics production interval.");
            var s = new LogisticsState();
            foreach (var station in d.Stations) s.Stations.Add(station.Id, new Inventory { Capacity = 1000 });
            s.Tracks.Add(d.InitialTrack); s.Trains.Add(new TrainState { Id = "train:1", Station = d.Stations[0].Id });
            s.Schedule(d.ProductionTickSeconds, "produce"); return s;
        }
        public static string EncodeRoute(params RouteStop[] stops) => FeatureJson.Write(stops);
        public DeliveryContract[] Contracts(int season)
        {
            var contracts=FeatureJson.Copy(Definition.Contracts);
            if(season%2==0 || Definition.AlternateContractOrder.Length==0) return contracts;
            contracts=Definition.AlternateContractOrder.Select(id=>contracts.Single(c=>c.Id==id)).ToArray();
            for(int i=0;i<contracts.Length;i++) contracts[i].Requires=i==0?null:contracts[i-1].Id;
            return contracts;
        }
        public Dictionary<string,BigDouble> CargoCosts(DeliveryContract contract,int season) => contract.Cargo.ToDictionary(p=>p.Key,p=>p.Value*(season%2==1?1.2:1));
        protected override string Act(LogisticsState s, FeatureCommand c, double time)
        {
            if (c.Action == "track")
            {
                var track = Definition.Tracks.FirstOrDefault(t => t.Id == c.Target);
                if (track == null || s.Tracks.Contains(track.Id)) return "Track unavailable.";
                if (s.Cash < track.Cost) return "Insufficient funds.";
                s.Cash -= track.Cost; s.Tracks.Add(track.Id);
            }
            else if (c.Action == "train")
            {
                if (s.Trains.Count >= 3 || s.Cash < 60) return "Need 60 funds and an available train slot.";
                s.Cash -= 60; s.Trains.Add(new TrainState { Id = "train:" + (s.Trains.Count + 1), Station = Definition.Stations[0].Id });
            }
            else if(c.Action == "park")
            {
                var train=s.Trains.FirstOrDefault(t=>t.Id==c.Target);if(train==null)return "Unknown train.";
                if(train.Destination==null)train.Route.Clear();else train.PendingRoute=new List<RouteStop>();
            }
            else if (c.Action == "route")
            {
                var train = s.Trains.FirstOrDefault(t => t.Id == c.Target);
                if (train == null) return "Unknown train.";
                List<RouteStop> route;
                try { route = FeatureJson.Read<List<RouteStop>>(c.Argument); }
                catch (Newtonsoft.Json.JsonException) { return "Invalid route data."; }
                if (route == null || route.Count < 2 || route.Count > 20 || route.Any(r => r == null || !s.Stations.ContainsKey(r.Station) || r.Load == null || r.Load.Any(p => p.Value < 0))) return "Invalid route stops.";
                for (int i = 0; i < route.Count; i++)
                    if (Track(s, route[i].Station, route[(i + 1) % route.Count].Station) == null) return "Build every route link first.";
                if (!route.Any(r => r.Station == (train.Destination ?? train.Station))) return "Route must include the train's next station.";
                if (train.Destination != null) train.PendingRoute = route;
                else { train.Route = route; train.Stop = route.FindIndex(r => r.Station == train.Station); Depart(s, train, time); }
            }
            else if (c.Action == "contract")
            {
                var contract = Contracts(s.Season).FirstOrDefault(x => x.Id == c.Target);
                if (contract == null || s.Completed.Contains(c.Target) || contract.Requires != null && !s.Completed.Contains(contract.Requires)) return "Contract unavailable.";
                var costs = CargoCosts(contract,s.Season);
                if (!s.Stations[contract.Station].Take(costs)) return "Deliver the required cargo to " + contract.Station + ".";
                s.Completed.Add(contract.Id); s.Cash += contract.Reward; s.Won = s.Completed.Contains(Definition.FinalContract);
            }
            else if (c.Action == "replay")
            {
                if (!s.Won) return "Supply the observatory first.";
                s.Season++; s.Completed.Clear(); s.Won = false;
            }
            else return "Unknown logistics action.";
            s.Message = s.Won ? "The observatory shines again!" : "Railway updated."; return null;
        }
        private TrackDefinition Track(LogisticsState s, string a, string b) => Definition.Tracks.FirstOrDefault(t => s.Tracks.Contains(t.Id) && (t.A == a && t.B == b || t.A == b && t.B == a));
        private void Depart(LogisticsState s, TrainState train, double time)
        {
            if (train.Route.Count < 2) return;
            var stop = train.Route[train.Stop]; var station = s.Stations[train.Station];
            if (stop.Unload)
                foreach (var item in train.Cargo.Items.ToArray())
                {
                    var amount = BigDouble.Min(item.Value, station.Capacity - station.Used);
                    if (amount <= 0) continue;
                    station.Put(item.Key, amount); train.Cargo.Items[item.Key] -= amount;
                }
            foreach (var item in stop.Load.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var amount = BigDouble.Min(item.Value, BigDouble.Min(station.Get(item.Key), train.Cargo.Capacity - train.Cargo.Used));
                if (amount <= 0) continue;
                station.Items[item.Key] -= amount; train.Cargo.Put(item.Key, amount);
            }
            int next = (train.Stop + 1) % train.Route.Count;
            var track = Track(s, train.Station, train.Route[next].Station);
            if (track == null) return;
            train.Stop = next; train.Destination = train.Route[next].Station;
            s.Schedule(time + track.Seconds, "arrive", train.Id);
        }
        protected override void OnEvent(LogisticsState s, FeatureEvent e, double time)
        {
            if (e.Kind == "produce")
            {
                foreach (var station in Definition.Stations)
                {
                    var inventory = s.Stations[station.Id];
                    inventory.Put(station.Product, BigDouble.Min(station.RatePerTick, inventory.Capacity - inventory.Used));
                }
                s.Schedule(time + productionTickSeconds, "produce");
            }
            else
            {
                var train = s.Trains.Single(t => t.Id == e.Target); train.Station = train.Destination; train.Destination = null;
                if (train.PendingRoute != null)
                { train.Route = train.PendingRoute; train.PendingRoute = null; train.Stop = train.Route.FindIndex(r => r.Station == train.Station); }
                Depart(s, train, time);
            }
        }
        protected override void ValidateState(LogisticsState s)
        {
            base.ValidateState(s); FeatureJson.Require(s.Stations != null && s.Trains != null && s.Cash >= 0, "Invalid railway state.");
            foreach (var inv in s.Stations.Values) inv.Validate(); foreach (var train in s.Trains) train.Cargo.Validate();
        }
    }
}
