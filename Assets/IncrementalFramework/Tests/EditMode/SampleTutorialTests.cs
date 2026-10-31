#if UNITY_EDITOR
using System;
using System.Linq;
using IncrementalFramework.Unity;
using NUnit.Framework;
using UnityEngine;

namespace IdleHands.Tests
{
    public sealed class SampleTutorialTests
    {
        [TestCase("OrbitalFoundry")][TestCase("LastlightRailway")][TestCase("MarshlightReserve")]
        [TestCase("ThirteenthBell")][TestCase("TideboundExchange")][TestCase("Floodwall")]
        public void ExpandedPlansTrackEachStepAndRestoreOriginalPresentation(string id)
        {
            var stages=SampleTutorialSession.Expanded(id);using var session=new SampleTutorialSession("test."+Guid.NewGuid(),stages);
            foreach(var stage in stages)
            {
                Assert.That(session.CurrentStage,Is.EqualTo(stage.Key));Assert.That(session.Resolve(stage.Target).Highlighted,Is.True);Assert.That(session.Caption,Does.Contain(stage.Instructions));
                session.RecordAction("invalid-action");Assert.That(session.CurrentStage,Is.EqualTo(stage.Key));session.RecordAction(stage.Actions[0]);
            }
            Assert.That(session.Completed,Is.True);Assert.That(session.Resolve("prestige").Enabled,Is.Null);Assert.That(stages.All(s=>session.Resolve(s.Target).Highlighted==null),Is.True);
        }
        [Test]
        public void SampleSidecarRestoresCurrentStepAndSkipSurvivesReload()
        {
            string identity="test.tutorial."+Guid.NewGuid();string key=identity+".tutorial.v1";
            try
            {
                var stages=SampleTutorialSession.Expanded("OrbitalFoundry");using var session=new SampleTutorialSession(identity,stages);session.RecordAction("priority");session.Save();
                using var restored=new SampleTutorialSession(identity,stages);restored.Load();Assert.That(restored.CurrentStage,Is.EqualTo("recipe"));
                restored.Skip();restored.Save();session.Load();Assert.That(session.Completed,Is.True);Assert.That(session.Resolve("game-action:recipe").Highlighted,Is.Null);
            }
            finally{PlayerPrefs.DeleteKey(key);PlayerPrefs.Save();}
        }
    }
}
#endif
