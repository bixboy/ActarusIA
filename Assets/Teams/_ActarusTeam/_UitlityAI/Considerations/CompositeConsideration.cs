using System;
using UnityEngine;

namespace Teams.Actarus {
    [CreateAssetMenu(menuName = "UtilityAI/Considerations/CompositeConsideration")]
    public class CompositeConsideration : Consideration
    {
        
        public AnimationCurve curve;
        
        public float inputMin;
        public float inputMax;
        
        public float scoreMin;
        public float scoreMax;
        
        public enum OperationType { Average, Multiply, Add, Subtract, Divide, Max, Min }
        
        public bool allMustBeNonZero = true;
        
        public OperationType operation = OperationType.Max;
        public Considerations considerations;
        
        

        public override float Evaluate(Context context) 
        {
            if (considerations.IsNull) return 0f;
            
            float result = considerations.FirstConsideration?.Evaluate(context) ?? 0f;
            
            float value = considerations.SecondConsideration?.Evaluate(context) ?? 0f;
            
            if (allMustBeNonZero && (result == 0f || value == 0f))
                return 0f;
            
            switch (operation) {
                case OperationType.Average:
                    result = (result + value) / 2;
                    break;
                case OperationType.Multiply:
                    result *= value;
                    break;
                case OperationType.Add:
                    result += value;
                    break;
                case OperationType.Subtract:
                    result -= value;
                    break;
                case OperationType.Divide:
                    result = value != 0 ? result / value : result; 
                    break;
                case OperationType.Max:
                    result = Mathf.Max(result, value);
                    break;
                case OperationType.Min:
                    result = Mathf.Min(result, value);
                    break;
            }

            float normalized = Mathf.Clamp(result, inputMin, inputMax) / inputMax;

            float evaluate = curve.Evaluate(normalized);
            
            return Mathf.InverseLerp(scoreMin, scoreMax, evaluate);
        }

        void Reset()
        {
            curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

            inputMin = 0;
            inputMax = 10;

            scoreMin = 0;
            scoreMax = 1;
        }
    }
    

    [Serializable]
    public struct Considerations
    {
        public Consideration FirstConsideration;
        public Consideration SecondConsideration;
        
        public bool IsNull => !FirstConsideration && !SecondConsideration;
        
        public static bool operator ==(Considerations a, object b)
        {

            if (b is null)
                return a.IsNull;
            
            if (b is Considerations other)
                return a.Equals(other);

            return false;
        }

        public static bool operator !=(Considerations a, object b) => !(a == b);
        

        public override bool Equals(object obj)
        {
            if (obj is not Considerations other)
                return false;

            return Nullable.Equals(FirstConsideration, other.FirstConsideration) &&
                   Nullable.Equals(SecondConsideration, other.SecondConsideration);
        }

        public override int GetHashCode()
        {
            int hash1 = FirstConsideration?.GetHashCode() ?? 0;
            int hash2 = SecondConsideration?.GetHashCode() ?? 0;
            return hash1 ^ hash2;
        }
        
    }

}