using Teams.ActarusController.Shahine;
using UnityEngine;
using UtilityAI;

namespace Teams.ActarusController.Shahine.UtilityActions
{
    /// <summary>
    /// Utility action that favours switching the controller to Capture mode when the legacy
    /// boolean checks such as <c>LosingLateGame</c>, <c>EnemyAggressive</c> or <c>LowEnergy</c>
    /// would previously have triggered.
    /// </summary>
    [CreateAssetMenu(menuName = "UtilityAI/Combat/ Switch To Capture Mode")]
    public sealed class SwitchToCaptureModeAction : CombatModeUtilityAction
    {
        [Header("Scoring Curves")]
        [SerializeField, Tooltip("Enemy distance (in world units) that we consider safe enough to focus on objectives.")]
        private float safeCaptureDistance = 8f;

        protected override float EvaluateModeUtility(Context context)
        {
            if (context == null)
                return 0f;

            float comfortableLead = EvaluateComfortableLead(context);
            float losingGround = 1f - comfortableLead; // Mirrors !ComfortableLead

            float energy = Mathf.Clamp01(context.GetData<float>("myEnergyNormalized"));
            float lowEnergy = 1f - energy; // Mirrors LowEnergy

            float timeLeft = Mathf.Clamp01(context.GetData<float>("timeLeftNormalized"));
            float lateGame = 1f - timeLeft; // Mirrors LosingLateGame

            float enemyAggression = Mathf.Clamp01(context.GetData<float>("enemyAggressionIndex")); // EnemyAggressive

            float enemyRunningAway = Mathf.Clamp01(context.GetData<float>("enemyRunningAway"));

            float enemyDistance = context.GetData<float>("enemyDistance");
            float enemyFar = Mathf.Clamp01(enemyDistance / Mathf.Max(safeCaptureDistance, 0.01f));

            // If the enemy is weak we tend to stay aggressive, so invert the score here.
            float enemyStrong = 1f - Mathf.Clamp01(context.GetData<float>("enemyWeak"));

            const float losingGroundWeight = 0.3f;
            const float lowEnergyWeight = 0.2f;
            const float lateGameWeight = 0.15f;
            const float enemyAggressionWeight = 0.15f;
            const float enemyFarWeight = 0.1f;
            const float runningAwayWeight = 0.05f;
            const float enemyStrongWeight = 0.05f;

            float weightedScore =
                losingGround * losingGroundWeight +
                lowEnergy * lowEnergyWeight +
                lateGame * lateGameWeight +
                enemyAggression * enemyAggressionWeight +
                enemyFar * enemyFarWeight +
                enemyRunningAway * runningAwayWeight +
                enemyStrong * enemyStrongWeight;

            return Mathf.Clamp01(weightedScore);
        }

        protected override void OnApplyMode(Context context)
        {
            if (context?.ControllerUtilityAI == null)
                return;

            context.ControllerUtilityAI.SetCombatMode(ActarusControllerUtilityAI.CombatMode.Capture);
        }

        private static float EvaluateComfortableLead(Context context)
        {
            int scoreLead = context.GetData<int>("scoreLead");
            int waypointLead = context.GetData<int>("waypointLead");

            float scoreComponent = Mathf.InverseLerp(-2f, 2f, scoreLead);
            float waypointComponent = Mathf.InverseLerp(-2f, 2f, waypointLead);

            return Mathf.Clamp01(0.6f * scoreComponent + 0.4f * waypointComponent);
        }
    }
}

