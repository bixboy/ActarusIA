using DoNotModify;
using UnityEngine;
using UtilityAI;

namespace Teams.ActarusController.Shahine.UtilityActions
{
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
        
        protected float EvaluateComfortableLead(Context context, float scoreMin, float scoreMax, float wpMin, float wpMax)
        {
            int scoreLead = context.GetData<int>("scoreLead");
            int waypointLead = context.GetData<int>("waypointLead");

            float scoreComponent = Mathf.InverseLerp(scoreMin, scoreMax, scoreLead);
            float waypointComponent = Mathf.InverseLerp(wpMin, wpMax, waypointLead);

            return Mathf.Clamp01(0.6f * scoreComponent + 0.4f * waypointComponent);
        }

        public override InputData Execute(Context context)
        {
            ApplyMode(context);
            return new InputData();
        }
        
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
}
