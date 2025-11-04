using DoNotModify;

namespace Teams.ActarusController.Shahine
{
    public abstract class UtilityAction
    {
        protected Blackboard _bb;

        public UtilityAction(Blackboard bb)
        {
            _bb = bb;
        }
        
        public abstract float ComputeUtility();
        
        public abstract InputData Execute();
    }
}