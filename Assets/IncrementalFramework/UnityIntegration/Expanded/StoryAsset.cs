using System;
using IncrementalFramework.Features;
using IncrementalFramework.Features.Narrative;
using UnityEngine;

namespace IncrementalFramework.Unity.Expanded
{
    [CreateAssetMenu(menuName = "Idle Hands/Narrative/Story", fileName = "Story")]
    public sealed class StoryAsset : ScriptableObject
    {
        [TextArea(5, 20)] public string Data;
        public System.Collections.Generic.List<StoryNodePosition> Positions = new System.Collections.Generic.List<StoryNodePosition>();
        public StoryDefinition Read() => FeatureJson.Read<StoryDefinition>(Data);
        public StoryDefinition Compile() => StoryCompiler.Compile(Read());
    }
    [Serializable]
    public sealed class StoryNodePosition
    {
        public string Id;
        public Vector2 Position;
    }
}
