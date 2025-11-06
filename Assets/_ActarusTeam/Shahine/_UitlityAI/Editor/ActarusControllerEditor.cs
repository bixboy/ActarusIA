using Teams.ActarusController.Shahine;
using UnityEngine;
using UnityEditor;

namespace UtilityAI
{
    [CustomEditor(typeof(ActarusControllerUtilityAI))]
    public class ActarusControllerEditor : Editor
    {
        void OnEnable()
        {
            RequiresConstantRepaint();
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI(); // Default inspector

            ActarusControllerUtilityAI actarusController = (ActarusControllerUtilityAI)target;

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play mode to view utility values.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            AIAction chosenAction = GetChosenAction(actarusController);
            if (chosenAction != null)
            {
                EditorGUILayout.LabelField($"Current Chosen Action: {chosenAction.name}", EditorStyles.boldLabel);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Actions & Considerations", EditorStyles.boldLabel);

            foreach (AIAction action in actarusController.actions)
            {
                float utility = action.CalculateUtility(actarusController.context);
                EditorGUILayout.LabelField($"Action: {action.name}, Utility: {utility:F2}");
                DrawConsideration(action.consideration, actarusController.context, 1);
                EditorGUILayout.Space();
            }
        }

        private void DrawConsideration(Consideration consideration, Context context, int indentLevel)
        {
            if (consideration == null) return;

            EditorGUI.indentLevel = indentLevel;

            if (consideration is CompositeConsideration composite)
            {
                EditorGUILayout.LabelField($"[Composite] {composite.name} (Op: {composite.operation})");

                var first = composite.considerations.FirstConsideration;
                var second = composite.considerations.SecondConsideration;

                if (first)
                    DrawConsideration(first, context, indentLevel + 1);
                if (second)
                    DrawConsideration(second, context, indentLevel + 1);
            }
            else if (consideration is FinalCompositeConsideration final)
            {
                EditorGUILayout.LabelField($"[FinalComposite] {final.name}");

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

        private AIAction GetChosenAction(ActarusControllerUtilityAI controller)
        {
            float highest = float.MinValue;
            AIAction best = null;

            foreach (var action in controller.actions)
            {
                float utility = action.CalculateUtility(controller.context);
                if (utility > highest)
                {
                    highest = utility;
                    best = action;
                }
            }

            return best;
        }
    }
}
