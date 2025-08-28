using UnityEngine;
using UnityEditor;
using System.Linq;

namespace Proselyte.PersistentIdSystem
{
    /// <summary>
    /// Custom editor for PersistentIdRegistrySO that displays registered IDs
    /// and provides utilities for registry management.
    /// </summary>
    [CustomEditor(typeof(PersistentIdRegistrySO))]
    public class PersistentIdRegistrySOEditor : Editor
    {
        private Vector2 scrollPosition;
        private bool showIds = true;
        private string searchFilter = "";

        public override void OnInspectorGUI()
        {
            var registry = (PersistentIdRegistrySO)target;

            EditorGUILayout.Space();

            // Header
            EditorGUILayout.LabelField("Persistent ID Registry", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Total Registered IDs: {registry.RegisteredCount}");

            EditorGUILayout.Space();

            // Utility buttons
            EditorGUILayout.BeginHorizontal();

            if(GUILayout.Button("Validate Registry"))
            {
                registry.ValidateRegistry();
                EditorUtility.DisplayDialog("Registry Validation", "Registry validation completed.", "OK");
            }

            if(GUILayout.Button("Refresh"))
            {
                Repaint();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Search filter
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Search:", GUILayout.Width(60));
            searchFilter = EditorGUILayout.TextField(searchFilter);

            if(GUILayout.Button("Clear", GUILayout.Width(50)))
            {
                searchFilter = "";
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // Toggle for showing IDs
            showIds = EditorGUILayout.Foldout(showIds, "Registered IDs", true);

            if(showIds && registry.RegisteredCount > 0)
            {
                DrawRegisteredIds(registry);
            }
            else if(registry.RegisteredCount == 0)
            {
                EditorGUILayout.HelpBox("No IDs registered yet.", MessageType.Info);
            }

            EditorGUILayout.Space();

            // Warning about manual editing
            EditorGUILayout.HelpBox(
                "This registry is automatically managed. Manual editing is not recommended and may cause issues.",
                MessageType.Warning
            );
        }

        private void DrawRegisteredIds(PersistentIdRegistrySO registry)
        {
            var filteredIds = registry.RegisteredIds.AsEnumerable();

            // Apply search filter
            if(!string.IsNullOrEmpty(searchFilter))
            {
                if(uint.TryParse(searchFilter, out uint searchId))
                {
                    filteredIds = filteredIds.Where(id => id == searchId);
                }
                else if(searchFilter.StartsWith("0x") && uint.TryParse(searchFilter.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out uint hexId))
                {
                    filteredIds = filteredIds.Where(id => id == hexId);
                }
                else
                {
                    filteredIds = filteredIds.Where(id =>
                        id.ToString().Contains(searchFilter) ||
                        $"0x{id:X8}".Contains(searchFilter.ToUpper())
                    );
                }
            }

            var sortedIds = filteredIds.OrderBy(id => id).ToList();

            if(sortedIds.Count == 0)
            {
                EditorGUILayout.HelpBox("No IDs match the current search filter.", MessageType.Info);
                return;
            }

            // Scroll view for large lists
            const float rowHeight = 20f;
            const int minVisibleRows = 12;
            float minHeight = rowHeight * minVisibleRows + 10f;
            float maxHeight = Mathf.Max(minHeight, Mathf.Min(300f, sortedIds.Count * rowHeight + 10f));
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(maxHeight));

            // Table header
            EditorGUILayout.BeginHorizontal("box");
            EditorGUILayout.LabelField("Decimal", EditorStyles.boldLabel, GUILayout.Width(100));
            EditorGUILayout.LabelField("Hexadecimal", EditorStyles.boldLabel, GUILayout.Width(100));
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            // ID rows
            foreach(var id in sortedIds)
            {
                EditorGUILayout.BeginHorizontal();

                // Decimal value
                EditorGUILayout.SelectableLabel(id.ToString(), GUILayout.Width(100), GUILayout.Height(16));

                // Hex value
                EditorGUILayout.SelectableLabel($"0x{id:X8}", GUILayout.Width(100), GUILayout.Height(16));

                // Action buttons
                EditorGUILayout.BeginHorizontal();

                if(GUILayout.Button("Copy Dec", GUILayout.Width(70)))
                {
                    EditorGUIUtility.systemCopyBuffer = id.ToString();
                    Debug.Log($"Copied to clipboard: {id}");
                }

                if(GUILayout.Button("Copy Hex", GUILayout.Width(70)))
                {
                    EditorGUIUtility.systemCopyBuffer = $"0x{id:X8}";
                    Debug.Log($"Copied to clipboard: 0x{id:X8}");
                }

                // Dangerous operation - only show in debug mode
                if(Application.isPlaying == false && GUILayout.Button("Remove", GUILayout.Width(60)))
                {
                    if(EditorUtility.DisplayDialog(
                        "Remove ID",
                        $"Remove ID {id} (0x{id:X8}) from registry?\n\nWarning: This may break existing references!",
                        "Remove",
                        "Cancel"))
                    {
                        registry.UnregisterId(id);
                        break; // Exit loop since we modified the collection
                    }
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            // Show filtered count if search is active
            if(!string.IsNullOrEmpty(searchFilter))
            {
                EditorGUILayout.LabelField($"Showing {sortedIds.Count} of {registry.RegisteredCount} IDs");
            }
        }
    }
}
