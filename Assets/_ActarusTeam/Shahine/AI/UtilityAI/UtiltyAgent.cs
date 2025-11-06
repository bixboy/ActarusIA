using System.Collections.Generic;
using System.Linq;
using DoNotModify;
using Teams.ActarusController.Shahine.UtilityActions;
using UnityEngine;

namespace Teams.ActarusController.Shahine
{
    public class UtilityAgent : MonoBehaviour
    {
        [SerializeField] private Blackboard _bb;
        [SerializeField] private List<UtilityAction> _actions = new List<UtilityAction>();
        private List<CombatModeUtilityAction> _modeActions = new List<CombatModeUtilityAction>();

        public UtilityAgent(Blackboard bb)
        {
            _bb = bb;
        }

        public void Init(Blackboard bb)
        {
            _bb = bb;

            var discovered = GetComponents<UtilityAction>()
                .Where(action => action != null)
                .ToList();

            foreach (UtilityAction action in discovered)
            {
                action.InitAction(_bb);
            }

            _modeActions = discovered
                .OfType<CombatModeUtilityAction>()
                .ToList();

            _actions = discovered
                .Where(action => action is not CombatModeUtilityAction)
                .ToList();
        }

        public void RegisterAction(UtilityAction action)
        {
            _actions.Add(action);
        }

        public InputData Decide()
        {
            if (_modeActions != null)
            {
                foreach (CombatModeUtilityAction modeAction in _modeActions)
                {
                    modeAction?.Execute();
                }
            }

            if (_actions == null || _actions.Count == 0)
                return new InputData();

            var currentMode = _bb != null ? _bb.combatMode : Blackboard.CombatMode.Capture;

            var actionable = _actions
                .Where(a => a != null && a.IsAvailableForMode(currentMode))
                .ToList();

            if (actionable.Count == 0)
                return new InputData();

            // Calcul des scores
            var scored = actionable
                .Select(a => new { action = a, score = a.ComputeUtility() })
                .OrderByDescending(a => a.score)
                .ToList();

            var best = scored.First();

            // Debug : visualiser les scores dans la console
            // Debug.Log($"Best action: {best.action.GetType().Name} (Score: {best.score:F2})");

            // Exécution de l’action avec le score max
            return best.action.Execute();
        }
    }
}