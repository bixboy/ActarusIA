using UnityEngine;
using UnityUtils;

namespace UtilityAI {
    [CreateAssetMenu(menuName = "UtilityAI/Considerations/InRangeConsideration")]
    public class InRangeConsideration : Consideration {
        public float maxDistance = 10f;
        public float maxAngle = 360f;
        public string targetTag = "Target";
        public AnimationCurve curve;

        public override float Evaluate(Context context) {
            
            return 0;
        }
        
        void Reset() {
            curve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(1f, 0f)
            );
        }
    }
}