using System;
using System.Collections.Generic;
using System.Linq;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;

namespace IncrementalFramework.Games.Rpg
{
    /// <summary>Reusable entity lifecycle effects; prices and eligibility are declared by game content.</summary>
    public sealed class RecruitEffect:EffectDefinition
    {
        private readonly ClassDefinition template;
        private readonly string[] names;
        public RecruitEffect(ClassDefinition template,IEnumerable<string>names){this.template=template;this.names=names.ToArray();}
        public override IEnumerable<PropertyReference> References=>Array.Empty<PropertyReference>();
        public override void Stage(EvaluationContext context,EntityTransaction transaction)
        {
            var guild=(GuildState)context.Self;
            int number=transaction.Read(guild.GetValue<int>("NextHeroId"));
            var hero=(HeroState)template.Instantiate(new EntityContext(),transaction.ReserveId(context.World,"hero"));
            hero.Number=number;hero.Name=names[(number-1)%names.Length]+" "+number;
            transaction.Set(guild.GetValue<int>("NextHeroId"),number+1);
            transaction.Set(guild.HeroIds,Array.AsReadOnly(transaction.Read(guild.HeroIds).Concat(new[]{hero.Id}).ToArray()));
            transaction.Create(context.World,hero);
        }
    }
    public sealed class AssignPartyEffect:EffectDefinition
    {
        private readonly bool remove;
        public AssignPartyEffect(bool remove=false){this.remove=remove;}
        public override IEnumerable<PropertyReference> References=>Array.Empty<PropertyReference>();
        public override void Stage(EvaluationContext context,EntityTransaction transaction)
        {
            var hero=(HeroState)context.Self;
            foreach(var party in context.World.Entities.Values.OfType<PartyState>())
            {
                var ids=transaction.Read(party.MemberIds);
                if(ids.Contains(hero.Id))transaction.Set(party.MemberIds,Array.AsReadOnly(ids.Where(id=>id!=hero.Id).ToArray()));
            }
            if(!remove)
            {
                var destination=(PartyState)context.Target;
                transaction.Set(destination.MemberIds,Array.AsReadOnly(transaction.Read(destination.MemberIds).Concat(new[]{hero.Id}).ToArray()));
            }
        }
    }
    public sealed class EquipEffect:EffectDefinition
    {
        private readonly EquipmentTemplate template;
        private readonly int tier;
        public EquipEffect(EquipmentTemplate template,int tier){this.template=template;this.tier=tier;}
        public override IEnumerable<PropertyReference> References=>Array.Empty<PropertyReference>();
        public override void Stage(EvaluationContext context,EntityTransaction transaction)
        {
            var hero=(HeroState)context.Self;
            var key=new PropertyKey<string>(template.Slot+"Id");
            var old=hero.GetValue(key);
            if(!string.IsNullOrEmpty(transaction.Read(old)))transaction.Remove(context.World.Find(transaction.Read(old)));
            string id=transaction.ReserveId(context.World,"item");
            var item=(EquipmentState)template.Instantiate(new EntityContext(),id);item.Tier=tier;item.Owner=hero.Id;
            transaction.Create(context.World,item);transaction.Set(old,id);
            foreach(var bonus in template.Bonuses)
                new NumericModifier<double>(id+":"+bonus.Property.Name,id,bonus.Property,NumericOperation.Flat,bonus.PerTier*tier).Stage(hero,transaction);
        }
    }
}
