using System;
using DoNotModify;
using UnityEngine;

namespace Teams.ActarusController.Shahine
{
    public abstract class UtilityAction : MonoBehaviour
    {
        [SerializeField] protected Blackboard _bb;
        [SerializeField] protected Scorer[] _scorers;
        [SerializeField] private bool _availableInCapture = true;
        [SerializeField] private bool _availableInHunt = true;



        public UtilityAction(Blackboard bb)
        {
            _bb = bb;
        }

        protected void ConfigureAvailability(bool availableInCapture, bool availableInHunt)
        {
            _availableInCapture = availableInCapture;
            _availableInHunt = availableInHunt;
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
            if (_scorers == null || _scorers.Length == 0)
                return 0f;

            float total = 0f;
            foreach (var scorer in _scorers)
            {
                total += scorer.ComputeScore(GetInputValue(scorer));
            }
            return total / _scorers.Length; // moyenne
        }

        public bool IsAvailableForMode(Blackboard.CombatMode mode)
        {
            return mode switch
            {
                Blackboard.CombatMode.Capture => _availableInCapture,
                Blackboard.CombatMode.Hunt => _availableInHunt,
                _ => true
            };
        }

        /// <summary>
        /// Méthode à redéfinir pour récupérer la donnée d’entrée propre à l’action.
        /// </summary>
        protected virtual float GetInputValue(Scorer scorer)
        {
            if (_bb == null || _bb.TargetWaypoint == null)
                return 0f;

            switch (scorer.inputType)
            {
                case ScorerInputType.DistanceToWaypointTarget:
                    return Vector2.Distance(_bb.MyShip.Position, _bb.TargetWaypoint.Position);
                
                case ScorerInputType.TargetWaypointOwnership:
                    return _bb.TargetWaypoint.Owner == -1 ? 1f : 0.5f;
                
                case ScorerInputType.ShipSpeed:
                    return _bb.MyShip.Velocity.magnitude;
                
                case ScorerInputType.LastWaypointProximity:
                    float dist = _bb.DistanceToLastTarget;
                    float radius = _bb.TargetWaypoint.Radius;
                    return Mathf.Clamp01(1f - (dist / (radius + 0.2f)));
                
                case ScorerInputType.MyShipEnergyLeft:
                    return _bb.MyShip.Energy;
                
                case ScorerInputType.EnemyEnergyLeft:
                    return _bb.EnemyEnergyLeft;
                
                case ScorerInputType.TimeLeft:
                    return _bb.TimeLeft;
                
                case ScorerInputType.MyScore:
                    return _bb.MyScore;
                
                case ScorerInputType.EnemyScore:
                    return _bb.EnemyScore;
                
                case ScorerInputType.EnemyDistance:
                    return _bb.EnemyDistanceToMyShip;
                 
                case ScorerInputType.EnemyIsInFront:
                    return _bb.IsEnemyInFront ? 1 : 0;
                
                case ScorerInputType.EnemyIsInBack:
                    return _bb.IsEnemyInBack ? 1 : 0;
                
                default:
                    return 0f;
            }
        }
        
        public abstract InputData Execute();
    }
}