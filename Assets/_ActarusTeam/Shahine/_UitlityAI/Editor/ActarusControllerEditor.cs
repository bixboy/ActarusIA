using Teams.ActarusController.Shahine;
using UnityEngine;
using UnityEditor;

namespace UtilityAI {
    [CustomEditor(typeof(ActarusControllerUtilityAI))]
    public class ActarusControllerEditor : Editor {
        void OnEnable() {
            this.RequiresConstantRepaint();
        }
            
        public override void OnInspectorGUI() {
            base.OnInspectorGUI(); // Draw the default inspector

            ActarusControllerUtilityAI actarusController = (ActarusControllerUtilityAI) target;

            if (Application.isPlaying) {
                AIAction chosenAction = GetChosenAction(actarusController);

                if (chosenAction != null) {
                    EditorGUILayout.LabelField($"Current Chosen Action: {chosenAction.name}", EditorStyles.boldLabel);
                }
                
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Actions/Considerations", EditorStyles.boldLabel);


                foreach (AIAction action in actarusController.actions) {
                    float utility = action.CalculateUtility(actarusController.context);
                    EditorGUILayout.LabelField($"Action: {action.name}, Utility: {utility:F2}");

                    // Draw the single consideration for the action
                    DrawConsideration(action.consideration, actarusController.context, 1);
                }
            } else {
                EditorGUILayout.HelpBox("Enter Play mode to view utility values.", MessageType.Info);
            }
        }

        private void DrawConsideration(Consideration consideration, Context context, int indentLevel) {
            EditorGUI.indentLevel = indentLevel;

            if (consideration is CompositeConsideration compositeConsideration) {
                EditorGUILayout.LabelField(
                    $"Composite Consideration: {compositeConsideration.name}, Operation: {compositeConsideration.operation}"
                );

                foreach (Consideration subConsideration in compositeConsideration.considerations) {
                    DrawConsideration(subConsideration, context, indentLevel + 1);
                }
            } else {
                float value = consideration.Evaluate(context);
                EditorGUILayout.LabelField($"Consideration: {consideration.name}, Value: {value:F2}");
            }

            EditorGUI.indentLevel = indentLevel - 1; // Reset indentation after drawing
        }

        private AIAction GetChosenAction(ActarusControllerUtilityAI actarusController) {
            float highestUtility = float.MinValue;
            AIAction chosenAction = null;

            foreach (var action in actarusController.actions) {
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