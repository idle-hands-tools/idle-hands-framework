using UnityEngine;

namespace IdleHands.UnityIntegration
{
    [CreateAssetMenu(menuName = "Idle Hands/Resource")]
    public sealed class ResourceAsset : ScriptableObject
    {
        public string id;
        public string displayName;
        [Tooltip("Invariant decimal or scientific notation; never converted through float.")]
        public string startingAmount = "0";
        public bool resetOnPrestige = true;
    }
}
