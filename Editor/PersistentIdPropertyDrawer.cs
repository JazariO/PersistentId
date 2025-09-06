#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace Proselyte.PersistentIdSystem
{
    /// <summary>
    /// Custom property drawer for PersistentId that makes the field read-only in inspector
    /// and provides context menu options for regeneration and copying.
    /// </summary>
    [CustomPropertyDrawer(typeof(PersistentId))]
    public class PersistentIdPropertyDrawer : PropertyDrawer
    {
        private const float COPY_BUTTON_WIDTH = 50f;
        private const float SPACING = 2f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var idProp = property.FindPropertyRelative("id");
            if(idProp == null)
            {
                EditorGUI.LabelField(position, label.text, "Invalid PersistentId structure");
                EditorGUI.EndProperty();
                return;
            }

            uint id = (uint)idProp.intValue;

            // Calculate rects
            var labelRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, position.height);
            var fieldRect = new Rect(
                position.x + EditorGUIUtility.labelWidth,
                position.y,
                position.width - EditorGUIUtility.labelWidth - COPY_BUTTON_WIDTH - SPACING,
                position.height
            );
            var buttonRect = new Rect(
                fieldRect.xMax + SPACING,
                position.y,
                COPY_BUTTON_WIDTH,
                position.height
            );

            // Draw label
            EditorGUI.LabelField(labelRect, label);

            // Handle right-click context menu
            if(Event.current.type == EventType.ContextClick && fieldRect.Contains(Event.current.mousePosition))
            {
                ShowContextMenu(property);
                Event.current.Use();
            }

            // Draw grayed-out hex field
            var oldEnabled = GUI.enabled;
            GUI.enabled = false;

            string displayValue = id == 0 ? "Not Generated" : $"0x{id:X8}";
            EditorGUI.TextField(fieldRect, displayValue);

            GUI.enabled = oldEnabled;

            // Draw copy button
            if(GUI.Button(buttonRect, "Copy"))
            {
                string copyValue = id == 0 ? "0" : id.ToString();
                EditorGUIUtility.systemCopyBuffer = copyValue;
                Debug.Log($"Copied PersistentId to clipboard: {copyValue}");
            }

            // Tooltip
            var tooltipRect = new Rect(position.x, position.y, position.width - COPY_BUTTON_WIDTH - SPACING, position.height);
            EditorGUI.LabelField(tooltipRect, new GUIContent("", GetTooltipText(id)));

            EditorGUI.EndProperty();
        }

        private void ShowContextMenu(SerializedProperty property)
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Regenerate Id"), false, () =>
            {
                if(EditorUtility.DisplayDialog(
                    "Regenerate Persistent Id",
                    "This will generate a new ID and may break existing save data references. Are you sure?",
                    "Regenerate",
                    "Cancel"))
                {
                    PersistentIdManager.RegenerateId(property);
                }
            });

            var idProp = property.FindPropertyRelative("id");
            uint id = (uint)idProp.intValue;

            if(id != 0)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Copy Id (Decimal)"), false, () =>
                {
                    EditorGUIUtility.systemCopyBuffer = id.ToString();
                });

                menu.AddItem(new GUIContent("Copy Id (Hex)"), false, () =>
                {
                    EditorGUIUtility.systemCopyBuffer = $"0x{id:X8}";
                });
            }

            menu.ShowAsContext();
        }

        private string GetTooltipText(uint id)
        {
            if(id == 0)
                return "PersistentId not generated. Will be auto-generated when needed.";

            return $"PersistentId: {id} (0x{id:X8})\nRight-click for options";
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }
    }
}
#endif
