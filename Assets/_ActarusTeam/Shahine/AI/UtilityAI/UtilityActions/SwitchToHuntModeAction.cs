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
            

            if (_bb.combatMode == Blackboard.CombatMode.Hunt)
                return input;

            var ev = EvaluateCombatSituation();
            if (!ev.HasValidData)
                return input;

            if (ShouldSwitchToHunt(ev) && HasWaitedMinimumDuration())
                _bb.SetCombatMode(Blackboard.CombatMode.Hunt);

            return input;
        }

        private bool ShouldSwitchToHunt(CombatEvaluation ev)
        {
            if (ev.EnemyWeak)
                return true;

            if (_bb.scoreLead < overallLeadForHunt + hysteresisMargin && _bb.waypointLead < waypointLeadForHunt + hysteresisMargin)
                return false;

            if (ev.LosingLateGame)
                return ev.EnoughEnergyForClutch;

            return ev.ComfortableLead
                   && ev.AcceptableDeficit
                   && ev.EnoughEnergy
                   && ev.EnoughTime
                   && ev.EnemyCloseEnough
                   && !ev.EnemyAggressive
                   && !ev.EnemyRunningAway;
        }
    }
}