using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.Core;
using IncrementalFramework.Combat;

namespace IncrementalFramework.Games.Rpg
{
    public enum GearSlot{Weapon,Armor,Charm}
    public static class RpgProperties
    {
        public static readonly PropertyKey<int> Experience=new PropertyKey<int>("Experience");
        public static readonly PropertyKey<int> Training=new PropertyKey<int>("Training");
        public static readonly PropertyKey<int> Level=new PropertyKey<int>("Level");
        public static readonly PropertyKey<double> Scale=new PropertyKey<double>("Scale");
        public static readonly PropertyKey<int> Tier=new PropertyKey<int>("Tier");
        public static readonly PropertyKey<bool> Busy=new PropertyKey<bool>("Busy");
        public static readonly PropertyKey<IReadOnlyList<string>> Members=new PropertyKey<IReadOnlyList<string>>("MemberIds");
    }
    public sealed class HeroState:Combatant
    {
        public int Number{get=>Get<int>(nameof(Number));set=>Set(nameof(Number),value);}
        public string Class{get=>Get<string>(nameof(Class));set=>Set(nameof(Class),value);}
        public int Experience{get=>Get<int>(nameof(Experience));set=>Set(nameof(Experience),value);}
        public int Training{get=>Get<int>(nameof(Training));set=>Set(nameof(Training),value);}
        public int Party{get=>Get<int>(nameof(Party));set=>Set(nameof(Party),value);}
        public int Level{get=>Get<int>(nameof(Level));set=>Set(nameof(Level),value);}
        public double Scale{get=>Get<double>(nameof(Scale));set=>Set(nameof(Scale),value);}
        public bool Busy{get=>Get<bool>(nameof(Busy));set=>Set(nameof(Busy),value);}
        public int TrainingCap{get=>Get<int>(nameof(TrainingCap));set=>Set(nameof(TrainingCap),value);}
        public int WorkshopLevel{get=>Get<int>(nameof(WorkshopLevel));set=>Set(nameof(WorkshopLevel),value);}
        public string AbilityId{get=>Get<string>(nameof(AbilityId));set=>Set(nameof(AbilityId),value);}
        public string AuraId{get=>Get<string>(nameof(AuraId));set=>Set(nameof(AuraId),value);}
        public int Weapon{get=>Get<int>(nameof(Weapon));set=>Set(nameof(Weapon),value);}
        public int Armor{get=>Get<int>(nameof(Armor));set=>Set(nameof(Armor),value);}
        public int Charm{get=>Get<int>(nameof(Charm));set=>Set(nameof(Charm),value);}
        public string WeaponId{get=>Get<string>(nameof(WeaponId));set=>Set(nameof(WeaponId),value);}
        public string ArmorId{get=>Get<string>(nameof(ArmorId));set=>Set(nameof(ArmorId),value);}
        public string CharmId{get=>Get<string>(nameof(CharmId));set=>Set(nameof(CharmId),value);}
        public HeroState(string id=null,string template=null,EntityContext context=null):base(id,template,context)
        {
            Number=0;Class="";Experience=0;Training=0;Level=1;Scale=1;Party=-1;Busy=false;TrainingCap=5;WorkshopLevel=1;AbilityId="";AuraId="";
            Weapon=Armor=Charm=0;WeaponId=ArmorId=CharmId="";
        }
        public int Gear(GearSlot slot)=>GetValue<int>(slot.ToString()).Current;
        public string GearId(GearSlot slot)=>GetValue<string>(slot+"Id").Current;
        protected override EntityInstance CreateClone()=>new HeroState(Id,TemplateId);
    }
    public sealed class EquipmentState:EntityInstance
    {
        public int Tier{get=>Get<int>(nameof(Tier));set=>Set(nameof(Tier),value);}
        public string Owner{get=>Get<string>(nameof(Owner));set=>Set(nameof(Owner),value);}
        public int Slot{get=>Get<int>(nameof(Slot));set=>Set(nameof(Slot),value);}
        public EquipmentState(string id=null,string template=null,EntityContext context=null):base(id,template,context){Kind="equipment";Tier=1;Owner="";Slot=0;}
    }
    public sealed class BuildingState:EntityInstance
    {
        public int Level{get=>Get<int>(nameof(Level));set=>Set(nameof(Level),value);}
        public BuildingState(string id=null,string template=null,EntityContext context=null):base(id,template,context){Kind="building";Level=1;}
    }
    public sealed class EnemyState:Combatant
    {
        public bool Rewarded{get=>Get<bool>(nameof(Rewarded));set=>Set(nameof(Rewarded),value);}
        public bool Boss{get=>Get<bool>(nameof(Boss));set=>Set(nameof(Boss),value);}
        public EnemyState(string id=null,string template=null,EntityContext context=null):base(id,template,context){Rewarded=false;Boss=false;Team=1;}
    }
    public sealed class RegionState:EntityInstance
    {
        public bool BossDefeated{get=>Get<bool>(nameof(BossDefeated));set=>Set(nameof(BossDefeated),value);}
        public RegionState(string id=null,string template=null,EntityContext context=null):base(id,template,context){Kind="region";BossDefeated=false;}
    }
    public sealed class PartyState:EntityInstance
    {
        public string Name{get=>Get<string>(nameof(Name));set=>Set(nameof(Name),value);}
        public bool Active{get=>Get<bool>(nameof(Active));set=>Set(nameof(Active),value);}
        public bool AutoRepeat{get=>Get<bool>(nameof(AutoRepeat));set=>Set(nameof(AutoRepeat),value);}
        public bool Boss{get=>Get<bool>(nameof(Boss));set=>Set(nameof(Boss),value);}
        public int Region{get=>Get<int>(nameof(Region));set=>Set(nameof(Region),value);}
        public int Wave{get=>Get<int>(nameof(Wave));set=>Set(nameof(Wave),value);}
        public int Round{get=>Get<int>(nameof(Round));set=>Set(nameof(Round),value);}
        public int Victories{get=>Get<int>(nameof(Victories));set=>Set(nameof(Victories),value);}
        public int Defeats{get=>Get<int>(nameof(Defeats));set=>Set(nameof(Defeats),value);}
        public double NextRound{get=>Get<double>(nameof(NextRound));set=>Set(nameof(NextRound),value);}
        public double RecoveryUntil{get=>Get<double>(nameof(RecoveryUntil));set=>Set(nameof(RecoveryUntil),value);}
        public string QuestId{get=>Get<string>(nameof(QuestId));set=>Set(nameof(QuestId),value);}
        public string Report{get=>Get<string>(nameof(Report));set=>Set(nameof(Report),value);}
        public ValueList<string> MemberIds=>(ValueList<string>)GetValue("MemberIds");
        public ValueList<string> EnemyIds=>(ValueList<string>)GetValue("EnemyIds");
        public ValueArray<double> Health=>HealthArray(MemberIds.Current,4);
        public ValueArray<double> Enemies=>HealthArray(EnemyIds.Current,3);
        private ValueArray<double> HealthArray(IReadOnlyList<string> ids,int count)=>new ValueArray<double>(count,i=>i<ids.Count?((Combatant)Context.Find(ids[i])).Health:0,(i,value)=>{if(i<ids.Count)((Combatant)Context.Find(ids[i])).Health=value;});
        public PartyState(string id=null,string template=null,EntityContext context=null):base(id,template,context)
        {
            Kind="party";QuestId="";Name="Company";Active=AutoRepeat=Boss=false;Region=Wave=Round=Victories=Defeats=0;NextRound=RecoveryUntil=0;
            Report="Choose adventurers, then dispatch a quest.";DefineList<string>("MemberIds");DefineList<string>("EnemyIds");
        }
    }
    public sealed class GuildState:EntityInstance
    {
        public ValueList<string> HeroIds=>(ValueList<string>)GetValue("HeroIds");
        public ValueList<string> PartyIds=>(ValueList<string>)GetValue("PartyIds");
        public ValueList<string> RegionIds=>(ValueList<string>)GetValue("RegionIds");
        public ValueList<string> BuildingIds=>(ValueList<string>)GetValue("BuildingIds");
        public ValueList<string> Journal=>(ValueList<string>)GetValue("Log");
        public IReadOnlyList<HeroState> Heroes=>HeroIds.Current.Select(id=>(HeroState)Context.Find(id)).ToArray();
        public PartyState[] Parties=>PartyIds.Current.Select(id=>(PartyState)Context.Find(id)).ToArray();
        public ValueArray<int> Buildings=>new ValueArray<int>(BuildingIds.Current.Count,i=>((BuildingState)Context.Find(BuildingIds.Current[i])).Level,(i,value)=>((BuildingState)Context.Find(BuildingIds.Current[i])).Level=value);
        public ValueArray<bool> BossesDefeated=>new ValueArray<bool>(RegionIds.Current.Count,i=>((RegionState)Context.Find(RegionIds.Current[i])).BossDefeated,(i,value)=>((RegionState)Context.Find(RegionIds.Current[i])).BossDefeated=value);
        public IReadOnlyList<string> Log=>Journal.Current;
        public int UnlockedRegion{get=>Get<int>(nameof(UnlockedRegion));set=>Set(nameof(UnlockedRegion),value);}
        public int NextHeroId{get=>Get<int>(nameof(NextHeroId));set=>Set(nameof(NextHeroId),value);}
        public int HeroCapacity{get=>Get<int>(nameof(HeroCapacity));set=>Set(nameof(HeroCapacity),value);}
        public GuildState(string id=null,string template=null,EntityContext context=null):base(id,template,context)
        {Kind="guild";DefineList<string>("HeroIds");DefineList<string>("PartyIds");DefineList<string>("BuildingIds");DefineList<string>("RegionIds");DefineList<string>("Log");UnlockedRegion=0;NextHeroId=1;HeroCapacity=8;}
    }
}
