using UnityEngine;

namespace Teams.Actarus {
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

            if (context == null)
            {
                Debug.LogWarning("Context in CurveConsideration : " + name + " is null");
                return 1;
            }
            
            object raw = context.GetData<object>(contextKey);
            float value = raw switch {
                float f => f,
                int i => i,
                _ => 0f
            };

            float normalized = inputMax > 0f ? Mathf.Clamp(value, inputMin, inputMax) / inputMax : 0f;
            float utility = curve.Evaluate(normalized);
            return Mathf.InverseLerp(scoreMin, scoreMax, utility);

        }

        void Reset() {
            curve = new AnimationCurve(
                new Keyframe(0f, 1f), 
                new Keyframe(1f, 0f)  
            );

            inputMin = 0;
            inputMax = 10;

            scoreMin = 0;
            scoreMax = 1;
        }
    }
}