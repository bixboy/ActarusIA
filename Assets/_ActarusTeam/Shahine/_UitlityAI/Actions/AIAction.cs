using DoNotModify;
using UnityEngine;

namespace UtilityAI {
    public abstract class AIAction : ScriptableObject {
        public string targetTag;
        public Consideration consideration;

        public virtual void Initialize(Context context) {
            // Optional initialization logic
        }
        
        public float CalculateUtility(Context context) => consideration.Evaluate(context);
        
        public abstract InputData Execute(Context context);

        public abstract void DrawActionGizmos(Context context);

    }
}