using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;
using IncrementalFramework.Core;

namespace IncrementalFramework.Features
{
    public sealed class CommodityDefinition
    {
        public string Id { get; set; }
        public double BasePrice { get; set; }
        public int Restock { get; set; } = 5;
    }
    public sealed class CivicContract
    {
        public string Id { get; set; }
        public string Requires { get; set; }
        public Dictionary<string, BigDouble> Goods { get; set; }
        public BigDouble Reward { get; set; }
        public double AvailableAfterSeconds { get; set; }
    }
    public sealed class MarketDefinition
    {
        public string[] Markets { get; set; }
        public CommodityDefinition[] Commodities { get; set; }
        public CivicContract[] Contracts { get; set; }
        public string FinalContract { get; set; }
        public double Interval { get; set; } = 30;
    }
    public sealed class MarketQuote
    {
        public string Market { get; set; }
        public string Commodity { get; set; }
        public int Stock { get; set; } = 80;
        public double Demand { get; set; } = 1;
        public double Impact { get; set; }
        public List<double> History { get; set; } = new List<double>();
    }
    public sealed class StandingOrder
    {
        public string Id { get; set; }
        public string Market { get; set; }
        public string Commodity { get; set; }
        public bool Buy { get; set; }
        public double Limit { get; set; }
        public int Remaining { get; set; }
    }
    public sealed class MarketState : FeatureState
    {
        public Inventory Warehouse { get; set; } = new Inventory { Capacity = 60 };
        public BigDouble Cash { get; set; } = 400;
        public List<MarketQuote> Quotes { get; set; } = new List<MarketQuote>();
        public List<StandingOrder> Orders { get; set; } = new List<StandingOrder>();
        public List<string> Completed { get; set; } = new List<string>();
        public List<string> Trades { get; set; } = new List<string>();
        public List<string> Access { get; set; } = new List<string>();
        public Dictionary<string, int> Allocation { get; set; } = new Dictionary<string, int>();
        public int Upgrades { get; set; }
        public int NextOrder { get; set; } = 1;
        public int Season { get; set; }
        public double SeasonStartedAt { get; set; }
        public ulong RandomState { get; set; } = 7341;
    }
    public sealed class MarketModule : FeatureModule<MarketState>
    {
        public override string Id => "markets";
        public MarketDefinition Definition { get; }
        private readonly double interval;
        public MarketModule(MarketDefinition d) : base(Initial(d), d) { Definition = FeatureJson.Copy(d); interval=d.Interval; }
        private static MarketState Initial(MarketDefinition d)
        {
            FeatureJson.Require(d?.Commodities?.Length > 0 && d.Markets?.Length > 0 && d.Contracts?.Length > 0 && d.Interval > 0 && FeatureJson.Finite(d.Interval), "Invalid market content.");
            FeatureJson.Require(d.Commodities.Select(x => x.Id).Distinct().Count() == d.Commodities.Length && d.Commodities.All(x => x.BasePrice > 0 && FeatureJson.Finite(x.BasePrice)), "Invalid commodities.");
            var s = new MarketState(); s.Access.Add(d.Markets[0]);
            foreach (var market in d.Markets)
                foreach (var commodity in d.Commodities) s.Quotes.Add(new MarketQuote { Market = market, Commodity = commodity.Id });
            s.Schedule(d.Interval, "market"); return s;
        }
        public double Price(MarketQuote q) => Math.Max(0.25, Math.Min(4, q.Demand + q.Impact)) * Definition.Commodities.Single(x => x.Id == q.Commodity).BasePrice;
        public BigDouble Quote(MarketQuote quote, int quantity, bool buy)
        {
            if (quantity <= 0 || quantity > 1000) throw new ArgumentOutOfRangeException(nameof(quantity));
            var copy = FeatureJson.Copy(quote); BigDouble total = 0;
            for (int i = 0; i < quantity; i++) { total += Price(copy) * (buy ? 1.03 : 0.97); copy.Impact = Math.Max(-0.75,Math.Min(3,copy.Impact+(buy?0.015:-0.015))); }
            return total;
        }
        private string Trade(MarketState s, MarketQuote q, int quantity, bool buy)
        {
            if (quantity <= 0 || quantity > 1000) return "Quantity must be 1–1000.";
            var total = Quote(q, quantity, buy);
            if (buy)
            {
                int allocated = s.Allocation.TryGetValue(q.Commodity, out var cap) ? cap : 1000000;
                if (q.Stock < quantity || s.Cash < total || s.Warehouse.Used + quantity > s.Warehouse.Capacity || s.Warehouse.Get(q.Commodity) + quantity > allocated) return "Insufficient cash, stock, or allocated storage.";
                s.Cash -= total; s.Warehouse.Put(q.Commodity, quantity); q.Stock -= quantity; q.Impact = Math.Min(3, q.Impact + quantity * 0.015);
            }
            else
            {
                if (s.Warehouse.Get(q.Commodity) < quantity || q.Stock + quantity > 200) return "Insufficient holdings or market capacity.";
                s.Warehouse.Items[q.Commodity] -= quantity; s.Cash += total; q.Stock += quantity; q.Impact = Math.Max(-0.75, q.Impact - quantity * 0.015);
            }
            s.Trades.Add((buy ? "Bought " : "Sold ") + quantity + " " + q.Commodity + " at " + q.Market + " for " + total);
            if (s.Trades.Count > 40) s.Trades.RemoveAt(0);
            return null;
        }
        protected override string Act(MarketState s, FeatureCommand c, double time)
        {
            if (c.Action == "contract")
            {
                var contract = Definition.Contracts.FirstOrDefault(x => x.Id == c.Target);
                if(contract!=null && time<s.SeasonStartedAt+contract.AvailableAfterSeconds)return "Delivery window opens in "+Math.Ceiling(s.SeasonStartedAt+contract.AvailableAfterSeconds-time)+"s.";
                if (contract == null || s.Completed.Contains(c.Target) || contract.Requires != null && !s.Completed.Contains(contract.Requires)) return "Contract unavailable.";
                if (!s.Warehouse.Take(contract.Goods)) return "Missing contract goods.";
                s.Cash += contract.Reward; s.Completed.Add(contract.Id);
                int access = Math.Min(Definition.Markets.Length,1+s.Completed.Count/2);
                while(s.Access.Count<access) s.Access.Add(Definition.Markets[s.Access.Count]);
                s.Won = s.Completed.Contains(Definition.FinalContract);
            }
            else if (c.Action == "upgrade")
            {
                BigDouble cost = 120 + s.Upgrades * 80;
                if (s.Upgrades >= 2 || s.Cash < cost) return "Warehouse upgrade unavailable.";
                s.Cash -= cost; s.Upgrades++; s.Warehouse.Capacity += 40;
            }
            else if (c.Action == "allocate")
            {
                if (!Definition.Commodities.Any(x => x.Id == c.Target) || c.Amount < 0 || c.Amount != Math.Floor(c.Amount) || c.Amount > 1000000 || s.Warehouse.Get(c.Target) > c.Amount) return "Invalid storage allocation.";
                s.Allocation[c.Target] = (int)c.Amount;
            }
            else if (c.Action == "cancel")
            {
                if (s.Orders.RemoveAll(x => x.Id == c.Target) == 0) return "Unknown order.";
            }
            else if (c.Action == "replay")
            {
                if (!s.Won) return "Complete the lighthouse first.";
                s.Season++; s.SeasonStartedAt=time; s.RandomState = (ulong)(7341 + s.Season * 7919); s.Completed.Clear(); s.Won = false;
                s.Warehouse.Items.Clear(); s.Orders.Clear(); s.Cash = 400;
            }
            else
            {
                var q = s.Quotes.FirstOrDefault(x => x.Market == c.Target && x.Commodity == c.Argument);
                if (q == null || !s.Access.Contains(c.Target)) return "Market unavailable.";
                if (c.Action == "buy" || c.Action == "sell")
                {
                    if (c.Amount != Math.Floor(c.Amount) || c.Amount > 1000) return "Use whole cargo units up to 1000.";
                    var error = Trade(s, q, (int)c.Amount, c.Action == "buy"); if (error != null) return error;
                }
                else if (c.Action == "order-buy" || c.Action == "order-sell")
                {
                    if (c.Amount <= 0 || c.Quantity<=0 || c.Quantity>1000 || s.Orders.Count >= 100) return "Invalid limit or too many orders.";
                    s.Orders.Add(new StandingOrder { Id = "order:" + s.NextOrder++, Market = c.Target, Commodity = c.Argument,
                        Buy = c.Action == "order-buy", Limit = c.Amount, Remaining = c.Quantity });
                }
                else return "Unknown market action.";
            }
            s.Message = s.Won ? "The lighthouse is supplied. A new trading season awaits." : "Exchange updated."; return null;
        }
        protected override void OnEvent(MarketState s, FeatureEvent e, double time)
        {
            var rng = new XorShift64(s.RandomState);
            foreach (var q in s.Quotes)
            {
                q.Demand = Math.Max(0.45, Math.Min(2.2, q.Demand + ((int)(rng.NextUInt64() % 7) - 3) * 0.08));
                q.Impact *= 0.8; q.Stock = Math.Min(200, q.Stock + Definition.Commodities.Single(x => x.Id == q.Commodity).Restock);
                q.History.Add(Price(q)); if (q.History.Count > 60) q.History.RemoveAt(0);
            }
            s.RandomState = rng.State;
            foreach (var order in s.Orders)
            {
                var q = s.Quotes.Single(x => x.Market == order.Market && x.Commodity == order.Commodity);
                while (order.Remaining > 0)
                {
                    double price = Quote(q, 1, order.Buy).ToDouble();
                    if (order.Buy ? price > order.Limit : price < order.Limit) break;
                    if (Trade(s, q, 1, order.Buy) != null) break;
                    order.Remaining--;
                }
            }
            s.Orders.RemoveAll(x => x.Remaining == 0); s.Schedule(time + interval, "market");
        }
        protected override void ValidateState(MarketState s)
        {
            base.ValidateState(s); s.Warehouse.Validate();
            FeatureJson.Require(s.Cash >= 0 && s.Quotes != null && s.Orders != null && s.Quotes.All(q => q.Stock >= 0 && q.Stock <= 200
                && FeatureJson.Finite(q.Demand) && FeatureJson.Finite(q.Impact)) && s.RandomState != 0, "Invalid market state.");
        }
    }
}
