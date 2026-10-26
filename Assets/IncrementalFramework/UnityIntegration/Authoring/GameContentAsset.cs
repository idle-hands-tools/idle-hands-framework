using UnityEngine;

namespace IdleHands.UnityIntegration
{
    [CreateAssetMenu(menuName = "Idle Hands/Game Content")]
    public sealed class GameContentAsset : ScriptableObject
    {
        public ResourceAsset[] resources = new ResourceAsset[0];
        public ProducerAsset[] producers = new ProducerAsset[0];
        public UpgradeAsset[] upgrades = new UpgradeAsset[0];
        public RuleAsset[] rules = new RuleAsset[0];
        public ResourceAsset clickResource;
        public string clickAmount = "1";
        public ResourceAsset prestigeResource;
        public string prestigeThreshold = "100";
        public string prestigeProductionBonus = "0.1";
        [Min(0)] public float maximumOfflineSeconds = 28800;

        [ContextMenu("Validate Content")]
        private void ValidateContent()
        {
            var errors = ContentCompiler.Validate(this);
            if (errors.Count == 0) Debug.Log("Content is valid: " + name, this);
            else Debug.LogError("Content validation failed:\n" + string.Join("\n", errors), this);
        }
    }
}
