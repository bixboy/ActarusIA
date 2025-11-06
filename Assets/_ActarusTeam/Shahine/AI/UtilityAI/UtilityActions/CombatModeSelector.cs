using DoNotModify;
using UnityEngine;

namespace Teams.ActarusController.Shahine.UtilityActions
{
    public class CombatModeSelector : UtilityAction
    {
        [Header("Score Thresholds")]
        [SerializeField] private int overallLeadForHunt = 2;
        [SerializeField] private int waypointLeadForHunt = 1;
        [SerializeField] private int scoreDeficitTolerance = 0;

        [Header("Resource Requirements")]
        [SerializeField, Range(0f, 1f)] private float minimumEnergyForHunt = 0.5f;
        [SerializeField] private float captureFocusTimeThreshold = 12f;

        [Header("Stability")]
        [SerializeField] private float minimumModeDuration = 3f;

        private float _lastModeChangeTime = -999f;

        public CombatModeSelector(Blackboard bb) : base(bb)
        {
        }

        private void Awake()
        {
            ConfigureAvailability(true, true);
        }

        protected override float GetInputValue(Scorer scorer)
        {
            if (_bb == null)
                return 0f;

            switch (scorer.inputType)
            {
                case ScorerInputType.Ownership:
                    return Mathf.Max(0, _bb.waypointLead);
                case ScorerInputType.Energy:
                    return _bb.myShip != null ? _bb.myShip.Energy : 0f;
                case ScorerInputType.Speed:
                    return _bb.enemyShip != null ? _bb.enemyShip.Velocity.magnitude : 0f;
                default:
                    return 0f;
            }
        }

        public override InputData Execute()
        {
            InputData input = new InputData();

            if (_bb == null || _bb.myShip == null || _bb.enemyShip == null)
                return input;

            _bb.RefreshScoreboard();

            bool shouldHunt = ShouldSwitchToHunt();
            Blackboard.CombatMode desiredMode = shouldHunt ? Blackboard.CombatMode.Hunt : Blackboard.CombatMode.Capture;

            if (_bb.combatMode != desiredMode)
            {
                if (Time.time - _lastModeChangeTime >= minimumModeDuration)
                {
                    _bb.SetCombatMode(desiredMode);
                    _lastModeChangeTime = Time.time;
                }
            }

            return input;
        }

        private bool ShouldSwitchToHunt()
        {
            if (_bb == null || _bb.myShip == null || _bb.enemyShip == null)
                return false;

            bool comfortableLead = _bb.scoreLead >= overallLeadForHunt || _bb.waypointLead >= waypointLeadForHunt;
            bool acceptableDeficit = _bb.scoreLead >= -Mathf.Abs(scoreDeficitTolerance);
            bool enoughEnergy = _bb.myShip.Energy >= minimumEnergyForHunt;
            bool enoughTime = _bb.timeLeft > captureFocusTimeThreshold;

            if (_bb.combatMode == Blackboard.CombatMode.Hunt)
            {
                if (!comfortableLead || !enoughEnergy || !enoughTime)
                    return false;
                return true;
            }

            return comfortableLead && enoughEnergy && enoughTime && acceptableDeficit;
        }
    }
}
