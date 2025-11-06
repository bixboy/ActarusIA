using UnityEngine;

namespace UtilityAI {
    [CreateAssetMenu(menuName = "UtilityAI/Considerations/ConstantConsideration")]
    public class ConstantConsideration : Consideration 
    {
        public float value;
        
        public override float Evaluate(Context context) => value;
    }
}