using System;
using System.Collections.Generic;
using System.Linq;
using BreakInfinity;
using IncrementalFramework.Core;
using IncrementalFramework.Definitions;
using IncrementalFramework.Economy;
using IncrementalFramework.Combat;
using IncrementalFramework.Games.Rpg;

namespace IncrementalFramework.Samples.IdleRpg
{
    /// <summary>The game's content is declared here; shared RPG execution does not know these identifiers.</summary>
    public static class EmberGuild
    {
        public static ResourceAmount[] Costs(params (string Id,double Amount)[] values)=>values.Select(x=>new ResourceAmount(x.Id,x.Amount)).ToArray();
        public static GuildDefinition Create()
        {
            var game=new GuildDefinition
            {
                Title="EMBER GUILD",PartyNames=new[]{"Dawn company","Dusk company","Star company"},
                HeroNames=new[]{"Ash","Rowan","Wren","Mira","Sage","Lark","Bram","Iris"},
                ResourceIds=new[]{"gold","timber","stone","ore","briar-heart","hydra-scale","frost-core","ember-shard"},
                ResourceNames=new[]{"Gold","Timber","Stone","Iron ore","Briar hearts","Hydra scales","Frost cores","Ember shards"},
                ProductionIds=new[]{"contracts","lumber","quarry","mine"},ProductionNames=new[]{"Contract office","Lumberyard","Quarry","Iron mine"},
                BuildingNames=new[]{"Guild hall","Workshop","Training grounds"},TierNames=new[]{"None","Iron","Briarforged","Hydrascale","Frostbound","Emberforged"}
            };
            ResourceAmount[] Costs(params (string Id,double Amount)[] values)=>values.Select(x=>new ResourceAmount(x.Id,x.Amount)).ToArray();
            game.Economy=new ContentDatabase(game.ResourceIds.Select(id=>new ResourceDefinition(id,id=="gold"?380:id=="timber"?40:id=="stone"?30:0)).ToArray(),new[]
            {
                new ProducerDefinition("contracts",Costs(("gold",80),("timber",15)),Costs(("gold",.75)),1.6),
                new ProducerDefinition("lumber",Costs(("gold",45),("stone",10)),Costs(("timber",.6)),1.6),
                new ProducerDefinition("quarry",Costs(("gold",50),("timber",15)),Costs(("stone",.4)),1.6),
                new ProducerDefinition("mine",Costs(("gold",90),("timber",30),("stone",25)),Costs(("ore",.3)),1.6)
            });
            var attack=Expr.Multiply(Expr.Property(CombatProperties.Attack),Expr.Property(CombatProperties.PartyBonus));
            var healing=Expr.Multiply(Expr.Property(CombatProperties.Healing),Expr.Property(CombatProperties.PartyBonus));
            game.Abilities.Add("strike",new AbilityDefinition("strike",new CombatEffect(false,attack,CombatTargets.Living(false).Take(1))));
            game.Abilities.Add("aimed-shot",new AbilityDefinition("aimed-shot",new CombatEffect(false,Expr.Multiply(attack,Expr.Constant(1.5)),CombatTargets.Living(false).Take(1))));
            var thirdRound=Expr.Equal(Expr.Modulo(Expr.Argument<int>("round"),Expr.Constant(3)),Expr.Constant(0));
            game.Abilities.Add("backstab",new AbilityDefinition("backstab",new CombatEffect(false,Expr.Multiply(attack,Expr.If(thirdRound,Expr.Constant(3d),Expr.Constant(1d))),CombatTargets.Living(false).Take(1),"backstabbed")));
            game.Abilities.Add("firestorm",new AbilityDefinition("firestorm",new CombatEffect(false,attack,CombatTargets.Living(false),"cast Firestorm on")));
            game.Abilities.Add("heal-strike",new AbilityDefinition("heal-strike",new CombatEffect(true,healing,CombatTargets.MostInjuredAlly()),new CombatEffect(false,attack,CombatTargets.Living(false).Take(1))));
            game.Abilities.Add("enemy-strike",new AbilityDefinition("enemy-strike",new CombatEffect(false,attack,CombatTargets.PriorityEnemy())));
            game.Auras.Add("song",new AuraDefinition("song",CombatProperties.PartyBonus,1.3));
            game.Classes=new[]
            {
                new ClassDefinition("guardian","Guardian","Tank","Taunts enemies; shield reduces incoming damage by 45%.",60,180,12,9,"strike",priority:100,incoming:.55),
                new ClassDefinition("ranger","Ranger","Single target","Aimed shot deals 150% damage to one enemy.",65,100,24,3,"aimed-shot"),
                new ClassDefinition("rogue","Rogue","Burst damage","Backstab deals triple damage every third round.",75,95,23,3,"backstab"),
                new ClassDefinition("mage","Mage","Area damage","Firestorm strikes every living enemy.",80,80,17,2,"firestorm"),
                new ClassDefinition("cleric","Cleric","Healer","Heals the most injured living ally before attacking.",70,110,9,4,"heal-strike"),
                new ClassDefinition("bard","Bard","Support","Songs boost party damage and healing by 30% (does not stack).",70,105,12,3,"strike","song")
            };
            game.Regions=new[]
            {
                new RegionDefinition("briarwood","Briarwood","Briar wolf","Ancient treant","briar-heart","Briar hearts",65,12,14,1),
                new RegionDefinition("marsh","Sunken Marsh","Bog lurker","Mire hydra","hydra-scale","Hydra scales",170,24,32,5),
                new RegionDefinition("frostpeak","Frostpeak","Frost wraith","Glacier colossus","frost-core","Frost cores",390,43,68,10),
                new RegionDefinition("rift","Ember Rift","Ash drake","Ember dragon","ember-shard","Ember shards",780,70,135,16)
            };
            var party=new StateTemplate("party","party");party.Define("Index",0);
            party.Define("MemberCount",0);
            game.Templates.Add("party",party);game.Templates.Add("guild",new StateTemplate("guild","guild"));game.Templates.Add("building",new StateTemplate("building","building"));
            var gear=new[]
            {
                new EquipmentTemplate("gear.weapon",GearSlot.Weapon,new StatBonus(CombatProperties.Attack,9),new StatBonus(CombatProperties.Healing,3)),
                new EquipmentTemplate("gear.armor",GearSlot.Armor,new StatBonus(CombatProperties.MaxHealth,28),new StatBonus(CombatProperties.Defense,2)),
                new EquipmentTemplate("gear.charm",GearSlot.Charm,new StatBonus(CombatProperties.MaxHealth,12),new StatBonus(CombatProperties.Healing,9))
            };
            foreach(var template in gear)game.Templates.Add(template.Id,template);
            foreach(var template in game.Classes)game.Templates.Add(template.Id,template);
            for(int index=0;index<game.Regions.Length;index++)
            {
                var region=game.Regions[index];game.Templates.Add(region.Id,region);
                game.Templates.Add(region.Id+".enemy",new EnemyTemplate(region.Id+".enemy",region.Enemy,region.Health,region.Attack,region.Gold,12+index*6,region.Material,1,abilityId:"enemy-strike"));
                game.Templates.Add(region.Id+".boss",new EnemyTemplate(region.Id+".boss",region.Boss,region.Health,region.Attack,region.Gold*4,12+index*6,region.Material,4,true,"enemy-strike"));
                var patrol=new QuestDefinition(region.Id+".patrol",region.Name+" patrol",index,-1,region.Gold*3,
                    new WaveDefinition(region.Id+".enemy",2),new WaveDefinition(region.Id+".enemy",3,1.12),new WaveDefinition(region.Id+".enemy",2,1.24));
                var hunt=new QuestDefinition(region.Id+".hunt",region.Name+" / "+region.Boss,index,Math.Min(3,index+1),region.Gold*3,
                    new WaveDefinition(region.Id+".enemy",2),new WaveDefinition(region.Id+".enemy",3,1.12),new WaveDefinition(region.Id+".boss",1,2.6,1.7));
                game.Quests.Add(patrol.Id,patrol);game.Quests.Add(hunt.Id,hunt);game.Templates.Add(patrol.Id,patrol);game.Templates.Add(hunt.Id,hunt);
            }
            var balance=new PropertyKey<BigDouble>("Balance");
            EffectDefinition Spend(string resource,BigDouble amount)=>Effects.Spend(balance,Expr.Constant(amount),TargetQuery.ById(Expr.Constant(resource)));
            var atHome=Expr.Not(Expr.Property(RpgProperties.Busy));
            foreach(var cls in game.Classes)
            {
                var requirement=Expr.Less(Expr.Count(TargetQuery.Related(new PropertyKey<IReadOnlyList<string>>("HeroIds"))),Expr.Property(new PropertyKey<int>("HeroCapacity")));
                game.Actions.Add("hire:"+cls.Id,new ActionDefinition("hire:"+cls.Id,requirement,Spend("gold",cls.HireGold),new RecruitEffect(cls,game.HeroNames)));
            }
            var capacity=Expr.Less(Expr.Property(new PropertyKey<int>("MemberCount"),EntityScope.Target),Expr.Constant(4));
            game.Actions.Add("assign",new ActionDefinition("assign",Expr.And(atHome,Expr.Not(Expr.Property(new PropertyKey<bool>("Active"),EntityScope.Target)),capacity),new AssignPartyEffect()));
            game.Actions.Add("rest",new ActionDefinition("rest",atHome,new AssignPartyEffect(true)));
            var training=Expr.Property(RpgProperties.Training);
            game.TrainingPrice=Expr.Ceiling(Expr.Multiply(Expr.Constant(30d),Expr.Power(Expr.Add(Expr.ToDouble(training),Expr.Constant(1d)),Expr.Constant(1.35))));
            var trainingPrice=EconomyExpressions.Big(game.TrainingPrice);
            game.Actions.Add("train",new ActionDefinition("train",Expr.And(atHome,Expr.Less(training,Expr.Property(new PropertyKey<int>("TrainingCap")))),
                Effects.Spend(balance,trainingPrice,TargetQuery.ById(Expr.Constant("gold"))),Effects.Add(RpgProperties.Training,Expr.Constant(1)),
                Effects.Attach("training","training",RpgProperties.Scale,NumericOperation.Flat,Expr.Multiply(Expr.ToDouble(training),Expr.Constant(.12)))));
            foreach(var equipment in gear)
            {
                var slot=equipment.Slot;int price=slot==GearSlot.Weapon?40:slot==GearSlot.Armor?35:50;game.GearPrices.Add(slot,price);
                game.Actions.Add("buy:"+slot,new ActionDefinition("buy:"+slot,Expr.And(atHome,Expr.Equal(Expr.Property(new PropertyKey<int>(slot.ToString())),Expr.Constant(0))),Spend("gold",price),new EquipEffect(equipment,1)));
                for(int tier=2;tier<=5;tier++)
                {
                    var costs=Costs(("gold",35*tier),("ore",5*tier),("timber",4*tier),(game.Regions[tier-2].Material,6+tier*2));
                    game.CraftPrices.Add(slot+":"+tier,costs);
                    var requirement=Expr.And(atHome,Expr.Equal(Expr.Property(new PropertyKey<int>(slot.ToString())),Expr.Constant(tier-1)),Expr.AtLeast(Expr.Property(new PropertyKey<int>("WorkshopLevel")),Expr.Constant(tier-1)));
                    game.Actions.Add("craft:"+slot+":"+tier,new ActionDefinition("craft:"+slot+":"+tier,requirement,costs.Select(x=>Spend(x.ResourceId,x.Amount)).Concat(new[]{(EffectDefinition)new EquipEffect(equipment,tier)}).ToArray()));
                }
            }
            for(int kind=0;kind<3;kind++)for(int level=1;level<(kind==0?3:4);level++)
            {
                var costs=Costs(("gold",70*Math.Pow(1.8,level)),("timber",30*Math.Pow(1.8,level)),("stone",20*Math.Pow(1.8,level)),("ore",kind==1?10*level:0));
                game.BuildingPrices.Add(kind+":"+level,costs);
                game.Actions.Add("building:"+kind+":"+level,new ActionDefinition("building:"+kind+":"+level,Expr.Equal(Expr.Property(new PropertyKey<int>("Level")),Expr.Constant(level)),
                    costs.Select(x=>Spend(x.ResourceId,x.Amount)).Concat(new[]{Effects.Add(new PropertyKey<int>("Level"),Expr.Constant(1))}).ToArray()));
            }
            var schemas=game.Templates.Values.Select(template=>(Entity)template.Instantiate(new EntityContext(),"validation")).Concat(game.Economy.Resources.Select(template=>(Entity)template.Instantiate(new EntityContext(),"validation"))).ToArray();
            foreach(var action in game.Actions.Values)DefinitionValidator.Validate(action,schemas);
            return game;
        }
    }
}
