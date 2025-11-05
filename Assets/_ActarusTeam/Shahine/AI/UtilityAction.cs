using DoNotModify;
using UnityEngine;

namespace Teams.ActarusController.Shahine
{
    public abstract class UtilityAction : MonoBehaviour
    {
        [SerializeField] protected Blackboard _bb;

        public UtilityAction(Blackboard bb)
        {
            _bb = bb;
        }

        public void InitAction(Blackboard bb)
        {
            _bb = bb;
        }
        
        public abstract float ComputeUtility();
        
        public abstract InputData Execute();
    }
}