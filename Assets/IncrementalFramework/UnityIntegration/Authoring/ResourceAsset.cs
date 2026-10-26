using UnityEngine;

namespace IdleHands.UnityIntegration
{
    public enum ResourceNumberFormat { Fixed, Scientific, Engineering, Compact }

    [CreateAssetMenu(menuName = "Idle Hands/Resource")]
    public sealed class ResourceAsset : ScriptableObject
    {
        public string id;
        public string displayName;
        public string category;
        [Tooltip("Invariant decimal or scientific notation; never converted through float.")]
        public string startingAmount = "0";
        public bool hasCap;
        public string cap = "1000";
        public IncrementalFramework.NegativeAmountPolicy negativePolicy;
        public ResourceNumberFormat numberFormat = ResourceNumberFormat.Compact;
        [Range(0, 12)] public int decimalPlaces = 2;
        [Tooltip("Thousands separators for Fixed and Compact. Fixed notation falls back to scientific outside 1e-6..1e15.")]
        public bool useGrouping;
        public bool resetOnPrestige = true;
        [Tooltip("Enable the explicit native reset policy instead of resetOnPrestige.")]
        public bool usePrestigePolicy;
        public IncrementalFramework.ResetPolicy prestigePolicy;
        [Tooltip("Non-negative balance multiplier used only by Transform.")]
        public string prestigeTransform = "1";
    }
}
