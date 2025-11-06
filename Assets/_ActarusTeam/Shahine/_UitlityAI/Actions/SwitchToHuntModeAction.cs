using DoNotModify;
using Teams.ActarusController.Shahine;
using UnityEngine;
using UtilityAI;

namespace Teams.ActarusController.Shahine.UtilityActions
{
    
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
            
            // Scores stratégiques
            float comfortableLead = EvaluateComfortableLead(context, -1f, 4f, -1f, 3f);
            float timeLeft = Mathf.Clamp01(context.GetData<float>("timeLeftNormalized"));
            float energy = Mathf.Clamp01(context.GetData<float>("myEnergyNormalized"));

            // État ennemi
            float enemyAggression = Mathf.Clamp01(context.GetData<float>("enemyAggressionIndex"));
            float calmEnemy = 1f - enemyAggression;
            float enemyWeak = Mathf.Clamp01(context.GetData<float>("enemyWeak"));
            float enemyRunningAway = Mathf.Clamp01(context.GetData<float>("enemyRunningAway"));
            float enemyHoldingGround = 1f - enemyRunningAway;

            // Proximité tactique
            float enemyDistance = context.GetData<float>("enemyDistance");
            float proximity = Mathf.Clamp01(1f - (enemyDistance / Mathf.Max(idealHuntDistance, 0.01f)));


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
            if (!context?.ControllerUtilityAI)
                return;
            
            Debug.Log("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz");

            context.ControllerUtilityAI.SetCombatMode(ActarusControllerUtilityAI.CombatMode.Hunt);
        }

        public override void DrawActionGizmos(Context context)
        {
            if (context?.ControllerUtilityAI == null) 
                return;

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(context.ControllerUtilityAI.transform.position, idealHuntDistance);
        }
    }
}

