using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using IncrementalFramework.Persistence;
using UnityEngine;

namespace IncrementalFramework.Unity
{
    [CreateAssetMenu(menuName = "Idle Hands/Flag")]
    public sealed class FlagAsset : ScriptableObject
    {
        public string id;
        public bool initial;
        public bool resetOnPrestige = true;
    }
}
