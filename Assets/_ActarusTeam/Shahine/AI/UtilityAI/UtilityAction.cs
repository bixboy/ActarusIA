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
        protected abstract float GetInputValue(Scorer scorer);
        
        public abstract InputData Execute();
    }
}