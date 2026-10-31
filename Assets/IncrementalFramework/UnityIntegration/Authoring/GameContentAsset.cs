using IncrementalFramework.Core;
using IncrementalFramework.Economy;
using IncrementalFramework.Progression;
using IncrementalFramework.Persistence;
using UnityEngine;

namespace IncrementalFramework.Unity
{
    public enum PrestigeMode { BalanceMultiplier, LifetimeCurrency }

    [CreateAssetMenu(menuName = "Idle Hands/Game Content")]
    public sealed class GameContentAsset : ScriptableObject
    {
        [Header("Presentation")]
        public string title = "IDLE HANDS / COOKIE BAKERY";
        [TextArea] public string introduction = "Bake cookies, hire cursors, unlock ovens, then prestige. Scroll for more.";
        public string clickLabel = "Bake";
        public string producersLabel = "PRODUCERS";
        public string upgradesLabel = "UPGRADES";
        public string rulesLabel = "MILESTONES";
        public Color accentColor = new Color(0.19f, 0.28f, 0.43f);
        public ResourceAsset[] resources = new ResourceAsset[0];
        public ProducerAsset[] producers = new ProducerAsset[0];
        public UpgradeAsset[] upgrades = new UpgradeAsset[0];
        public RuleAsset[] rules = new RuleAsset[0];
        public FlagAsset[] flags = new FlagAsset[0];
        public ResourceAsset clickResource;
        public string clickAmount = "1";
        public PrestigeMode prestigeMode = PrestigeMode.LifetimeCurrency;
        [Tooltip("Eligibility source: current balance in BalanceMultiplier mode, lifetime earnings in LifetimeCurrency mode.")]
        [UnityEngine.Serialization.FormerlySerializedAs("prestigeResource")]
        public ResourceAsset prestigeSourceResource;
        [Tooltip("Separate persistent currency awarded in LifetimeCurrency mode.")]
        public ResourceAsset prestigeRewardResource;
        public string prestigeThreshold = "100";
        public double prestigeRewardExponent = 0.5;
        public string prestigeRewardMultiplier = "1";
        [Tooltip("Per-earned-currency production bonus in lifetime mode; per-reset bonus in legacy balance mode.")]
        public string prestigeProductionBonus = "0.1";
        public bool prestigeResetProducers = true;
        public bool prestigeResetUpgrades = true;
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
