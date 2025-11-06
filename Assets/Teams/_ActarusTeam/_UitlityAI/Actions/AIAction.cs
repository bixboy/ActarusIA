using DoNotModify;
using UnityEngine;

namespace Teams.Actarus {
    public abstract class AIAction : ScriptableObject {

        public Consideration consideration;

        public virtual void Initialize(Context context) {
            // Optional initialization logic
        }
        
        public virtual float CalculateUtility(Context context)
        {
            if (consideration == null)
                return 0f;

            return consideration.Evaluate(context);
        }
        
        public abstract InputData Execute(Context context);

        public abstract void DrawActionGizmos(Context context);

    }
}