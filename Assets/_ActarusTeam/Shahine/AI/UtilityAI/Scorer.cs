using UnityEngine;

namespace Teams.ActarusController.Shahine
{
    [System.Serializable]
    public enum ScorerInputType
    {
        Distance,
        Ownership,
        Speed,
        Proximity,
        Energy
    }
    
    [System.Serializable]
    public class Scorer
    {
        public ScorerInputType inputType;
        
        [Header("Input Settings")] 
        public float inputMin = 0f;
        public float inputMax = 10f;

        [Header("Score Settings")] 
        public float scoreMin = 0f;
        public float scoreMax = 1f;

        [Header("Evaluation Curve")] 
        public AnimationCurve scoreCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        /// <summary>
        /// Calcule le score normalisé à partir d'une valeur brute.
        /// </summary>
        public float ComputeScore(float inputValue)
        {
            // Étape 1 : Normalisation entre 0 et 1
            float normalizedInput = Mathf.InverseLerp(inputMin, inputMax, inputValue);

            // Étape 2 : Évaluation de la courbe
            float curveValue = scoreCurve.Evaluate(normalizedInput);

            // Étape 3 : Interpolation entre scoreMin et scoreMax
            float finalScore = Mathf.Lerp(scoreMin, scoreMax, curveValue);

            return finalScore;
        }
    }
}