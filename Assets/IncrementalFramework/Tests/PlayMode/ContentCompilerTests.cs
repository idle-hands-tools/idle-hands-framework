using ResourceAmount = IncrementalFramework.Unity.ResourceAmount;
using RequirementKind = IncrementalFramework.Unity.RequirementKind;
using RuleActionKind = IncrementalFramework.Unity.RuleActionKind;
using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using IncrementalFramework.Persistence;
using System;
using IncrementalFramework.Unity;
using NUnit.Framework;
using UnityEngine;

namespace IdleHands.Tests
{
    public sealed class ContentCompilerTests
    {
        [Test]
        public void NumericFormattingHonorsAuthoredNotationAndGrouping()
        {
            Assert.That(new ResourceNumberFormatter(ResourceNumberFormat.Fixed, 2, true)
                .Format(new BreakInfinity.BigDouble(1234.5)), Is.EqualTo("1,234.50"));
            Assert.That(new ResourceNumberFormatter(ResourceNumberFormat.Compact, 2, false)
                .Format(new BreakInfinity.BigDouble(1500000)), Is.EqualTo("1.50M"));
            Assert.That(new ResourceNumberFormatter(ResourceNumberFormat.Engineering, 2, false)
                .Format(new BreakInfinity.BigDouble(1500000)), Is.EqualTo("1.50e6"));
            Assert.That(new ResourceNumberFormatter(ResourceNumberFormat.Engineering, 2, false)
                .Format(new BreakInfinity.BigDouble(0.0001)), Is.EqualTo("100.00e-6"));
        }

        [Test]
        public void PresenterCanSubscribeToCoalescedCoreChanges()
        {
            var simulation = new IncrementalFramework.Progression.Simulation(new IncrementalFramework.Economy.ContentDatabase(
                new[] { new IncrementalFramework.Economy.ResourceDefinition("cookies") }));
            int notifications = 0;
            IncrementalFramework.Progression.SimulationChangeSet last = null;
            simulation.Changed += changes => { notifications++; last = changes; };
            Assert.That(simulation.Execute(new IncrementalFramework.Progression.GrantResourceCommand("cookies", 1)).Success, Is.True);
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(simulation.Execute(new IncrementalFramework.Progression.GrantResourceCommand("cookies", -1)).Success, Is.False);
            Assert.That(notifications, Is.EqualTo(1));
            simulation.AdvanceTo(1);
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(simulation.Execute(new IncrementalFramework.Progression.GrantResourceCommand("cookies", 2)).Success, Is.True);
            Assert.That(notifications, Is.EqualTo(2));
            Assert.That(last.Deltas.Count, Is.EqualTo(1));
            Assert.That(last.Deltas[0].After.Amount, Is.EqualTo(new BreakInfinity.BigDouble(3)));
        }

        [Test]
        public void CompilesCapsCategoriesAndAllModifierStagesWithoutDroppingPriority()
        {
            var content = ScriptableObject.CreateInstance<GameContentAsset>();
            var resource = ScriptableObject.CreateInstance<ResourceAsset>();
            var secondResource = ScriptableObject.CreateInstance<ResourceAsset>();
            var producer = ScriptableObject.CreateInstance<ProducerAsset>();
            var upgrade = ScriptableObject.CreateInstance<UpgradeAsset>();
            try
            {
                resource.id = "cookies";
                resource.category = "currency";
                resource.hasCap = true;
                resource.cap = "100";
                secondResource.id = "flour";
                producer.id = "cursor";
                producer.category = "bakery";
                producer.costs = new[]
                {
                    new ResourceAmount { resource = resource, amount = "1" },
                    new ResourceAmount { resource = secondResource, amount = "2" }
                };
                producer.productionPerSecond = new[]
                {
                    new ResourceAmount { resource = resource, amount = "1" },
                    new ResourceAmount { resource = secondResource, amount = "0.5" }
                };
                upgrade.id = "stages";
                upgrade.modifiers = new[]
                {
                    new ProductionModifier { scope = AuthoredModifierScope.ProducerCategory, targetId = "bakery",
                        kind = AuthoredModifierKind.Flat, value = "-1", priority = 2 },
                    new ProductionModifier { scope = AuthoredModifierScope.ResourceCategory, targetId = "currency",
                        kind = AuthoredModifierKind.AddPercent, value = "0.1" },
                    new ProductionModifier { scope = AuthoredModifierScope.Producer, targetId = "cursor",
                        kind = AuthoredModifierKind.Override, value = "3", priority = 5 },
                    new ProductionModifier { scope = AuthoredModifierScope.Global,
                        kind = AuthoredModifierKind.Multiply, value = "2" }
                };
                content.resources = new[] { resource, secondResource };
                content.producers = new[] { producer };
                content.upgrades = new[] { upgrade };
                content.clickResource = resource;
                var compiled = ContentCompiler.Compile(content).Content;
                Assert.That(compiled.Resources[0].Cap.Value, Is.EqualTo(new BreakInfinity.BigDouble(100)));
                Assert.That(compiled.Resources[0].Category, Is.EqualTo("currency"));
                Assert.That(compiled.Producers[0].Category, Is.EqualTo("bakery"));
                Assert.That(compiled.Producers[0].Costs.Count, Is.EqualTo(2));
                Assert.That(compiled.Producers[0].Production.Count, Is.EqualTo(2));
                Assert.That(compiled.Producers[0].Costs[1].ResourceId, Is.EqualTo("flour"));
                Assert.That(compiled.Upgrades[0].Modifiers.Count, Is.EqualTo(4));
                Assert.That(compiled.Upgrades[0].Modifiers[0].Kind, Is.EqualTo(IncrementalFramework.Economy.ModifierKind.Flat));
                Assert.That(compiled.Upgrades[0].Modifiers[0].Value, Is.EqualTo(new BreakInfinity.BigDouble(-1)));
                Assert.That(compiled.Upgrades[0].Modifiers[0].Target, Is.EqualTo(IncrementalFramework.Economy.ModifierTarget.ProducerCategoryProduction));
                Assert.That(compiled.Upgrades[0].Modifiers[1].Target, Is.EqualTo(IncrementalFramework.Economy.ModifierTarget.ResourceCategoryProduction));
                Assert.That(compiled.Upgrades[0].Modifiers[2].Priority, Is.EqualTo(5));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(content);
                UnityEngine.Object.DestroyImmediate(resource);
                UnityEngine.Object.DestroyImmediate(secondResource);
                UnityEngine.Object.DestroyImmediate(producer);
                UnityEngine.Object.DestroyImmediate(upgrade);
            }
        }

        [Test]
        public void AuthoredPrerequisitesRulesClickModifiersAndLifetimeWorkTogether()
        {
            var content = ScriptableObject.CreateInstance<GameContentAsset>();
            var resource = ScriptableObject.CreateInstance<ResourceAsset>();
            var producer = ScriptableObject.CreateInstance<ProducerAsset>();
            var upgrade = ScriptableObject.CreateInstance<UpgradeAsset>();
            var rule = ScriptableObject.CreateInstance<RuleAsset>();
            var flag = ScriptableObject.CreateInstance<FlagAsset>();
            try
            {
                resource.id = "cookies";
                flag.id = "ready";
                producer.id = "cursor";
                producer.costs = new[] { new ResourceAmount { resource = resource, amount = "1" } };
                producer.productionPerSecond = new[] { new ResourceAmount { resource = resource, amount = "1" } };
                producer.prerequisites = new[]
                {
                    new Requirement { kind = RequirementKind.Resource, targetId = "cookies", amount = "2" },
                    new Requirement { kind = RequirementKind.LifetimeResource, targetId = "cookies", amount = "2" }
                };
                producer.rootConditionIndex = 0;
                producer.booleanNodes = new[]
                { new ConditionNode { operation = ConditionOperator.Flag, flagId = "ready" } };
                upgrade.id = "clicks";
                upgrade.costs = new[] { new ResourceAmount { resource = resource, amount = "1" } };
                upgrade.prerequisites = new[]
                { new Requirement { kind = RequirementKind.Producer, targetId = "cursor", amount = "1" } };
                upgrade.rootConditionIndex = 0;
                upgrade.booleanNodes = new[]
                {
                    new ConditionNode { operation = ConditionOperator.Not, children = new[] { 1 } },
                    new ConditionNode { operation = ConditionOperator.Flag, flagId = "ready", expected = false }
                };
                upgrade.modifiers = new[]
                { new ProductionModifier { scope = AuthoredModifierScope.Click, targetId = "cookies",
                    kind = AuthoredModifierKind.Multiply, value = "2" } };
                rule.id = "milestone";
                rule.conditions = new[]
                {
                    new Requirement { kind = RequirementKind.Upgrade, targetId = "clicks", amount = "1" },
                    new Requirement { kind = RequirementKind.Prestige, amount = "0" }
                };
                rule.actions = new[]
                { new RuleActionEntry { kind = RuleActionKind.GrantResource, targetId = "cookies", amount = "5" } };
                content.resources = new[] { resource };
                content.producers = new[] { producer };
                content.upgrades = new[] { upgrade };
                content.rules = new[] { rule };
                content.flags = new[] { flag };
                content.clickResource = resource;
                var simulation = new IncrementalFramework.Progression.Simulation(ContentCompiler.Compile(content).Content);
                Assert.That(simulation.Execute(new IncrementalFramework.Progression.BuyProducerCommand("cursor")).Success, Is.False);
                Assert.That(simulation.Execute(new IncrementalFramework.Progression.ClickCommand("cookies", 1)).Success, Is.True);
                Assert.That(simulation.Execute(new IncrementalFramework.Progression.ClickCommand("cookies", 1)).Success, Is.True);
                Assert.That(simulation.Execute(new IncrementalFramework.Progression.BuyProducerCommand("cursor")).Success, Is.False);
                Assert.That(simulation.Execute(new IncrementalFramework.Progression.SetFlagCommand("ready", true)).Success, Is.True);
                Assert.That(simulation.Execute(new IncrementalFramework.Progression.BuyProducerCommand("cursor")).Success, Is.True);
                Assert.That(simulation.Execute(new IncrementalFramework.Progression.BuyUpgradeCommand("clicks")).Success, Is.True);
                Assert.That(simulation.AdvanceTo(0).Completed, Is.True);
                Assert.That(simulation.Execute(new IncrementalFramework.Progression.ClickCommand("cookies", 1)).Success, Is.True);
                Assert.That(simulation.GetResource("cookies").Balance, Is.EqualTo(new BreakInfinity.BigDouble(7)));
                Assert.That(simulation.GetResource("cookies").LifetimeEarned, Is.EqualTo(new BreakInfinity.BigDouble(9)));
                Assert.That(simulation.ExportState().Rules[0].Fired, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(content);
                UnityEngine.Object.DestroyImmediate(resource);
                UnityEngine.Object.DestroyImmediate(producer);
                UnityEngine.Object.DestroyImmediate(upgrade);
                UnityEngine.Object.DestroyImmediate(rule);
                UnityEngine.Object.DestroyImmediate(flag);
            }
        }

        [Test]
        public void CompilesBooleanFlagsAndRejectsConditionCycles()
        {
            var content = ScriptableObject.CreateInstance<GameContentAsset>();
            var resource = ScriptableObject.CreateInstance<ResourceAsset>();
            var flag = ScriptableObject.CreateInstance<FlagAsset>();
            var rule = ScriptableObject.CreateInstance<RuleAsset>();
            try
            {
                resource.id = "cookies";
                flag.id = "ready";
                flag.initial = true;
                rule.id = "boolean-rule";
                rule.rootConditionIndex = 0;
                rule.booleanNodes = new[]
                {
                    new ConditionNode { operation = ConditionOperator.All, children = new[] { 1, 2 } },
                    new ConditionNode { operation = ConditionOperator.Flag, flagId = "ready" },
                    new ConditionNode { operation = ConditionOperator.Not, children = new[] { 3 } },
                    new ConditionNode { operation = ConditionOperator.Any, children = new[] { 4, 5 } },
                    new ConditionNode { operation = ConditionOperator.Requirement,
                        requirement = new Requirement { kind = RequirementKind.Resource, targetId = "cookies", amount = "1" } },
                    new ConditionNode { operation = ConditionOperator.Flag, flagId = "ready", expected = false }
                };
                content.resources = new[] { resource };
                content.flags = new[] { flag };
                content.rules = new[] { rule };
                content.clickResource = resource;
                var compiled = ContentCompiler.Compile(content).Content;
                Assert.That(compiled.Flags[0].Initial, Is.True);
                var expression = compiled.Rules[0].Condition.Children[1];
                Assert.That(expression.Kind, Is.EqualTo(IncrementalFramework.Economy.ConditionKind.And));
                Assert.That(expression.Children[0].FlagId, Is.EqualTo("ready"));
                Assert.That(expression.Children[1].Kind, Is.EqualTo(IncrementalFramework.Economy.ConditionKind.Not));
                Assert.That(expression.Children[1].Children[0].Kind, Is.EqualTo(IncrementalFramework.Economy.ConditionKind.Or));
                rule.booleanNodes[0].children = new[] { 0 };
                Assert.That(ContentCompiler.Validate(content).Exists(x => x.Contains("cycle")), Is.True);
                Assert.Throws<ContentValidationException>(() => ContentCompiler.Compile(content));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(content);
                UnityEngine.Object.DestroyImmediate(resource);
                UnityEngine.Object.DestroyImmediate(flag);
                UnityEngine.Object.DestroyImmediate(rule);
            }
        }

        [Test]
        public void CompilesAdvancedActionsAndAwardsOnlyUnclaimedLifetimePrestigeCurrency()
        {
            var content = ScriptableObject.CreateInstance<GameContentAsset>();
            var source = ScriptableObject.CreateInstance<ResourceAsset>();
            var reward = ScriptableObject.CreateInstance<ResourceAsset>();
            var flag = ScriptableObject.CreateInstance<FlagAsset>();
            var rule = ScriptableObject.CreateInstance<RuleAsset>();
            try
            {
                source.id = "cookies";
                reward.id = "heaven";
                reward.resetOnPrestige = false;
                flag.id = "ready";
                rule.id = "actions";
                rule.threshold = "10";
                rule.actions = new[]
                {
                    new RuleActionEntry { kind = RuleActionKind.SpendResource, targetId = "cookies", amount = "2" },
                    new RuleActionEntry { kind = RuleActionKind.SetFlag, targetId = "ready", flagValue = true },
                    new RuleActionEntry { kind = RuleActionKind.ScheduleGrantResource, targetId = "cookies",
                        amount = "3", delaySeconds = 5, priority = 7 },
                    new RuleActionEntry { kind = RuleActionKind.SetModifier, modifierId = "click-boost",
                        modifier = new ProductionModifier { scope = AuthoredModifierScope.Click, targetId = "cookies",
                            kind = AuthoredModifierKind.Multiply, value = "2" } }
                };
                content.resources = new[] { source, reward };
                content.flags = new[] { flag };
                content.rules = new[] { rule };
                content.clickResource = source;
                content.prestigeMode = PrestigeMode.LifetimeCurrency;
                content.prestigeSourceResource = source;
                content.prestigeRewardResource = reward;
                var compiled = ContentCompiler.Compile(content).Content;
                Assert.That(compiled.Prestige.UsesLifetimeReward, Is.True);
                Assert.That(compiled.Prestige.RewardResourceId, Is.EqualTo("heaven"));
                Assert.That(compiled.Rules[0].Actions[0], Is.TypeOf<IncrementalFramework.Economy.SpendResourceAction>());
                Assert.That(((IncrementalFramework.Economy.SetFlagAction)compiled.Rules[0].Actions[1]).Value, Is.True);
                var scheduled = (IncrementalFramework.Economy.ScheduleGrantResourceAction)compiled.Rules[0].Actions[2];
                Assert.That(scheduled.Delay, Is.EqualTo(5));
                Assert.That(scheduled.Priority, Is.EqualTo(7));
                Assert.That(((IncrementalFramework.Economy.SetModifierAction)compiled.Rules[0].Actions[3]).ModifierId,
                    Is.EqualTo("click-boost"));
                var simulation = new IncrementalFramework.Progression.Simulation(compiled);
                Assert.That(simulation.Execute(new IncrementalFramework.Progression.GrantResourceCommand("cookies", 100)).Success, Is.True);
                Assert.That(simulation.GetPrestigeReward(), Is.EqualTo(BreakInfinity.BigDouble.One));
                Assert.That(simulation.Execute(new IncrementalFramework.Progression.PrestigeCommand()).Success, Is.True);
                Assert.That(simulation.GetResource("heaven").Balance, Is.EqualTo(BreakInfinity.BigDouble.One));
                Assert.That(simulation.Execute(new IncrementalFramework.Progression.PrestigeCommand()).Success, Is.False);
                Assert.That(simulation.Execute(new IncrementalFramework.Progression.GrantResourceCommand("cookies", 300)).Success, Is.True);
                Assert.That(simulation.GetPrestigeReward(), Is.EqualTo(BreakInfinity.BigDouble.One));
                Assert.That(simulation.Execute(new IncrementalFramework.Progression.PrestigeCommand()).Success, Is.True);
                Assert.That(simulation.GetResource("heaven").Balance, Is.EqualTo(new BreakInfinity.BigDouble(2)));
                Assert.That(simulation.PermanentMultiplier, Is.EqualTo(new BreakInfinity.BigDouble(1.2)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(content);
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(reward);
                UnityEngine.Object.DestroyImmediate(flag);
                UnityEngine.Object.DestroyImmediate(rule);
            }
        }

        [Test]
        public void AuthoredTimeAndManualTriggersStayDistinct()
        {
            var content = ScriptableObject.CreateInstance<GameContentAsset>();
            var resource = ScriptableObject.CreateInstance<ResourceAsset>();
            var timed = ScriptableObject.CreateInstance<RuleAsset>();
            var manual = ScriptableObject.CreateInstance<RuleAsset>();
            try
            {
                resource.id = "cookies";
                timed.id = "timed";
                timed.triggerKind = IncrementalFramework.Economy.RuleTriggerKind.Time;
                timed.triggerTime = 5;
                timed.actions = new[]
                { new RuleActionEntry { kind = RuleActionKind.GrantResource, targetId = "cookies", amount = "1" } };
                manual.id = "manual";
                manual.triggerKind = IncrementalFramework.Economy.RuleTriggerKind.Manual;
                manual.actions = new[]
                { new RuleActionEntry { kind = RuleActionKind.GrantResource, targetId = "cookies", amount = "1" } };
                content.resources = new[] { resource };
                content.rules = new[] { timed, manual };
                content.clickResource = resource;
                var simulation = new IncrementalFramework.Progression.Simulation(ContentCompiler.Compile(content).Content);
                Assert.That(simulation.AdvanceTo(4).Completed, Is.True);
                Assert.That(simulation.GetResource("cookies").Balance, Is.EqualTo(BreakInfinity.BigDouble.Zero));
                Assert.That(simulation.Execute(new IncrementalFramework.Progression.TriggerRuleCommand("manual")).Success, Is.True);
                Assert.That(simulation.AdvanceTo(4).Completed, Is.True);
                Assert.That(simulation.GetResource("cookies").Balance, Is.EqualTo(BreakInfinity.BigDouble.One));
                Assert.That(simulation.AdvanceTo(5).Completed, Is.True);
                Assert.That(simulation.GetResource("cookies").Balance, Is.EqualTo(new BreakInfinity.BigDouble(2)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(content);
                UnityEngine.Object.DestroyImmediate(resource);
                UnityEngine.Object.DestroyImmediate(timed);
                UnityEngine.Object.DestroyImmediate(manual);
            }
        }

        [Test]
        public void NativePrestigePoliciesCompileAndTransformBalancesAndOwnedCounts()
        {
            var content = ScriptableObject.CreateInstance<GameContentAsset>();
            var resource = ScriptableObject.CreateInstance<ResourceAsset>();
            var producer = ScriptableObject.CreateInstance<ProducerAsset>();
            try
            {
                resource.id = "cookies";
                resource.startingAmount = "100";
                resource.usePrestigePolicy = true;
                producer.id = "cursor";
                producer.usePrestigePolicy = true;
                producer.prestigePolicy = IncrementalFramework.Core.ResetPolicy.Transform;
                producer.prestigeTransform = "0.5";
                producer.productionPerSecond = new[] { new ResourceAmount { resource = resource, amount = "1" } };
                content.resources = new[] { resource };
                content.producers = new[] { producer };
                content.clickResource = resource;
                content.prestigeMode = PrestigeMode.BalanceMultiplier;
                content.prestigeSourceResource = resource;
                content.prestigeThreshold = "10";
                foreach (IncrementalFramework.Core.ResetPolicy policy in Enum.GetValues(typeof(IncrementalFramework.Core.ResetPolicy)))
                {
                    resource.prestigePolicy = policy;
                    resource.prestigeTransform = policy == IncrementalFramework.Core.ResetPolicy.Transform ? "0.5" : "1";
                    Assert.That(ContentCompiler.Compile(content).Content.Resources[0].PrestigePolicy, Is.EqualTo(policy));
                }
                resource.prestigePolicy = IncrementalFramework.Core.ResetPolicy.Transform;
                resource.prestigeTransform = "0.5";
                var simulation = new IncrementalFramework.Progression.Simulation(ContentCompiler.Compile(content).Content);
                Assert.That(simulation.Execute(new IncrementalFramework.Progression.BuyProducerCommand("cursor", 4)).Success, Is.True);
                Assert.That(simulation.Execute(new IncrementalFramework.Progression.PrestigeCommand()).Success, Is.True);
                Assert.That(simulation.GetResource("cookies").Balance, Is.EqualTo(new BreakInfinity.BigDouble(50)));
                Assert.That(simulation.GetProducer("cursor").Owned, Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(content);
                UnityEngine.Object.DestroyImmediate(resource);
                UnityEngine.Object.DestroyImmediate(producer);
            }
        }

        [Test]
        public void NegativePoliciesAndResourceChangedScheduledEventFiltersCompileAndExecute()
        {
            var content = ScriptableObject.CreateInstance<GameContentAsset>();
            var resource = ScriptableObject.CreateInstance<ResourceAsset>();
            var producer = ScriptableObject.CreateInstance<ProducerAsset>();
            var changedFlag = ScriptableObject.CreateInstance<FlagAsset>();
            var scheduledFlag = ScriptableObject.CreateInstance<FlagAsset>();
            var changed = ScriptableObject.CreateInstance<RuleAsset>();
            var scheduled = ScriptableObject.CreateInstance<RuleAsset>();
            try
            {
                resource.id = "credit";
                resource.startingAmount = "-3";
                resource.negativePolicy = IncrementalFramework.Economy.NegativeAmountPolicy.ClampToZero;
                producer.id = "cursor";
                producer.costs = new[] { new ResourceAmount { resource = resource, amount = "2" } };
                producer.productionPerSecond = new[] { new ResourceAmount { resource = resource, amount = "1" } };
                changedFlag.id = "changed";
                scheduledFlag.id = "scheduled";
                changed.id = "on-change";
                changed.useResourceThreshold = false;
                changed.triggerKind = IncrementalFramework.Economy.RuleTriggerKind.ResourceChanged;
                changed.triggerTargetId = "credit";
                changed.actions = new[]
                { new RuleActionEntry { kind = RuleActionKind.SetFlag, targetId = "changed" } };
                scheduled.id = "on-scheduled-grant";
                scheduled.useResourceThreshold = false;
                scheduled.triggerKind = IncrementalFramework.Economy.RuleTriggerKind.ScheduledEvent;
                scheduled.filterScheduledEvent = true;
                scheduled.scheduledEventKind = IncrementalFramework.Economy.ScheduledEventTriggerKind.GrantResource;
                scheduled.actions = new[]
                { new RuleActionEntry { kind = RuleActionKind.SetFlag, targetId = "scheduled" } };
                content.resources = new[] { resource };
                content.producers = new[] { producer };
                content.flags = new[] { changedFlag, scheduledFlag };
                content.rules = new[] { changed, scheduled };
                content.clickResource = resource;
                Assert.That(ContentCompiler.Compile(content).Content.Resources[0].Initial, Is.EqualTo(BreakInfinity.BigDouble.Zero));
                resource.negativePolicy = IncrementalFramework.Economy.NegativeAmountPolicy.Reject;
                Assert.Throws<ContentValidationException>(() => ContentCompiler.Compile(content));
                resource.negativePolicy = IncrementalFramework.Economy.NegativeAmountPolicy.Allow;
                var simulation = new IncrementalFramework.Progression.Simulation(ContentCompiler.Compile(content).Content);
                Assert.That(simulation.GetResource("credit").Balance, Is.EqualTo(new BreakInfinity.BigDouble(-3)));
                Assert.That(simulation.Execute(new IncrementalFramework.Progression.BuyProducerCommand("cursor")).Success, Is.True);
                Assert.That(simulation.GetResource("credit").Balance, Is.EqualTo(new BreakInfinity.BigDouble(-5)));
                Assert.That(simulation.AdvanceTo(0).Completed, Is.True);
                Assert.That(simulation.GetFlag("changed"), Is.True);
                simulation.Schedule(new IncrementalFramework.Progression.GrantResourceCommand("credit", 1), 0);
                Assert.That(simulation.AdvanceTo(0).Completed, Is.True);
                Assert.That(simulation.GetFlag("scheduled"), Is.True);
                Assert.That(simulation.GetResource("credit").Balance, Is.EqualTo(new BreakInfinity.BigDouble(-4)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(content);
                UnityEngine.Object.DestroyImmediate(resource);
                UnityEngine.Object.DestroyImmediate(producer);
                UnityEngine.Object.DestroyImmediate(changedFlag);
                UnityEngine.Object.DestroyImmediate(scheduledFlag);
                UnityEngine.Object.DestroyImmediate(changed);
                UnityEngine.Object.DestroyImmediate(scheduled);
            }
        }

        [Test]
        public void ReportsMissingReferencesAndInvalidNumbersTogether()
        {
            var content = ScriptableObject.CreateInstance<GameContentAsset>();
            var resource = ScriptableObject.CreateInstance<ResourceAsset>();
            try
            {
                resource.id = "cookies";
                resource.startingAmount = "1e2e3";
                resource.negativePolicy = (IncrementalFramework.Economy.NegativeAmountPolicy)999;
                content.resources = new[] { resource, resource };
                content.clickAmount = "NaN";
                var errors = ContentCompiler.Validate(content);
                Assert.That(errors.Exists(x => x.Contains("duplicate ID")), Is.True);
                Assert.That(errors.Exists(x => x.Contains("startingAmount")), Is.True);
                Assert.That(errors.Exists(x => x.Contains("clickResource")), Is.True);
                Assert.That(errors.Exists(x => x.Contains("clickAmount")), Is.True);
                Assert.That(errors.Exists(x => x.Contains("negativePolicy")), Is.True);
                Assert.Throws<ContentValidationException>(() => ContentCompiler.Compile(content));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(content);
                UnityEngine.Object.DestroyImmediate(resource);
            }
        }

        [Test]
        public void CompilationDetachesDefinitionsFromAuthoringAssets()
        {
            var content = ScriptableObject.CreateInstance<GameContentAsset>();
            var resource = ScriptableObject.CreateInstance<ResourceAsset>();
            var producer = ScriptableObject.CreateInstance<ProducerAsset>();
            try
            {
                resource.id = "cookies";
                resource.startingAmount = "1E30";
                resource.decimalPlaces = 1;
                producer.id = "cursor";
                producer.costs = new[] { new ResourceAmount { resource = resource, amount = "10" } };
                producer.productionPerSecond = new[] { new ResourceAmount { resource = resource, amount = "1" } };
                content.resources = new[] { resource };
                content.producers = new[] { producer };
                content.clickResource = resource;
                var compiled = ContentCompiler.Compile(content);
                resource.id = "changed";
                resource.decimalPlaces = 6;
                producer.costs[0].amount = "999";
                Assert.That(compiled.Content.Resources[0].Id, Is.EqualTo("cookies"));
                Assert.That(compiled.Content.Producers[0].Costs[0].Amount.ToString(), Is.EqualTo("10"));
                Assert.That(compiled.ClickResourceId, Is.EqualTo("cookies"));
                Assert.That(compiled.FormatResource("cookies", 10), Is.EqualTo("10.0"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(content);
                UnityEngine.Object.DestroyImmediate(resource);
                UnityEngine.Object.DestroyImmediate(producer);
            }
        }
    }
}
