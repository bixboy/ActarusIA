using DoNotModify;
using UnityEngine;

namespace Teams.ActarusController.Shahine.UtilityActions
{
    public class SwitchToCaptureModeAction : CombatModeUtilityAction
    {
        public SwitchToCaptureModeAction(Blackboard bb) : base(bb) {}

        public override InputData Execute()
        {
            InputData input = new InputData();

            if (!_bb)
                return input;

            if (_bb.combatMode != Blackboard.CombatMode.Hunt)
                return input;

            _bb.RefreshScoreboard();

            CombatEvaluation evaluation = EvaluateCombatSituation();
            if (!evaluation.HasValidData)
                return input;

            if (ShouldSwitchToCapture(evaluation) && HasWaitedMinimumDuration())
            {
                _bb.SetCombatMode(Blackboard.CombatMode.Capture);
            }

            return input;
        }

        private bool ShouldSwitchToCapture(CombatEvaluation evaluation)
        {
            if (evaluation.LosingLateGame)
            {
                return !evaluation.EnoughEnergyForClutch;
            }

            if (!evaluation.ComfortableLead || !evaluation.EnoughEnergy || !evaluation.EnoughTime)
                return true;

            if (evaluation.EnemyAggressive)
                return true;

            return false;
        }
    }
}
