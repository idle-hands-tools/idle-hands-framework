#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using IncrementalFramework.Features;
using IncrementalFramework.Features.Narrative;
using IncrementalFramework.Samples.Expanded;
using IncrementalFramework.Unity.Expanded;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace IdleHands.Tests
{
    public sealed class StoryGraphEditorTests
    {
        private EditorWindow editor;
        private StoryAsset asset;
        private Type type;
        private string clipboard;
        private object Call(string name,params object[] args) => type.GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(editor,args);
        [SetUp] public void Setup()
        {
            clipboard=EditorGUIUtility.systemCopyBuffer;
            type=Type.GetType("IncrementalFramework.Editor.Narrative.StoryGraphEditor, Assembly-CSharp-Editor",true);
            editor=ScriptableObject.CreateInstance(type) as EditorWindow;
            type.GetMethod("CreateGUI").Invoke(editor,null);
            asset=ScriptableObject.CreateInstance<StoryAsset>();asset.Data=FeatureJson.Write(ExpandedCampaigns.Floodwall());
            Call("Select",asset);
        }
        [TearDown] public void Cleanup()
        {
            if(editor!=null)UnityEngine.Object.DestroyImmediate(editor);if(asset!=null)UnityEngine.Object.DestroyImmediate(asset);
            EditorGUIUtility.systemCopyBuffer=clipboard;
        }
        [Test] public void DuplicateAssignsFreshNodeAndChoiceIdsAndUndoRestoresTheGraph()
        {
            var field=type.GetField("selected",BindingFlags.Instance|BindingFlags.NonPublic);field.SetValue(editor,"first-warning");
            int count=asset.Read().Nodes.Count;Call("CopyNode");Undo.IncrementCurrentGroup();Call("PasteNode");Undo.FlushUndoRecordObjects();
            var copy=asset.Read();Assert.That(copy.Nodes.Count,Is.EqualTo(count+1));
            Assert.That(copy.Nodes.Select(n=>n.Id).Distinct().Count(),Is.EqualTo(copy.Nodes.Count));
            var choices=copy.Nodes.SelectMany(n=>n.Choices).ToArray();Assert.That(choices.Select(c=>c.Id).Distinct().Count(),Is.EqualTo(choices.Length));
            Undo.PerformUndo();Assert.That(asset.Read().Nodes.Count,Is.EqualTo(count));
        }
        [Test] public void PlaytestAndVariableEditsDoNotChangeAuthoredDefaults()
        {
            string before=asset.Data;Call("StartPlaytest");
            var game=(FeatureGame)type.GetField("playtest",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(editor);
            var module=(NarrativeModule)game.Module;
            Assert.That(game.Execute("continue",argument:module.Token).Success,Is.True);
            Assert.That(asset.Data,Is.EqualTo(before));
            Assert.That(module.State.Run.Current,Is.EqualTo("ledger"));
        }
        [Test] public void PositionsRemainOutsideCompiledStoryData()
        {
            string original=FeatureJson.Write(asset.Compile());asset.Positions.Add(new StoryNodePosition { Id="appointment", Position=new Vector2(124,567) });
            Assert.That(FeatureJson.Write(asset.Compile()),Is.EqualTo(original));
        }
    }
}
#endif
