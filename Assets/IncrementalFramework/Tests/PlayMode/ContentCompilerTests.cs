using System;
using IdleHands.UnityIntegration;
using NUnit.Framework;
using UnityEngine;

namespace IdleHands.Tests
{
    public sealed class ContentCompilerTests
    {
        [Test]
        public void ReportsMissingReferencesAndInvalidNumbersTogether()
        {
            var content = ScriptableObject.CreateInstance<GameContentAsset>();
            var resource = ScriptableObject.CreateInstance<ResourceAsset>();
            try
            {
                resource.id = "cookies";
                resource.startingAmount = "1e2e3";
                content.resources = new[] { resource, resource };
                content.clickAmount = "NaN";
                var errors = ContentCompiler.Validate(content);
                Assert.That(errors.Exists(x => x.Contains("duplicate ID")), Is.True);
                Assert.That(errors.Exists(x => x.Contains("startingAmount")), Is.True);
                Assert.That(errors.Exists(x => x.Contains("clickResource")), Is.True);
                Assert.That(errors.Exists(x => x.Contains("clickAmount")), Is.True);
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
                producer.id = "cursor";
                producer.costs = new[] { new ResourceAmount { resource = resource, amount = "10" } };
                producer.productionPerSecond = new[] { new ResourceAmount { resource = resource, amount = "1" } };
                content.resources = new[] { resource };
                content.producers = new[] { producer };
                content.clickResource = resource;
                var compiled = ContentCompiler.Compile(content);
                resource.id = "changed";
                producer.costs[0].amount = "999";
                Assert.That(compiled.Content.Resources[0].Id, Is.EqualTo("cookies"));
                Assert.That(compiled.Content.Producers[0].Costs[0].Amount.ToString(), Is.EqualTo("10"));
                Assert.That(compiled.ClickResourceId, Is.EqualTo("cookies"));
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
