using Teams.ActarusController.Shahine;
using UnityEngine;
using UtilityAI;

namespace Teams.ActarusController.Shahine.UtilityActions
{
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

            // === Données contextuelles ===
            float comfortableLead       = EvaluateComfortableLead(context, -2f, 2f, -2f, 2f);
            float energy                = Mathf.Clamp01(context.GetData<float>("myEnergyNormalized"));
            float timeLeft              = Mathf.Clamp01(context.GetData<float>("timeLeftNormalized"));
            float enemyAggression       = Mathf.Clamp01(context.GetData<float>("enemyAggressionIndex"));
            float enemyRunningAway      = Mathf.Clamp01(context.GetData<float>("enemyRunningAway"));
            float enemyDistance         = context.GetData<float>("enemyDistance");
            float enemyWeak             = Mathf.Clamp01(context.GetData<float>("enemyWeak"));

            // === Transformations ===
            float losingGround          = 1f - comfortableLead;
            float lowEnergy             = 1f - energy;
            float lateGame              = 1f - timeLeft;
            float enemyFar              = Mathf.Clamp01(enemyDistance / Mathf.Max(safeCaptureDistance, 0.01f));
            float enemyStrong           = 1f - enemyWeak;

            // === Pondérations ===
            const float losingGroundWeight     = 0.30f;
            const float lowEnergyWeight        = 0.20f;
            const float lateGameWeight         = 0.15f;
            const float enemyAggressionWeight  = 0.15f;
            const float enemyFarWeight         = 0.10f;
            const float runningAwayWeight      = 0.05f;
            const float enemyStrongWeight      = 0.05f;

            // === Calcul du score pondéré ===
            float weightedScore =
                losingGround        * losingGroundWeight +
                lowEnergy           * lowEnergyWeight +
                lateGame            * lateGameWeight +
                enemyAggression     * enemyAggressionWeight +
                enemyFar            * enemyFarWeight +
                enemyRunningAway    * runningAwayWeight +
                enemyStrong         * enemyStrongWeight;

            return Mathf.Clamp01(weightedScore);
        }

        protected override void OnApplyMode(Context context)
        {
            if (!context?.ControllerUtilityAI)
                return;

            context.ControllerUtilityAI.SetCombatMode(ActarusControllerUtilityAI.CombatMode.Capture);
        }

        public override void DrawActionGizmos(Context context)
        {
            // TODO: Optionally implement debug gizmos for capture mode decision
        }
    }
}