#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;

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
        private string searchFilter = "";

        private bool init = false;
        private Dictionary<string, bool> sceneFoldout = new();

        public override void OnInspectorGUI()
        {
            var registry = (PersistentIdRegistrySO)target;

            if(init == false)
            {
                foreach(var scene in registry.sceneDataList)
                {
                    sceneFoldout.Add(scene.sceneGuid, true);
                }
                init = true;
            }

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

            EditorGUILayout.LabelField("Registered IDs", EditorStyles.boldLabel);

            if(registry.RegisteredCount > 0)
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
            // Scroll view for large lists
            const float rowHeight = 16f;
            const int minVisibleRows = 16;
            float minHeight = rowHeight * minVisibleRows + 60f;
            float maxHeight = Mathf.Max(minHeight, Mathf.Min(600f, registry.RegisteredCount * rowHeight + 10f));
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(maxHeight));

            int totalFilteredCount = 0;

            foreach(var scene in registry.sceneDataList)
            {
                string sceneGuid = scene.sceneGuid;
                string scenePath = AssetDatabase.GUIDToAssetPath(sceneGuid);
                string sceneTitle = string.IsNullOrEmpty(scenePath) ? "Unknown Scene" :
                    scenePath.Split('/')[^1].Replace(".unity", "");

                // Ensure foldout state is initialized
                if(!sceneFoldout.ContainsKey(sceneGuid))
                    sceneFoldout[sceneGuid] = true;

                // Filter IDs for this scene
                var filteredSceneIds = ApplySearchFilter(scene.registeredIds);
                var sortedSceneIds = filteredSceneIds.OrderBy(id => id).ToList();

                totalFilteredCount += sortedSceneIds.Count;

                // Skip scenes with no matching IDs if search is active
                if(!string.IsNullOrEmpty(searchFilter) && sortedSceneIds.Count == 0)
                    continue;

                // Foldout header with count
                sceneFoldout[sceneGuid] = EditorGUILayout.Foldout(
                    sceneFoldout[sceneGuid],
                    $"Scene: {sceneTitle} ({sortedSceneIds.Count} IDs)",
                    true
                );

                if(!sceneFoldout[sceneGuid])
                    continue;

                EditorGUILayout.BeginVertical("box");

                if(sortedSceneIds.Count == 0)
                {
                    EditorGUILayout.HelpBox("No matching IDs in this scene.", MessageType.None);
                }
                else
                {
                    // Table header
                    EditorGUILayout.BeginHorizontal("box");
                    EditorGUILayout.LabelField("Decimal", EditorStyles.boldLabel, GUILayout.Width(100));
                    EditorGUILayout.LabelField("Hexadecimal", EditorStyles.boldLabel, GUILayout.Width(100));
                    EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
                    EditorGUILayout.EndHorizontal();

                    foreach(var id in sortedSceneIds)
                    {
                        EditorGUILayout.BeginHorizontal();

                        EditorGUILayout.SelectableLabel(id.ToString(), GUILayout.Width(100), GUILayout.Height(16));
                        EditorGUILayout.SelectableLabel($"0x{id:X8}", GUILayout.Width(100), GUILayout.Height(16));

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

                        if(!Application.isPlaying && GUILayout.Button("Remove", GUILayout.Width(60)))
                        {
                            if(EditorUtility.DisplayDialog(
                                "Remove ID",
                                $"Remove ID {id} (0x{id:X8}) from registry?\n\nWarning: This may break existing references!",
                                "Remove", "Cancel"))
                            {
                                registry.UnregisterId(id);
                                break;
                            }
                        }

                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.EndHorizontal();
                    }
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();

            // Show filtered count if search is active
            if(!string.IsNullOrEmpty(searchFilter))
            {
                EditorGUILayout.LabelField($"Showing {totalFilteredCount} of {registry.RegisteredCount} IDs");
            }
            else if(registry.sceneDataList.Count == 0)
            {
                EditorGUILayout.HelpBox("No scenes with registered IDs found.", MessageType.Info);
            }
        }

        // Helper method to avoid code duplication
        private IEnumerable<uint> ApplySearchFilter(List<uint> ids)
        {
            if(string.IsNullOrEmpty(searchFilter))
                return ids;

            if(uint.TryParse(searchFilter, out uint searchId))
            {
                return ids.Where(id => id == searchId);
            }
            else if(searchFilter.StartsWith("0x") &&
                    uint.TryParse(searchFilter.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out uint hexId))
            {
                return ids.Where(id => id == hexId);
            }
            else
            {
                return ids.Where(id =>
                    id.ToString().Contains(searchFilter) ||
                    $"0x{id:X8}".Contains(searchFilter.ToUpper())
                );
            }
        }
    }
}
#endif