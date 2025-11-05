using System;
using DoNotModify;
using UnityEngine;

namespace Teams.ActarusController.Shahine
{
    public abstract class UtilityAction : MonoBehaviour
    {
        [SerializeField] protected Blackboard _bb;
        [SerializeField] protected Scorer[] _scorers;

        

        public UtilityAction(Blackboard bb)
        {
            _bb = bb;
        }

        public void InitAction(Blackboard bb)
        {
            _bb = bb;
        }
        
        /// <summary>
        /// Calcule la somme pondérée de tous les scorers associés à cette action.
        /// </summary>
        public float ComputeUtility()
        {
            float total = 0f;
            foreach (var scorer in _scorers)
            {
                total += scorer.ComputeScore(GetInputValue(scorer));
            }
            return total / _scorers.Length; // moyenne
        }
        
        /// <summary>
        /// Méthode à redéfinir pour récupérer la donnée d’entrée propre à l’action.
        /// </summary>
        protected virtual float GetInputValue(Scorer scorer)
        {
            if (_bb == null || _bb.targetWaypoint == null)
                return 0f;

            switch (scorer.inputType)
            {
                case ScorerInputType.Distance:
                    return Vector2.Distance(_bb.myShip.Position, _bb.targetWaypoint.Position);

                case ScorerInputType.Speed:
                    return _bb.myShip.Velocity.magnitude;

                case ScorerInputType.Energy:
                    return _bb.myShip.Energy;

                case ScorerInputType.Ownership:
                    return _bb.targetWaypoint.Owner == -1 ? 1f : 0.5f;

                case ScorerInputType.Proximity:
                    float dist = _bb.distanceToTarget;
                    float radius = _bb.targetWaypoint.Radius;
                    return Mathf.Clamp01(1f - (dist / (radius + 0.25f)));

                default:
                    return 0f;
            }
        }
        
        public abstract InputData Execute();
    }
}