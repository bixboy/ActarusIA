using DoNotModify;
using UnityEngine;

namespace Teams.ActarusController.Shahine.UtilityActions
{
    public class SwitchToHuntModeAction : CombatModeUtilityAction
    {
        public SwitchToHuntModeAction(Blackboard bb) : base(bb) {}

        public override InputData Execute()
        {
            InputData input = new InputData();

            if (!_bb)
                return input;

            _bb.RefreshScoreboard();

            if (_bb.combatMode == Blackboard.CombatMode.Hunt)
                return input;

            CombatEvaluation evaluation = EvaluateCombatSituation();
            if (!evaluation.HasValidData)
                return input;

            if (ShouldSwitchToHunt(evaluation) && HasWaitedMinimumDuration())
            {
                _bb.SetCombatMode(Blackboard.CombatMode.Hunt);
            }

            return input;
        }

        private bool ShouldSwitchToHunt(CombatEvaluation evaluation)
        {
            if (evaluation.LosingLateGame)
            {
                return evaluation.EnoughEnergyForClutch;
            }

            return evaluation.ComfortableLead
                   && evaluation.AcceptableDeficit
                   && evaluation.EnoughEnergy
                   && evaluation.EnoughTime
                   && evaluation.EnemyCloseEnough
                   && !evaluation.EnemyAggressive;
        }
    }
}
