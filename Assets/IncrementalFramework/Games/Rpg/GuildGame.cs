using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;
using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using IncrementalFramework.Persistence;
using IncrementalFramework.Definitions;
using IncrementalFramework.Combat;

namespace IncrementalFramework.Games.Rpg
{
    /// <summary>Genre execution facade. Content and abilities arrive through an injected declaration set.</summary>
    public sealed class GuildGame
    {
        public GuildDefinition Definition{get;}
        private readonly double combatRoundSeconds;
        public Simulation Economy{get;}
        public GuildState State{get;}
        public EntityContext World=>Economy.Entities;
        public double Time=>Economy.Time;
        public int PartyCount=>State.Buildings[0];
        public int HeroCapacity=>State.HeroCapacity;
        public string LastMessage{get;private set;}="Hire a party to begin.";
        public GuildGame(GuildDefinition definition):this(definition,new Simulation(definition.Economy),true){ }
        private GuildGame(GuildDefinition definition,Simulation economy,bool initialize)
        {
            if(double.IsNaN(definition.CombatRoundSeconds)||double.IsInfinity(definition.CombatRoundSeconds)||definition.CombatRoundSeconds<=0)
                throw new ArgumentOutOfRangeException(nameof(definition.CombatRoundSeconds));
            combatRoundSeconds=definition.CombatRoundSeconds;
            Definition=definition;Economy=economy;
            foreach(var template in definition.Templates.Values)World.RegisterTemplate(template);
            if(initialize)
            {
                using(World.Batch())
                {
                    foreach(string id in new[]{"guild.hall","guild.workshop","guild.training"})definition.Templates["building"].Instantiate(World,id);
                    for(int i=0;i<definition.PartyNames.Length;i++)
                    {var party=(PartyState)definition.Templates["party"].Instantiate(World,"party:"+i);party.Name=definition.PartyNames[i];party.GetValue<int>("Index").Current=i;}
                    for(int i=0;i<definition.Regions.Length;i++)definition.Regions[i].Instantiate(World,"region:"+i);
                    State=(GuildState)definition.Templates["guild"].Instantiate(World,"guild");
                    foreach(string id in new[]{"guild.hall","guild.workshop","guild.training"})State.BuildingIds.Add(id);
                    for(int i=0;i<definition.PartyNames.Length;i++)State.PartyIds.Add("party:"+i);
                    for(int i=0;i<definition.Regions.Length;i++)State.RegionIds.Add("region:"+i);
                }
                Economy.Execute(new BuyProducerCommand(definition.ProductionIds[0]));
            }
            else State=(GuildState)World.Find("guild");
            State.GetValue<int>("HeroCapacity").Calculate(entity=>Math.Min(GuildDefinition.MaximumHeroes,4+((BuildingState)entity.Context.Find("guild.hall")).Level*4));
            foreach(var party in State.Parties)
                party.GetValue<int>("MemberCount").Calculate(entity=>((PartyState)entity).MemberIds.Current.Count,"MemberIds");
            if(initialize)Record("The contract office is open. Its gold supports an idle or recovering guild.");
        }
        public BigDouble Balance(string id)=>Economy.GetResource(id).Balance;
        public bool CanAfford(IEnumerable<ResourceAmount> costs)=>costs.GroupBy(x=>x.ResourceId).All(group=>Balance(group.Key)>=group.Aggregate(BigDouble.Zero,(sum,x)=>sum+x.Amount));
        public List<HeroState> Members(int party)=>State.Parties[party].MemberIds.Current.Select(id=>(HeroState)World.Find(id)).OrderBy(x=>x.Number).ToList();
        public bool IsBusy(HeroState hero)=>hero.Busy;
        private bool Fail(string message){LastMessage=message;return false;}
        private bool Success(string message){LastMessage=message;return true;}
        private bool Execute(string id,EntityInstance actor,EntityInstance target=null)
        {
            if(!Definition.Actions.TryGetValue(id,out var action))return Fail("That action is unavailable.");
            var context=new EvaluationContext(actor){Target=target??actor,Time=Time};
            return action.Execute(context,out var error)||Fail(error);
        }
        private void Record(string message)
        {State.Journal.Current=Array.AsReadOnly(new[]{message}.Concat(State.Log).Take(30).ToArray());}
        private HeroState Hero(int number)=>State.Heroes.FirstOrDefault(x=>x.Number==number);
        public bool Hire(string classId)
        {return Execute("hire:"+classId,State)&&Success("Adventurer hired. Assign them from the roster.");}
        public bool Assign(int heroNumber,int party)
        {
            var hero=Hero(heroNumber);
            if(hero==null||party< -1||party>=PartyCount)return Fail("Choose an adventurer and unlocked party.");
            if(hero.Party==party)return Success("Already assigned.");
            return Execute(party<0?"rest":"assign",hero,party<0?hero:(EntityInstance)State.Parties[party])&&Success(party<0?"Adventurer is resting at the guild.":"Adventurer assigned to "+State.Parties[party].Name+".");
        }
        public int TrainingCost(HeroState hero)=>(int)Definition.TrainingPrice.Evaluate(new EvaluationContext(hero));
        public bool Train(int heroNumber)
        {var hero=Hero(heroNumber);return hero==null?Fail("Select an adventurer."):Execute("train",hero)&&Success(hero.Name+" completed training rank "+hero.Training+".");}
        public bool BuyEquipment(int heroNumber,GearSlot slot)
        {var hero=Hero(heroNumber);return hero==null?Fail("Select an adventurer."):Execute("buy:"+slot,hero)&&Success("Bought and equipped "+Definition.GearName(slot,1)+" for "+hero.Name+".");}
        public bool Craft(int heroNumber,GearSlot slot)
        {
            var hero=Hero(heroNumber);if(hero==null||!Enum.IsDefined(typeof(GearSlot),slot))return Fail("Select an adventurer and item slot.");
            int next=hero.Gear(slot)+1;
            return Execute("craft:"+slot+":"+next,hero)&&Success("Reforged and equipped "+Definition.GearName(slot,next)+".");
        }
        public bool UpgradeProduction(int index)
        {
            if(index<0||index>=Definition.ProductionIds.Length)return Fail("Unknown production building.");
            var producer=Economy.GetProducer(Definition.ProductionIds[index]);
            var costs=Economy.GetProducerCost(Definition.ProductionIds[index]);
            var effects=costs.Select(cost=>Effects.Spend(new PropertyKey<BigDouble>("Balance"),Expr.Constant(cost.Amount),TargetQuery.ById(Expr.Constant(cost.ResourceId)))).ToList();
            effects.Add(Effects.Add(new PropertyKey<long>("Owned"),Expr.Constant(1L)));
            var action=new ActionDefinition("upgrade-production",Expr.Less(Expr.Property(new PropertyKey<long>("Owned")),Expr.Constant(20L)),effects.ToArray());
            return action.Execute(new EvaluationContext(producer),out var error)?Success("Production building upgraded."):Fail(error);
        }
        public bool UpgradeBuilding(int kind)
        {
            if(kind<0||kind>=State.BuildingIds.Current.Count)return Fail("Unknown guild building.");
            var building=(BuildingState)World.Find(State.BuildingIds.Current[kind]);
            return Execute("building:"+kind+":"+building.Level,building)&&Success(Definition.BuildingNames[kind]+" upgraded to level "+building.Level+".");
        }
        public double MaxHealth(HeroState hero)=>hero.MaxHealth;
        public double Attack(HeroState hero)=>hero.Attack;
        public double Defense(HeroState hero)=>hero.Defense;
        public double Healing(HeroState hero)=>hero.Healing;
        public bool Dispatch(int partyIndex,int region,bool boss)
        {
            if(partyIndex<0||partyIndex>=PartyCount||region<0||region>State.UnlockedRegion)return Fail("That party or region is locked.");
            var party=State.Parties[partyIndex];var members=Members(partyIndex);
            if(party.Active||members.Count==0||Time<party.RecoveryUntil)return Fail("The party must be home, recovered, and have at least one adventurer.");
            using(World.Batch())
            {
                ClearEnemies(party);
                if(!string.IsNullOrEmpty(party.QuestId)&&World.Entities.ContainsKey(party.QuestId))World.Remove(party.QuestId);
                string id=World.NewId("quest");Definition.Quests[Definition.QuestId(region,boss)].Instantiate(World,id);party.QuestId=id;
                party.Active=true;party.Region=region;party.Boss=boss;party.Wave=0;party.Round=0;
                foreach(var hero in members){hero.Health=hero.MaxHealth;hero.Encounter=party.Id;hero.Team=0;}
                Spawn(party);party.NextRound=Time+combatRoundSeconds;party.Report="Travelling into "+Definition.Regions[region].Name+".";
            }
            return Success(party.Name+" dispatched. Combat resolves automatically every two seconds.");
        }
        public bool SetRepeat(int partyIndex,bool repeat)
        {if(partyIndex<0||partyIndex>=PartyCount)return Fail("Choose an unlocked party.");State.Parties[partyIndex].AutoRepeat=repeat;return Success(repeat?"Repeat enabled until recalled or defeated.":"Repeat disabled; return after this quest.");}
        public bool Recall(int partyIndex)
        {
            if(partyIndex<0||partyIndex>=PartyCount)return Fail("Choose an unlocked party.");var party=State.Parties[partyIndex];
            using(World.Batch()){party.Active=false;party.AutoRepeat=false;party.RecoveryUntil=Time+5;party.Report="Recalled. Earned loot kept. Recovering for five seconds.";}
            return Success(party.Name+" returned to the guild.");
        }
        private EntityInstance Quest(PartyState party)=>World.Find(party.QuestId);
        private void ClearEnemies(PartyState party)
        {using(World.Batch()){foreach(var id in party.EnemyIds.Current.ToArray())if(World.Entities.ContainsKey(id))World.Remove(id);party.EnemyIds.Current=Array.AsReadOnly(Array.Empty<string>());}}
        private void Spawn(PartyState party)
        {
            var wave=Quest(party).GetValue<WaveDefinition[]>("Waves").Current[party.Wave];
            ClearEnemies(party);var transaction=new EntityTransaction();var ids=new List<string>();
            for(int i=0;i<wave.Count;i++)
            {
                string id=transaction.ReserveId(World,"enemy");
                var enemy=(EnemyState)Definition.Templates[wave.EnemyTemplateId].Instantiate(new EntityContext(),id);
                enemy.MaxHealth*=wave.HealthFactor;enemy.Health=enemy.MaxHealth;enemy.Attack*=wave.AttackFactor;enemy.Encounter=party.Id;
                transaction.Create(World,enemy);ids.Add(id);
            }
            transaction.Set(party.EnemyIds,Array.AsReadOnly(ids.ToArray()));
            if(!transaction.Commit())throw new InvalidOperationException("Quest spawning failed.");
        }
        public bool AdvanceTo(double target,int roundBudget=256)=>AdvanceTimed(target,roundBudget).Status==IncrementalFramework.Progression.Timing.TimelineAdvanceStatus.ReachedTarget;
        public IncrementalFramework.Progression.Timing.TimelineAdvanceResult AdvanceTimed(double target,int workBudget=256)
        {
            if(double.IsNaN(target)||double.IsInfinity(target)||target<Time||workBudget<1)throw new ArgumentOutOfRangeException(nameof(target));
            int work=0;
            IncrementalFramework.Progression.Timing.TimelineAdvanceResult Result(bool complete)=>new IncrementalFramework.Progression.Timing.TimelineAdvanceResult(Time,
                complete?IncrementalFramework.Progression.Timing.TimelineAdvanceStatus.ReachedTarget:IncrementalFramework.Progression.Timing.TimelineAdvanceStatus.BudgetExhausted,work);
            while(true)
            {
                if(work>=workBudget)return Result(false);
                double next=State.Parties.Take(PartyCount).Where(x=>x.Active).Select(x=>x.NextRound).DefaultIfEmpty(double.PositiveInfinity).Min();
                var boundary=Economy.AdvanceToBoundary(Math.Min(next,target),workBudget-work);work+=boundary.ProcessedWork;
                if(!boundary.Completed)return Result(false);
                if(next>target)return Result(true);
                for(int i=0;i<PartyCount;i++)if(State.Parties[i].Active&&State.Parties[i].NextRound<=next)
                {
                    if(work>=workBudget)return Result(false);
                    ResolveRound(i);work++;
                }
            }
        }
        private void ResolveRound(int partyIndex)
        {
            var party=State.Parties[partyIndex];var members=Members(partyIndex);party.Round++;
            foreach(var hero in members)World.RemoveModifiers(hero.Id);
            foreach(var source in members.Where(x=>x.Health>0&&!string.IsNullOrEmpty(x.AuraId)))Definition.Auras[source.AuraId].Apply(source,CombatTargets.Living(true));
            var log=new List<string>();
            foreach(var hero in members)
            {
                if(hero.Health<=0||!party.Enemies.Any(hp=>hp>0))continue;
                if(!Definition.Abilities[hero.AbilityId].Execute(hero,party.Round,log,out var error))throw new InvalidOperationException(error);
                RewardDefeated(party,members);
            }
            if(party.Enemies.All(hp=>hp<=0))
            {
                party.Wave++;
                if(party.Wave>=Quest(party).GetValue<WaveDefinition[]>("Waves").Current.Length){Complete(partyIndex);return;}
                Spawn(party);party.Report="Wave cleared! Entering wave "+(party.Wave+1)+".";
            }
            else
            {
                foreach(var id in party.EnemyIds.Current)
                {
                    var enemy=(EnemyState)World.Find(id);if(enemy.Health<=0)continue;
                    Definition.Abilities[enemy.GetValue<string>("AbilityId").Current].Execute(enemy,party.Round,log,out _);
                }
                party.Report=string.Join(" · ",log);
                if(members.All(hero=>hero.Health<=0))
                {
                    using(World.Batch()){party.Active=false;party.AutoRepeat=false;party.Defeats++;party.RecoveryUntil=Time+20;}
                    party.Report="Defeat. Everyone returned safely; recover for 20s. Improve equipment, training, or party roles. Earned loot kept.";
                    Record(party.Name+" was defeated.");return;
                }
            }
            party.NextRound=Time+combatRoundSeconds;
        }
        private void RewardDefeated(PartyState party,List<HeroState> members)
        {
            foreach(var id in party.EnemyIds.Current)
            {
                var enemy=(EnemyState)World.Find(id);if(enemy.Health>0||enemy.Rewarded)continue;
                var effects=new List<EffectDefinition>
                {
                    Effects.Add(new PropertyKey<BigDouble>("Balance"),Expr.Constant(new BigDouble(enemy.GetValue<int>("Gold").Current)),TargetQuery.ById(Expr.Constant(Definition.CurrencyResourceId))),
                    Effects.Add(new PropertyKey<BigDouble>("Balance"),Expr.Constant(new BigDouble(enemy.GetValue<int>("Drops").Current)),TargetQuery.ById(Expr.Constant(enemy.GetValue<string>("Material").Current))),
                    Effects.Set(new PropertyKey<bool>("Rewarded"),Expr.Constant(true))
                };
                foreach(var hero in members)
                    effects.Add(Effects.Set(RpgProperties.Experience,Expr.Min(Expr.Constant(2900),Expr.Add(Expr.Property(RpgProperties.Experience,EntityScope.Target),Expr.Constant(enemy.GetValue<int>("Experience").Current))),TargetQuery.ById(Expr.Constant(hero.Id))));
                if(!new ActionDefinition("enemy-rewards",null,effects.ToArray()).Execute(new EvaluationContext(enemy),out var error))throw new InvalidOperationException(error);
            }
        }
        private void Complete(int partyIndex)
        {
            var party=State.Parties[partyIndex];var quest=Quest(party);
            var transaction=new EntityTransaction();
            var gold=Economy.GetResource(Definition.CurrencyResourceId).GetValue<BigDouble>("Balance");transaction.Set(gold,transaction.Read(gold)+quest.GetValue<int>("CompletionGold").Current);
            transaction.Set(party.GetValue<int>("Victories"),party.Victories+1);transaction.Set(party.GetValue<bool>("Active"),false);
            int unlock=quest.GetValue<int>("Unlock").Current;
            if(unlock>=0)
            {
                var region=(RegionState)World.Find(State.RegionIds.Current[party.Region]);transaction.Set(region.GetValue<bool>("BossDefeated"),true);
                transaction.Set(State.GetValue<int>("UnlockedRegion"),Math.Max(State.UnlockedRegion,unlock));
            }
            if(!transaction.Commit())throw new InvalidOperationException("Quest completion failed.");
            party.Report=party.Name+" completed "+quest.GetValue<string>("Name").Current+". Gold, experience, and enemy materials collected.";
            if(State.BossesDefeated.All(x=>x))party.Report+=" Guild campaign complete!";
            Record(party.Report);if(party.AutoRepeat)Dispatch(partyIndex,party.Region,party.Boss);
        }
        public string Save(DateTime utcNow)=>SnapshotService.Serialize(SnapshotService.Capture(Economy,utcNow));
        public static GuildGame Load(GuildDefinition definition,string json,DateTime utcNow,out double offlineSeconds)
        {
            if(utcNow.Kind!=DateTimeKind.Utc)throw new ArgumentException("Load time must be UTC.");
            var snapshot=SnapshotService.Deserialize(json);
            var modules=new ModuleBuilder();foreach(var template in definition.Templates.Values)modules.Entity(template);
            var economy=SnapshotService.Restore(definition.Economy,snapshot,modules:modules);
            var game=new GuildGame(definition,economy,false);
            game.Validate();offlineSeconds=Math.Min(GuildDefinition.OfflineCap,Math.Max(0,(utcNow-snapshot.SavedAtUtc).TotalSeconds));return game;
        }
        /// <summary>Opt-in timing restore, retaining all party combat and behavior state.</summary>
        public static GuildGame LoadTimed(GuildDefinition definition,string json,DateTime utcNow,
            IncrementalFramework.Progression.Timing.TimingOptions options,
            out IncrementalFramework.Progression.Timing.TimingController timing,
            out IncrementalFramework.Progression.Timing.OfflineClaimSummary offline,bool adoptLegacy=false)
        {
            GuildGame game=null;var modules=new ModuleBuilder();foreach(var template in definition.Templates.Values)modules.Entity(template);
            var resumed=TimingSnapshotService.Resume(definition.Economy,SnapshotService.Deserialize(json),new TimingUtcClock(utcNow),options,modules,
                sim=>{game=new GuildGame(definition,sim,false);game.Validate();return new GuildTimeline(game);},adoptLegacy);
            timing=resumed.Timing;offline=resumed.Offline;return game;
        }
        private void Validate()
        {
            if(State.Heroes.Count>GuildDefinition.MaximumHeroes||State.Heroes.Select(x=>x.Number).Distinct().Count()!=State.Heroes.Count||State.UnlockedRegion<0||State.UnlockedRegion>=Definition.Regions.Length||State.Buildings[0]<1||State.Buildings[0]>State.Parties.Length)throw new ArgumentException("Invalid guild snapshot.");
            foreach(var party in State.Parties)
                if(party.MemberIds.Current.Count>GuildDefinition.PartySize||party.NextRound<0||double.IsNaN(party.NextRound)||double.IsInfinity(party.NextRound)||(party.Active&&(party.NextRound<Time||party.Wave<0||party.Wave>=Quest(party).GetValue<WaveDefinition[]>("Waves").Current.Length)))throw new ArgumentException("Invalid quest snapshot.");
            foreach(var hero in State.Heroes)
                if(hero.Experience<0||hero.Experience>2900||hero.Training<0||hero.Training>20)throw new ArgumentException("Invalid adventurer snapshot.");
        }
    }
}
