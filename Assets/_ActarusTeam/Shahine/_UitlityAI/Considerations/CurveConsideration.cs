using UnityEngine;

namespace UtilityAI {
    [CreateAssetMenu(menuName = "UtilityAI/Considerations/CurveConsideration")]
    public class CurveConsideration : Consideration 
    {
        public AnimationCurve curve;
        
        public string contextKey;
        
        public float inputMin;
        public float inputMax;
        
        public float scoreMin;
        public float scoreMax;
        

        public override float Evaluate(Context context) {
            
            float inputValue = Mathf.Clamp(context.GetData<float>(contextKey), inputMin, inputMax);
            
            float utility = curve.Evaluate(inputValue/inputMax);
            
            return Mathf.InverseLerp(scoreMin, scoreMax, utility);
        }

        void Reset() {
            curve = new AnimationCurve(
                new Keyframe(0f, 1f), // At normalized distance 0, utility is 1
                new Keyframe(1f, 0f)  // At normalized distance 1, utility is 0
            );

            inputMin = 0;
            inputMax = 10;

            scoreMin = 0;
            scoreMax = 1;
        }
    }
}