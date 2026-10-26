using UnityEngine;

namespace IdleHands.UnityIntegration
{
    [CreateAssetMenu(menuName = "Idle Hands/Flag")]
    public sealed class FlagAsset : ScriptableObject
    {
        public string id;
        public bool initial;
        public bool resetOnPrestige = true;
    }
}
