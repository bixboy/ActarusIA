using DoNotModify;
using UnityEngine;

namespace UtilityAI {
    public abstract class AIAction : ScriptableObject {
        public string targetTag;
        public Consideration consideration;

        public virtual void Initialize(Context context) {
            // Optional initialization logic
        }

        public virtual float CalculateUtility(Context context) => EvaluateUtility(context);

        protected virtual float EvaluateUtility(Context context) => consideration ? consideration.Evaluate(context) : 0f;

        public abstract InputData Execute(Context context);

        public abstract void DrawActionGizmos(Context context);

    }
}