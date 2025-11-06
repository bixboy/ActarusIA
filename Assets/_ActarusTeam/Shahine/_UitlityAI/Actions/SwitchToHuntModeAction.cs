using DoNotModify;
using Teams.ActarusController.Shahine;
using UnityEngine;
using UtilityAI;

namespace Teams.ActarusController.Shahine.UtilityActions
{
    /// <summary>
    /// Base class for combat mode switching actions driven by the Utility AI framework.
    /// Provides a shared evaluation and execution flow so derived actions only need to
    /// express their specific utility curves.
    /// </summary>
    public abstract class CombatModeUtilityAction : AIAction
    {
        [Header("Utility Settings")]
        [Tooltip("Minimum time (in seconds) that must elapse between two combat mode switches.")]
        [SerializeField] private float minSwitchInterval = 4f;

        protected float MinSwitchInterval => minSwitchInterval;

        protected override float EvaluateUtility(Context context)
        {
            float modeUtility = EvaluateModeUtility(context);
            if (modeUtility <= 0f)
                return 0f;

            float readiness = ComputeSwitchReadiness(context);
            return Mathf.Clamp01(modeUtility * readiness);
        }

        public override InputData Execute(Context context)
        {
            ApplyMode(context);
            return new InputData();
        }

        /// <summary>
        /// Called by <see cref="ActarusControllerUtilityAI"/> to apply the new combat mode when this
        /// action has the highest utility score.
        /// </summary>
        public void ApplyMode(Context context)
        {
            OnApplyMode(context);
        }

        protected abstract float EvaluateModeUtility(Context context);

        protected abstract void OnApplyMode(Context context);

        private float ComputeSwitchReadiness(Context context)
        {
            if (context == null)
                return 0f;

            float timeSinceSwitch = context.GetData<float>("timeSinceLastCombatModeSwitch");
            if (minSwitchInterval <= 0f)
                return 1f;

            return Mathf.Clamp01(timeSinceSwitch / minSwitchInterval);
        }
    }

    /// <summary>
    /// Utility action that favours switching the controller to Hunt mode when we have a comfortable
    /// lead and the tactical situation mirrors the legacy <c>ComfortableLead</c>, <c>EnemyWeak</c>
    /// and related boolean checks.
    /// </summary>
    [CreateAssetMenu(menuName = "UtilityAI/Combat/ Switch To Hunt Mode")]
    public sealed class SwitchToHuntModeAction : CombatModeUtilityAction
    {
        [Header("Scoring Curves")]
        [SerializeField, Tooltip("Distance (in world units) that we still consider close enough to keep pressure on the enemy.")]
        private float idealHuntDistance = 6f;

        protected override float EvaluateModeUtility(Context context)
        {
            if (context == null)
                return 0f;

            // --- Old condition mapping ---
            // ComfortableLead -> derived from scoreLead & waypointLead margins
            float comfortableLead = EvaluateComfortableLead(context);

            // EnoughEnergy -> based on our remaining energy
            float energy = Mathf.Clamp01(context.GetData<float>("myEnergyNormalized"));

            // Plenty of time left in the round
            float timeLeft = Mathf.Clamp01(context.GetData<float>("timeLeftNormalized"));

            // EnemyNotAggressive -> inverse of enemy aggression index
            float enemyAggression = Mathf.Clamp01(context.GetData<float>("enemyAggressionIndex"));
            float calmEnemy = 1f - enemyAggression;

            // EnemyClose -> proximity bonus when the enemy is within ideal hunting range
            float enemyDistance = context.GetData<float>("enemyDistance");
            float proximity = Mathf.Clamp01(1f - (enemyDistance / Mathf.Max(idealHuntDistance, 0.01f)));

            // EnemyNotRunningAway -> convert direction alignment into a 0..1 readiness score
            float enemyRunningAway = Mathf.Clamp01(context.GetData<float>("enemyRunningAway"));
            float enemyHoldingGround = 1f - enemyRunningAway;

            // EnemyWeak -> directly mapped from the blackboard heuristic
            float enemyWeak = Mathf.Clamp01(context.GetData<float>("enemyWeak"));

            const float comfortableLeadWeight = 0.25f;
            const float energyWeight = 0.2f;
            const float timeWeight = 0.15f;
            const float calmEnemyWeight = 0.15f;
            const float proximityWeight = 0.1f;
            const float holdingGroundWeight = 0.1f;
            const float enemyWeakWeight = 0.05f;

            float weightedScore =
                comfortableLead * comfortableLeadWeight +
                energy * energyWeight +
                timeLeft * timeWeight +
                calmEnemy * calmEnemyWeight +
                proximity * proximityWeight +
                enemyHoldingGround * holdingGroundWeight +
                enemyWeak * enemyWeakWeight;

            return Mathf.Clamp01(weightedScore);
        }

        protected override void OnApplyMode(Context context)
        {
            if (context?.ControllerUtilityAI == null)
                return;

            context.ControllerUtilityAI.SetCombatMode(ActarusControllerUtilityAI.CombatMode.Hunt);
        }

        private static float EvaluateComfortableLead(Context context)
        {
            int scoreLead = context.GetData<int>("scoreLead");
            int waypointLead = context.GetData<int>("waypointLead");

            float scoreComponent = Mathf.InverseLerp(-1f, 4f, scoreLead);
            float waypointComponent = Mathf.InverseLerp(-1f, 3f, waypointLead);

            return Mathf.Clamp01(0.6f * scoreComponent + 0.4f * waypointComponent);
        }
    }
}

