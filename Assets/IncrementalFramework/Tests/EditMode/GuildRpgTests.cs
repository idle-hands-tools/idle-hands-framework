using IncrementalFramework.Samples.IdleRpg;
using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using IncrementalFramework.Persistence;
using System;
using System.Linq;
using BreakInfinity;
using IncrementalFramework.Games.Rpg;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace IdleHands.Tests
{
    public sealed class GuildRpgTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 27, 12, 0, 0, DateTimeKind.Utc);
        private static void Fund(GuildGame game, int amount = 100000)
        {
            foreach (string id in EmberGuild.Create().ResourceIds)
                Assert.That(game.Economy.Execute(new GrantResourceCommand(id, amount)).Success, Is.True);
        }
        private static GuildGame Balanced()
        {
            var game = new GuildGame(EmberGuild.Create());
            foreach (var cls in new[] { "guardian", "ranger", "mage", "cleric" })
                Assert.That(game.Hire(cls), Is.True, game.LastMessage);
            foreach (var hero in game.State.Heroes) Assert.That(game.Assign(hero.Number, 0), Is.True);
            return game;
        }
        private static void Advance(GuildGame game, double seconds, int budget = 256)
        {
            double target = game.Time + seconds;
            int calls = 0;
            while (!game.AdvanceTo(target, budget))
                if (++calls > 100000) Assert.Fail("Catch-up did not converge.");
        }

        [Test]
        public void GoldRecruitsDistinctPersistentCharactersAndRejectsUnaffordableHiring()
        {
            var game = new GuildGame(EmberGuild.Create());
            var before = game.Balance("gold");
            Assert.That(game.Hire("guardian"), Is.True);
            Assert.That(game.Balance("gold"), Is.EqualTo(before - 60));
            Assert.That(game.State.Heroes.Single().Class, Is.EqualTo("guardian"));
            Assert.That(game.State.Heroes.Single().Party, Is.EqualTo(-1));
            game.Economy.GetResource("gold").Balance -= 240;
            Assert.That(game.Hire("mage"), Is.False);
            Assert.That(game.State.Heroes.Count, Is.EqualTo(1));
            Advance(game, 120);
            Assert.That(game.Hire("mage"), Is.True, "The contract office prevents a gold soft-lock.");
        }

        [Test]
        public void PartySlotsAreExclusiveAndCannotChangeDuringAQuest()
        {
            var game = Balanced(); Fund(game);
            Assert.That(game.Hire("bard"), Is.True);
            var extra = game.State.Heroes.Last();
            Assert.That(game.Assign(extra.Number, 0), Is.False, "Four slots only.");
            Assert.That(game.Assign(extra.Number, 1), Is.False, "Second party is locked until hall level 2.");
            Assert.That(game.UpgradeBuilding(0), Is.True);
            Assert.That(game.Assign(extra.Number, 1), Is.True);
            Assert.That(game.Assign(game.State.Heroes[1].Number, 1), Is.True);
            Assert.That(game.Members(0).Count, Is.EqualTo(3));
            Assert.That(game.Members(1).Count, Is.EqualTo(2));
            Assert.That(game.Dispatch(0, 0, false), Is.True);
            var hero = game.State.Heroes[0];
            var gold = game.Balance("gold");
            Assert.That(game.Assign(hero.Number, 1), Is.False);
            Assert.That(game.Assign(extra.Number, 0), Is.False);
            Assert.That(game.Train(hero.Number), Is.False);
            Assert.That(game.BuyEquipment(hero.Number, GearSlot.Weapon), Is.False);
            Assert.That(game.Balance("gold"), Is.EqualTo(gold));
        }

        [Test]
        public void BalancedStarterPartyClearsBossEarnsDropsAndUnlocksNextRegion()
        {
            var game = Balanced();
            Assert.That(game.Dispatch(0, 1, true), Is.False);
            Assert.That(game.Dispatch(0, 0, true), Is.True);
            Advance(game, 180);
            Assert.That(game.State.Parties[0].Victories, Is.EqualTo(1));
            Assert.That(game.State.BossesDefeated[0], Is.True);
            Assert.That(game.State.UnlockedRegion, Is.EqualTo(1));
            Assert.That(game.Balance("briar-heart"), Is.EqualTo(new BigDouble(9)));
            Assert.That(game.State.Heroes.All(hero => hero.Experience > 0), Is.True);
            var balance = game.Balance("briar-heart");
            Assert.That(game.AdvanceTo(game.Time), Is.True);
            Assert.That(game.Balance("briar-heart"), Is.EqualTo(balance), "No double loot at the same timestamp.");
        }

        [Test]
        public void GuardianTanksClericHealsMageDamagesMultipleEnemies()
        {
            var game = Balanced();
            game.Dispatch(0, 0, false);
            var party = game.State.Parties[0];
            party.Health[0] -= 60;
            double tankBefore = party.Health[0];
            Advance(game, 2);
            Assert.That(party.Health[0], Is.GreaterThan(tankBefore), "Cleric healing beats low-region incoming damage.");
            Assert.That(party.Health[1], Is.EqualTo(game.MaxHealth(game.State.Heroes[1])), "Guardian draws attacks away from ranger.");
            Assert.That(party.Enemies[1], Is.LessThan(EmberGuild.Create().Regions[0].Health), "Mage's Firestorm reaches the second enemy.");
            Assert.That(party.Report, Does.Contain("healed"));
            Assert.That(party.Report, Does.Contain("Firestorm"));
        }

        [Test]
        public void RogueBurstAndBardSupportHaveDistinctCombatEffects()
        {
            var game = new GuildGame(EmberGuild.Create()); Fund(game);
            game.Hire("rogue"); game.Hire("bard");
            foreach (var hero in game.State.Heroes) game.Assign(hero.Number, 0);
            game.Dispatch(0, 0, true);
            var party = game.State.Parties[0];
            party.Enemies[0] = 10000; party.Enemies[1] = party.Enemies[2] = 0;
            double before = party.Enemies[0];
            Advance(game, 2);
            Assert.That(before - party.Enemies[0], Is.EqualTo((23 + 12) * 1.3).Within(0.001));
            Advance(game, 4);
            Assert.That(party.Report, Does.Contain("backstabbed"));
        }

        [Test]
        public void CraftingAtomicallyConsumesDropsRawMaterialsGoldAndThePriorItem()
        {
            var game = Balanced();
            var hero = game.State.Heroes[0];
            Assert.That(game.Craft(hero.Number, GearSlot.Weapon), Is.False);
            Advance(game, 60);
            Assert.That(game.BuyEquipment(hero.Number, GearSlot.Weapon), Is.True);
            var before = game.Balance("gold");
            Assert.That(game.Craft(hero.Number, GearSlot.Weapon), Is.False);
            Assert.That(game.Balance("gold"), Is.EqualTo(before));
            Assert.That(hero.Weapon, Is.EqualTo(1));
            Fund(game);
            var cost = EmberGuild.Create().CraftCost(GearSlot.Weapon, 2);
            var balances = cost.Select(x => game.Balance(x.ResourceId)).ToArray();
            double attack = game.Attack(hero);
            Assert.That(game.Craft(hero.Number, GearSlot.Weapon), Is.True);
            Assert.That(hero.Weapon, Is.EqualTo(2));
            Assert.That(game.Attack(hero), Is.GreaterThan(attack));
            for (int i = 0; i < cost.Length; i++)
                Assert.That(game.Balance(cost[i].ResourceId), Is.EqualTo(balances[i] - cost[i].Amount));
            Assert.That(game.Craft(hero.Number, GearSlot.Weapon), Is.False, "Workshop level 2 gates tier 3.");
            Assert.That(game.UpgradeBuilding(1), Is.True);
            Assert.That(game.Craft(hero.Number, GearSlot.Weapon), Is.True);
            Assert.That(hero.Weapon, Is.EqualTo(3));
        }

        [Test]
        public void RawProductionFundsConstructionAndTrainingGroundsRaiseTrainingCap()
        {
            var game = Balanced(); Fund(game);
            var hero = game.State.Heroes[0];
            foreach (int index in new[] { 1, 2, 3 }) Assert.That(game.UpgradeProduction(index), Is.True);
            var wood = game.Balance("timber"); var ore = game.Balance("ore");
            Advance(game, 100);
            Assert.That(game.Balance("timber"), Is.EqualTo(wood + 60));
            Assert.That(game.Balance("ore"), Is.EqualTo(ore + 30));
            for (int i = 0; i < 5; i++) Assert.That(game.Train(hero.Number), Is.True);
            Assert.That(game.Train(hero.Number), Is.False);
            Assert.That(game.UpgradeBuilding(2), Is.True);
            Assert.That(game.Train(hero.Number), Is.True);
            Assert.That(hero.Training, Is.EqualTo(6));
        }

        [Test]
        public void DefeatStopsRepeatPreservesRosterAndRequiresRecovery()
        {
            var game = new GuildGame(EmberGuild.Create()); game.Hire("ranger");
            game.Assign(game.State.Heroes[0].Number, 0);
            game.State.UnlockedRegion = 3;
            game.SetRepeat(0, true);
            game.Dispatch(0, 3, true);
            Advance(game, 2);
            Assert.That(game.State.Parties[0].Defeats, Is.EqualTo(1));
            Assert.That(game.State.Parties[0].AutoRepeat, Is.False);
            Assert.That(game.State.Heroes.Count, Is.EqualTo(1));
            Assert.That(game.Dispatch(0, 0, false), Is.False);
            Advance(game, 20);
            Assert.That(game.Dispatch(0, 0, false), Is.True);
        }

        [Test]
        public void ConcurrentPartiesResolveIndependentlyAndCannotShareAdventurers()
        {
            var game = Balanced(); Fund(game);
            game.UpgradeBuilding(0);
            game.Hire("guardian"); game.Hire("mage");
            foreach (var hero in game.State.Heroes.Skip(4)) game.Assign(hero.Number, 1);
            game.Dispatch(0, 0, false); game.Dispatch(1, 0, false);
            Advance(game, 4);
            Assert.That(game.State.Parties[0].Round, Is.EqualTo(2));
            Assert.That(game.State.Parties[1].Round, Is.EqualTo(2));
            game.Recall(0); Advance(game, 2);
            Assert.That(game.State.Parties[0].Active, Is.False);
            Assert.That(game.State.Parties[1].Round, Is.EqualTo(3));
        }

        [Test]
        public void MidCombatSaveAndBudgetedOfflineCatchupMatchContinuousSimulation()
        {
            var game = Balanced(); game.SetRepeat(0, true); game.Dispatch(0, 0, false);
            Advance(game, 6);
            string json = game.Save(Now);
            var resumed = GuildGame.Load(EmberGuild.Create(), json, Now.AddDays(1), out double offline);
            Assert.That(offline, Is.EqualTo(28800));
            var continuous = GuildGame.Load(EmberGuild.Create(), json, Now, out _);
            Advance(resumed, offline, 1);
            Advance(continuous, offline, 100000);
            Assert.That(resumed.Save(Now), Is.EqualTo(continuous.Save(Now)), "All combat, loot, XP, and balances must match.");
            var backwards = GuildGame.Load(EmberGuild.Create(), json, Now.AddHours(-1), out double negative);
            Assert.That(negative, Is.EqualTo(0));
            Assert.That(backwards.Time, Is.EqualTo(game.Time));
        }

        [Test]
        public void InvalidSavesRejectDuplicateHeroesAndImpossibleCombatTimers()
        {
            var game = Balanced(); game.Dispatch(0, 0, false);
            var json = JObject.Parse(game.Save(Now));
            var records=(JArray)json["entities"]["entities"];
            records[1]["id"]=records[0]["id"].DeepClone();
            Assert.Throws<ArgumentException>(()=>GuildGame.Load(EmberGuild.Create(),json.ToString(),Now,out _));
            json=JObject.Parse(game.Save(Now));
            var partyRecord=((JArray)json["entities"]["entities"]).First(x=>(string)x["id"]=="party:0");
            partyRecord["properties"]["NextRound"]=-1;
            Assert.Throws<ArgumentException>(()=>GuildGame.Load(EmberGuild.Create(),json.ToString(),Now,out _));
        }

        [Test]
        public void CampaignCanReachAllRegionsAndCraftEmberEquipmentWithEarnedResources()
        {
            var game = Balanced();
            game.SetRepeat(0, true); game.Dispatch(0, 0, true);
            for (int step = 0; step < 600 && game.State.Heroes[0].Weapon < 5; step++)
            {
                Advance(game, 120);
                // Collect earned loot, return to town, invest, then resume the hardest unlocked boss.
                if (game.State.Parties[0].Active) game.Recall(0);
                Advance(game, 20);
                for (int building = 1; building <= 3; building++)
                    if (game.Economy.GetProducer(game.Definition.ProductionIds[building]).Owned < 3)
                        game.UpgradeProduction(building);
                for (int building = 1; building < 3; building++)
                    if (game.State.Buildings[building] < 4) game.UpgradeBuilding(building);
                foreach (var hero in game.State.Heroes)
                {
                    if (hero.Training < game.State.Buildings[2] * 5) game.Train(hero.Number);
                    for (int slot = 0; slot < 3; slot++)
                    {
                        if (hero.Gear((GearSlot)slot) == 0) game.BuyEquipment(hero.Number, (GearSlot)slot);
                        else game.Craft(hero.Number, (GearSlot)slot);
                    }
                }
                // Farm a region until everyone has its crafting tier before moving onward.
                int region = Math.Min(game.State.UnlockedRegion, Math.Max(0, game.State.Heroes.Min(hero => hero.Weapon) - 1));
                game.SetRepeat(0, true); game.Dispatch(0, region, true);
            }
            Assert.That(game.State.BossesDefeated.All(x => x), Is.True, "All four bosses should be beatable without resource grants.");
            Assert.That(game.State.Heroes[0].Weapon, Is.EqualTo(5), "The final enemy material must be obtainable and craftable.");
        }
    }
}
