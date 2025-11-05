using System.Collections.Generic;
using System.Linq;
using DoNotModify;
using UnityEngine;

namespace Teams.ActarusController.Shahine
{
    public class UtilityAgent
    {
        private Blackboard _bb;
        private List<UtilityAction> _actions = new();

        public UtilityAgent(Blackboard bb)
        {
            _bb = bb;
        }

        public void RegisterAction(UtilityAction action)
        {
            _actions.Add(action);
        }

        public InputData Decide()
        {
            if (_actions.Count == 0)
                return new InputData();

            // Calcul des scores
            var scored = _actions
                .Select(a => new { action = a, score = a.ComputeUtility() })
                .OrderByDescending(a => a.score)
                .ToList();

            var best = scored.First();

            // Debug : visualiser les scores dans la console
            Debug.Log($"Best action: {best.action.GetType().Name} (Score: {best.score:F2})");

            // Exécution de l’action avec le score max
            return best.action.Execute();
        }
    }
}