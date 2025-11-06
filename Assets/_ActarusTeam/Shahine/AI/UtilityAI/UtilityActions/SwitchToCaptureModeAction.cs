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
            

            var ev = EvaluateCombatSituation();
            if (!ev.HasValidData)
                return input;

            if (ShouldSwitchToCapture(ev) && HasWaitedMinimumDuration())
                _bb.SetCombatMode(Blackboard.CombatMode.Capture);

            return input;
        }

        private bool ShouldSwitchToCapture(CombatEvaluation ev)
        {
            if (ev.LosingLateGame)
                return !ev.EnoughEnergyForClutch;

            if (!ev.ComfortableLead || !ev.EnoughEnergy || !ev.EnoughTime)
                return true;

            if (ev.EnemyAggressive)
                return true;

            if (_bb.waypointLead < waypointLeadForHunt)
                return true;

            return false;
        }
    }
}