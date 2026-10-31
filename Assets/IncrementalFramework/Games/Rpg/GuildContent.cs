using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;
using IncrementalFramework.Combat;
using IncrementalFramework.Economy;

namespace IncrementalFramework.Games.Rpg
{
    public sealed class ClassDefinition:EntityTemplate
    {
        public string Name{get=>Get<string>(nameof(Name));set=>Set(nameof(Name),value);}
        public string Role{get=>Get<string>(nameof(Role));set=>Set(nameof(Role),value);}
        public string Skill{get=>Get<string>(nameof(Skill));set=>Set(nameof(Skill),value);}
        public int HireGold{get=>Get<int>(nameof(HireGold));set=>Set(nameof(HireGold),value);}
        public double Health{get=>Get<double>(nameof(Health));set=>Set(nameof(Health),value);}
        public double Attack{get=>Get<double>(nameof(Attack));set=>Set(nameof(Attack),value);}
        public double Defense{get=>Get<double>(nameof(Defense));set=>Set(nameof(Defense),value);}
        public string AbilityId{get=>Get<string>(nameof(AbilityId));set=>Set(nameof(AbilityId),value);}
        public string AuraId{get=>Get<string>(nameof(AuraId));set=>Set(nameof(AuraId),value);}
        public double Priority{get=>Get<double>(nameof(Priority));set=>Set(nameof(Priority),value);}
        public double IncomingMultiplier{get=>Get<double>(nameof(IncomingMultiplier));set=>Set(nameof(IncomingMultiplier),value);}
        public ClassDefinition(string id,string name,string role,string skill,int gold,double health,double attack,double defense,string abilityId,string auraId="",double priority=0,double incoming=1):base(id)
        {
            Name=name;Role=role;Skill=skill;HireGold=gold;Health=health;Attack=attack;Defense=defense;AbilityId=abilityId;AuraId=auraId;Priority=priority;IncomingMultiplier=incoming;
            var schema=new HeroState();foreach(var value in schema.Values.Values)if(!Values.ContainsKey(value.Name))Register(value.Name,value.CopyFor(this));
        }
        public override EntityInstance Instantiate(EntityContext context,string instanceId)
        {
            var hero=new HeroState(instanceId,Id,context){Class=Id,Name=Name,AbilityId=AbilityId,AuraId=AuraId,Priority=Priority,IncomingMultiplier=IncomingMultiplier};
            CopyValuesTo(hero);hero.Class=Id;
            hero.Define("BaseHealth",Health);hero.Define("BaseAttack",Attack);hero.Define("BaseDefense",Defense);
            Bind(hero,RpgProperties.Level,Expr.Min(Expr.Constant(30),Expr.Add(Expr.Constant(1),Expr.Divide(Expr.Property(RpgProperties.Experience),Expr.Constant(100)))));
            Bind(hero,RpgProperties.Scale,Expr.Add(Expr.Constant(1d),Expr.Multiply(Expr.ToDouble(Expr.Subtract(Expr.Property(RpgProperties.Level),Expr.Constant(1))),Expr.Constant(.08))));
            Bind(hero,CombatProperties.MaxHealth,Expr.Multiply(Expr.Property(new PropertyKey<double>("BaseHealth")),Expr.Property(RpgProperties.Scale)));
            Bind(hero,CombatProperties.Attack,Expr.Multiply(Expr.Property(new PropertyKey<double>("BaseAttack")),Expr.Property(RpgProperties.Scale)));
            Bind(hero,CombatProperties.Defense,Expr.Multiply(Expr.Property(new PropertyKey<double>("BaseDefense")),Expr.Property(RpgProperties.Scale)));
            Bind(hero,CombatProperties.Healing,Expr.Multiply(Expr.Constant(26d),Expr.Property(RpgProperties.Scale)));
            foreach(var slot in Enum.GetValues(typeof(GearSlot)).Cast<GearSlot>())
            {
                string key=slot.ToString();
                hero.GetValue<int>(key).Calculate(entity=>
                {string item=entity.GetValue<string>(key+"Id").Current;return string.IsNullOrEmpty(item)?0:((EquipmentState)entity.Context.Find(item)).Tier;},key+"Id");
            }
            hero.GetValue<int>("Party").Calculate(entity=>
            {
                foreach(var party in entity.Context.Entities.Values.OfType<PartyState>().OrderBy(x=>x.Id,StringComparer.Ordinal))
                    if(party.MemberIds.Current.Contains(entity.Id))return party.GetValue<int>("Index").Current;
                return -1;
            });
            hero.GetValue<bool>("Busy").Calculate(entity=>
            {int index=((HeroState)entity).Party;return index>=0&&entity.Context.Entities.Values.OfType<PartyState>().Any(x=>x.GetValue<int>("Index").Current==index&&x.Active);},"Party");
            hero.GetValue<int>("TrainingCap").Calculate(entity=>entity.Context.Entities.TryGetValue("guild.training",out var building)?((BuildingState)building).Level*5:5);
            hero.GetValue<int>("WorkshopLevel").Calculate(entity=>entity.Context.Entities.TryGetValue("guild.workshop",out var building)?((BuildingState)building).Level:1);
            hero.Health=hero.MaxHealth;context.Add(hero);return hero;
        }
        private static void Bind<T>(EntityInstance entity,PropertyKey<T> key,Expression<T> expression)
        {entity.GetValue(key).Calculate(owner=>expression.Evaluate(new EvaluationContext((EntityInstance)owner)),expression.References.Where(x=>x.Scope==EntityScope.Self).Select(x=>x.Name).Distinct().ToArray());}
    }
    public sealed class RegionDefinition:EntityTemplate
    {
        public string Name{get=>Get<string>(nameof(Name));set=>Set(nameof(Name),value);}
        public string Enemy{get=>Get<string>(nameof(Enemy));set=>Set(nameof(Enemy),value);}
        public string Boss{get=>Get<string>(nameof(Boss));set=>Set(nameof(Boss),value);}
        public string Material{get=>Get<string>(nameof(Material));set=>Set(nameof(Material),value);}
        public string MaterialName{get=>Get<string>(nameof(MaterialName));set=>Set(nameof(MaterialName),value);}
        public double Health{get=>Get<double>(nameof(Health));set=>Set(nameof(Health),value);}
        public double Attack{get=>Get<double>(nameof(Attack));set=>Set(nameof(Attack),value);}
        public int Gold{get=>Get<int>(nameof(Gold));set=>Set(nameof(Gold),value);}
        public int RecommendedLevel{get=>Get<int>(nameof(RecommendedLevel));set=>Set(nameof(RecommendedLevel),value);}
        public RegionDefinition(string id,string name,string enemy,string boss,string material,string materialName,double health,double attack,int gold,int level):base(id)
        {Name=name;Enemy=enemy;Boss=boss;Material=material;MaterialName=materialName;Health=health;Attack=attack;Gold=gold;RecommendedLevel=level;}
        public override EntityInstance Instantiate(EntityContext context,string id){var state=new RegionState(id,Id,context);CopyValuesTo(state);context.Add(state);return state;}
    }
    public sealed class WaveDefinition:IImmutableValue
    {
        public string EnemyTemplateId{get;}
        public int Count{get;}
        public double HealthFactor{get;}
        public double AttackFactor{get;}
        public WaveDefinition(string enemyTemplateId,int count,double healthFactor=1,double attackFactor=1)
        {if(count<1||count>100)throw new ArgumentOutOfRangeException(nameof(count));EnemyTemplateId=enemyTemplateId;Count=count;HealthFactor=healthFactor;AttackFactor=attackFactor;}
    }
    public sealed class QuestDefinition:EntityTemplate
    {
        public WaveDefinition[] Waves{get=>Get<WaveDefinition[]>(nameof(Waves));set=>Set(nameof(Waves),value);}
        public string Name{get=>Get<string>(nameof(Name));set=>Set(nameof(Name),value);}
        public int Region{get=>Get<int>(nameof(Region));set=>Set(nameof(Region),value);}
        public int Unlock{get=>Get<int>(nameof(Unlock));set=>Set(nameof(Unlock),value);}
        public int CompletionGold{get=>Get<int>(nameof(CompletionGold));set=>Set(nameof(CompletionGold),value);}
        public QuestDefinition(string id,string name,int region,int unlock,int completionGold,params WaveDefinition[] waves):base(id)
        {Name=name;Region=region;Unlock=unlock;CompletionGold=completionGold;Waves=waves;}
    }
    public sealed class EnemyTemplate:EntityTemplate
    {
        private readonly string name,material,abilityId;
        private readonly double health,attack;
        private readonly int gold,xp,drops;
        private readonly bool boss;
        public EnemyTemplate(string id,string name,double health,double attack,int gold,int xp,string material,int drops,bool boss=false,string abilityId=null):base(id)
        {this.name=name;this.health=health;this.attack=attack;this.gold=gold;this.xp=xp;this.material=material;this.drops=drops;this.boss=boss;this.abilityId=abilityId;}
        public override EntityInstance Instantiate(EntityContext context,string id)
        {
            var enemy=new EnemyState(id,Id,context){Name=name,Health=health,MaxHealth=health,Attack=attack,Boss=boss};
            enemy.Define("AbilityId",abilityId);enemy.Define("Gold",gold);enemy.Define("Experience",xp);enemy.Define("Material",material);enemy.Define("Drops",drops);context.Add(enemy);return enemy;
        }
    }
    public sealed class StatBonus:IImmutableValue
    {
        public PropertyKey<double> Property{get;}
        public double PerTier{get;}
        public StatBonus(PropertyKey<double>property,double perTier){Property=property;PerTier=perTier;}
    }
    public sealed class EquipmentTemplate:EntityTemplate
    {
        public GearSlot Slot{get;}
        public IReadOnlyList<StatBonus> Bonuses{get;}
        public EquipmentTemplate(string id,GearSlot slot,params StatBonus[]bonuses):base(id){Slot=slot;Bonuses=Array.AsReadOnly((StatBonus[])bonuses.Clone());new EquipmentState().CopyValuesTo(this);GetValue<int>("Slot").Current=(int)slot;}
        public override EntityInstance Instantiate(EntityContext context,string id){var item=new EquipmentState(id,Id,context);CopyValuesTo(item);context.Add(item);return item;}
    }
    public sealed class StateTemplate:EntityTemplate
    {
        private readonly string kind;
        public StateTemplate(string id,string kind):base(id)
        {
            this.kind=kind;
            Entity prototype=kind=="party"?(Entity)new PartyState():kind=="guild"?new GuildState():new BuildingState();
            prototype.CopyValuesTo(this);
        }
        public override EntityInstance Instantiate(EntityContext context,string id)
        {
            EntityInstance entity=kind=="party"?(EntityInstance)new PartyState(id,Id,context):kind=="guild"?new GuildState(id,Id,context):new BuildingState(id,Id,context);
            CopyValuesTo(entity);context.Add(entity);return entity;
        }
    }
    public sealed class GuildDefinition
    {
        public const int PartySize=4;
        public const int MaximumHeroes=16;
        public const double RoundSeconds=2;
        /// <summary>Seconds per combat round; queued rounds keep their existing deadlines.</summary>
        public double CombatRoundSeconds{get;set;}=RoundSeconds;
        public const double OfflineCap=28800;
        public string Title{get;set;}="Guild";
        public string CurrencyResourceId{get;set;}="gold";
        public Expression<double> TrainingPrice{get;set;}
        public int MaxLevel{get;set;}=30;
        public double LevelBonus{get;set;}=.08;
        public double TrainingBonus{get;set;}=.12;
        public Dictionary<GearSlot,int> GearPrices{get;}=new Dictionary<GearSlot,int>();
        public string[] TierNames{get;set;}
        public Dictionary<string,ResourceAmount[]> CraftPrices{get;}=new Dictionary<string,ResourceAmount[]>();
        public Dictionary<string,ResourceAmount[]> BuildingPrices{get;}=new Dictionary<string,ResourceAmount[]>();
        public ClassDefinition[] Classes{get;set;}
        public RegionDefinition[] Regions{get;set;}
        public string[] PartyNames{get;set;}
        public string[] HeroNames{get;set;}
        public ContentDatabase Economy{get;set;}
        public string[] ProductionIds{get;set;}
        public string[] ProductionNames{get;set;}
        public string[] BuildingNames{get;set;}
        public string[] ResourceIds{get;set;}
        public string[] ResourceNames{get;set;}
        public Dictionary<string,AbilityDefinition> Abilities{get;}=new Dictionary<string,AbilityDefinition>();
        public Dictionary<string,AuraDefinition> Auras{get;}=new Dictionary<string,AuraDefinition>();
        public Dictionary<string,EntityTemplate> Templates{get;}=new Dictionary<string,EntityTemplate>();
        public Dictionary<string,QuestDefinition> Quests{get;}=new Dictionary<string,QuestDefinition>();
        public Dictionary<string,ActionDefinition> Actions{get;}=new Dictionary<string,ActionDefinition>();
        public string QuestId(int region,bool boss)=>Regions[region].Id+(boss?".hunt":".patrol");
        public ClassDefinition Class(string id)=>Classes.First(x=>x.Id==id);
        public int GearPrice(GearSlot slot)=>GearPrices[slot];
        public string GearName(GearSlot slot,int tier)=>tier==0?TierNames[0]:TierNames[tier]+" "+slot.ToString().ToLowerInvariant();
        public ResourceAmount[] CraftCost(GearSlot slot,int tier)=>CraftPrices[slot+":"+tier];
        public ResourceAmount[] BuildingCost(int kind,int level)=>BuildingPrices[kind+":"+level];
    }
}
