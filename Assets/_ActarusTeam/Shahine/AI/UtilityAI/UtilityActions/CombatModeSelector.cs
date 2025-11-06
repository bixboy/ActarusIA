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

        [Header("Adaptation")]
        [SerializeField, Range(0f, 25f)] private float maxHuntDistance = 12f;
        [SerializeField, Range(0f, 1f)] private float enemyAggressionTolerance = 0.65f;
        [SerializeField, Range(0f, 1f)] private float clutchAggressionBoost = 1.35f;

        [Header("Stability")]
        [SerializeField] private float minimumModeDuration = 3f;
        private float _lastModeChangeTime = -999f;

        public CombatModeSelector(Blackboard bb) : base(bb) {}

        private void Awake()
        {
            ConfigureAvailability(true, true);
        }

        protected override float GetInputValue(Scorer scorer)
        {
            if (!_bb)
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

            if (!_bb || _bb.myShip == null || _bb.enemyShip == null)
                return input;

            _bb.RefreshScoreboard();

            bool shouldHunt = ShouldSwitchToHunt();
            Blackboard.CombatMode desiredMode = shouldHunt ? Blackboard.CombatMode.Hunt : Blackboard.CombatMode.Capture;

            if (_bb.combatMode != desiredMode && Time.time - _lastModeChangeTime >= minimumModeDuration)
            {
                _bb.SetCombatMode(desiredMode);
                _lastModeChangeTime = Time.time;
            }

            return input;
        }

        private bool ShouldSwitchToHunt()
        {
            if (!_bb || _bb.myShip == null || _bb.enemyShip == null)
                return false;

            // --- CONDITIONS DE BASE ---
            bool comfortableLead = _bb.scoreLead >= overallLeadForHunt || _bb.waypointLead >= waypointLeadForHunt;
            bool acceptableDeficit = _bb.scoreLead >= -Mathf.Abs(scoreDeficitTolerance);
            bool enoughEnergy = _bb.myShip.Energy >= minimumEnergyForHunt;
            bool enoughTime = _bb.timeLeft > captureFocusTimeThreshold;

            // --- DISTANCE LIMIT ---
            float enemyDistance = Vector2.Distance(_bb.myShip.Position, _bb.enemyShip.Position);
            bool enemyCloseEnough = enemyDistance <= maxHuntDistance;

            bool enemyAggressive = _bb.enemyAggressionIndex > enemyAggressionTolerance;
            
            // --- CLUTCH MODE ---
            bool losingLateGame = _bb.timeLeft < captureFocusTimeThreshold && _bb.scoreLead < 0;
            if (losingLateGame)
            {
                enoughEnergy = _bb.myShip.Energy >= minimumEnergyForHunt * clutchAggressionBoost;
                return enoughEnergy;
            }

            // MODE ACTUEL: HUNT
            if (_bb.combatMode == Blackboard.CombatMode.Hunt)
            {
                if (!comfortableLead || !enoughEnergy || !enoughTime || enemyAggressive)
                    return false;
                
                return true;
            }

            // MODE ACTUEL: CAPTURE
            return comfortableLead && acceptableDeficit && enoughEnergy && enoughTime && enemyCloseEnough && !enemyAggressive;
        }
    }
}
