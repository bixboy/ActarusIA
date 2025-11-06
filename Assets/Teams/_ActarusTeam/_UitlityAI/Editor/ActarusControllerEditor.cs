using System;
using UnityEngine;
using UnityEditor;

namespace Teams.Actarus {
    [CustomEditor(typeof(ActarusControllerUtilityAI))]
    public class ActarusControllerEditor : Editor {
        void OnEnable() {
            this.RequiresConstantRepaint();
        }
            
        public override void OnInspectorGUI() {
            base.OnInspectorGUI(); 

            ActarusControllerUtilityAI actarusController = (ActarusControllerUtilityAI) target;

            if (Application.isPlaying) 
            {
                AIAction chosenAction = GetChosenAction(actarusController);
                
                if (actarusController.context == null)
                {
                    EditorGUILayout.HelpBox("⚠️ Context is NULL!", MessageType.Warning);
                }

                EditorGUILayout.Space();
                
                if (chosenAction != null) {
                    EditorGUILayout.LabelField($"Current Chosen Action: {chosenAction.name}", EditorStyles.boldLabel);
                }
                
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Actions/Considerations", EditorStyles.boldLabel);


                foreach (AIAction action in actarusController.actions) 
                {
                    float utility = action.CalculateUtility(actarusController.context);
                    EditorGUILayout.LabelField($"Action: {action.name}, Utility: {utility:F2}");


                    DrawConsideration(action.consideration, actarusController.context, 1);
                }
            } 
            else 
            {
                
                EditorGUILayout.HelpBox("Enter Play mode to view utility values.", MessageType.Info);
            }
        }

        private void DrawConsideration(Consideration consideration, Context context, int indentLevel)
        {
            if (consideration == null) return;

            EditorGUI.indentLevel = indentLevel;

            if (consideration is CompositeConsideration composite)
            {
                EditorGUILayout.LabelField($"[Composite] {composite.name} (Op: {composite.operation}) → {composite.Evaluate(context):F2}");

                var first = composite.considerations.FirstConsideration;
                var second = composite.considerations.SecondConsideration;

                if (first)
                    DrawConsideration(first, context, indentLevel + 1);
                if (second)
                    DrawConsideration(second, context, indentLevel + 1);
            }
            else if (consideration is FinalCompositeConsideration final)
            {
                EditorGUILayout.LabelField($"[FinalComposite] {final.name} → {final.Evaluate(context):F2}" );

                if (final.considerations == null || final.considerations.Count == 0)
                {
                    EditorGUILayout.LabelField("⚠️ No sub-considerations assigned.");
                }
                else
                {
                    foreach (var sub in final.considerations)
                    {
                        if (sub != null)
                            DrawConsideration(sub, context, indentLevel + 1);
                        else
                            EditorGUILayout.LabelField("⚠️ Sub-consideration is null.");
                    }
                }
            }
            else
            {
                float value = consideration.Evaluate(context);
                EditorGUILayout.LabelField($"[Leaf] {consideration.name} → {value:F2}");
            }

            EditorGUI.indentLevel = indentLevel - 1;
        }

        private static float SafeEvaluate(Consideration consideration, Context context) {
            if (context == null) {
                return 0f;
            }

            try {
                return Mathf.Clamp01(consideration.Evaluate(context));
            } catch (Exception ex) {
                Debug.LogWarning($"Failed to evaluate consideration '{consideration.name}': {ex.Message}");
                return 0f;
            }
        }

        private static string DisplayContextKey(string key) {
            return string.IsNullOrWhiteSpace(key) ? "(default)" : key;
        }

        private AIAction GetChosenAction(ActarusControllerUtilityAI actarusController) {
            float highestUtility = float.MinValue;
            AIAction chosenAction = null;

            foreach (var action in actarusController.actions) 
            {
                
                float utility = action.CalculateUtility(actarusController.context);
                if (utility > highestUtility) {
                    highestUtility = utility;
                    chosenAction = action;
                }
            }

            return chosenAction;
        }
    }
}