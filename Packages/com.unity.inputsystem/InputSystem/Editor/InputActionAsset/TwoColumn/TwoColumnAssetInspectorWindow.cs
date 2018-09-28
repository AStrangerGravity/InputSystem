#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.IMGUI.Controls;

namespace UnityEngine.Experimental.Input.Editor
{
    internal class TwoColumnAssetInspectorWindow : EditorWindow
    {
        public static class Styles
        {
            public static GUIStyle actionTreeBackground = new GUIStyle("Label");
            public static GUIStyle propertiesBackground = new GUIStyle("Label");
            public static GUIStyle columnHeaderLabel = new GUIStyle(EditorStyles.toolbar);

            ////TODO: move to a better place
            public static string SharedResourcesPath = "Packages/com.unity.inputsystem/InputSystem/Editor/InputActionAsset/Resources/";
            public static string ResourcesPath
            {
                get
                {
                    if (EditorGUIUtility.isProSkin)
                        return SharedResourcesPath + "pro/";
                    return SharedResourcesPath + "personal/";
                }
            }

            static Styles()
            {
                actionTreeBackground.normal.background = AssetDatabase.LoadAssetAtPath<Texture2D>(ResourcesPath + "actionTreeBackground.png");
                actionTreeBackground.border = new RectOffset(3, 3, 3, 3);

                propertiesBackground.normal.background = AssetDatabase.LoadAssetAtPath<Texture2D>(ResourcesPath + "propertiesBackground.png");
                propertiesBackground.border = new RectOffset(3, 3, 3, 3);

                columnHeaderLabel.alignment = TextAnchor.MiddleLeft;
                columnHeaderLabel.fontStyle = FontStyle.Bold;
                columnHeaderLabel.padding.left = 10;
            }
        }

        [SerializeField]
        private TreeViewState m_ActionMapsTreeState;
        [SerializeField]
        private TreeViewState m_ActionsTreeState;
        [SerializeField]
        private TreeViewState m_PickerTreeViewState;
        [SerializeField]
        private InputActionAssetManager m_ActionAssetManager;
        [SerializeField]
        private InputActionWindowToolbar m_InputActionWindowToolbar;
        [SerializeField]
        private ActionInspectorContextMenu m_ContextMenu;

        private InputBindingPropertiesView m_PropertyView;
        internal ActionMapsTree m_ActionMapsTree;
        internal ActionsTree m_ActionsTree;
        internal CopyPasteUtility m_CopyPasteUtility;

        private static bool s_RefreshPending;
        private static readonly string k_FileExtension = ".inputactions";

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedoCallback;
            if (m_ActionAssetManager == null)
            {
                return;
            }

            // Initialize after assembly reload
            m_ActionAssetManager.InitializeObjectReferences();
            m_InputActionWindowToolbar.SetReferences(m_ActionAssetManager);
            m_ContextMenu.SetReferences(this, m_ActionAssetManager);

            InitializeTrees();
            OnActionMapSelection();
            LoadPropertiesForSelection(false);
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedoCallback;
        }

        private void OnDestroy()
        {
            if (m_ActionAssetManager.dirty)
            {
                var result = EditorUtility.DisplayDialogComplex("Unsaved changes", "Do you want to save the changes you made before quitting?", "Save", "Cancel", "Don't Save");
                switch (result)
                {
                    case 0:
                        // Save
                        m_ActionAssetManager.SaveChangesToAsset();
                        m_ActionAssetManager.UnloadAssets();
                        break;
                    case 1:
                        // Cancel
                        Instantiate(this).Show();
                        break;
                    case 2:
                        // Don't save
                        break;
                }
            }
        }

        // Set asset would usually only be called when the window is open
        private void SetAsset(InputActionAsset referencedObject)
        {
            m_ActionAssetManager = new InputActionAssetManager(referencedObject);
            m_ActionAssetManager.InitializeObjectReferences();
            m_InputActionWindowToolbar = new InputActionWindowToolbar(m_ActionAssetManager);
            m_ContextMenu = new ActionInspectorContextMenu(this, m_ActionAssetManager);
            InitializeTrees();

            // Make sure first actions map selected and actions tree expanded
            m_ActionMapsTree.SelectFirstRow();
            OnActionMapSelection();
            m_ActionsTree.ExpandAll();
            LoadPropertiesForSelection(true);
        }

        private void InitializeTrees()
        {
            m_ActionMapsTree = ActionMapsTree.CreateFromSerializedObject(Apply, m_ActionAssetManager.serializedObject, ref m_ActionMapsTreeState);
            m_ActionMapsTree.OnSelectionChanged = OnActionMapSelection;
            m_ActionMapsTree.OnContextClick = m_ContextMenu.OnActionMapContextClick;

            m_ActionsTree = ActionsTree.CreateFromSerializedObject(Apply, ref m_ActionsTreeState);
            m_ActionsTree.OnSelectionChanged = OnActionSelection;
            m_ActionsTree.OnContextClick = m_ContextMenu.OnActionsContextClick;
            m_InputActionWindowToolbar.OnSearchChanged = m_ActionsTree.SetNameFilter;

            m_CopyPasteUtility = new CopyPasteUtility(Apply, m_ActionMapsTree, m_ActionsTree, m_ActionAssetManager.serializedObject);
            if (m_PickerTreeViewState == null)
                m_PickerTreeViewState = new TreeViewState();
        }

        private void OnUndoRedoCallback()
        {
            if (m_ActionMapsTree == null)
                return;

            m_ActionAssetManager.LoadImportedObjectFromGuid();
            Apply();
            // Since the Undo.undoRedoPerformed callback is global, the callback will be called for any undo/redo action
            // We need to make sure we dirty the state only in case of changes to the asset.
            m_ActionAssetManager.UpdateAssetDirtyState();
        }

        private void OnActionMapSelection()
        {
            if (m_ActionMapsTree.GetSelectedRow() != null)
                m_ActionsTree.actionMapProperty = m_ActionMapsTree.GetSelectedRow().elementProperty;
            m_ActionsTree.Reload();
        }

        private void OnActionSelection()
        {
            LoadPropertiesForSelection(true);
        }

        private void LoadPropertiesForSelection(bool checkFocus)
        {
            m_PropertyView = null;

            if ((!checkFocus || m_ActionMapsTree.HasFocus()) && m_ActionMapsTree.GetSelectedRow() != null)
            {
                var row = m_ActionMapsTree.GetSelectedRow();
                if (row != null)
                {
                    m_ActionsTree.actionMapProperty = m_ActionMapsTree.GetSelectedRow().elementProperty;
                    m_ActionsTree.Reload();
                }
            }
            if ((!checkFocus || m_ActionsTree.HasFocus()) && m_ActionsTree.HasSelection() && m_ActionsTree.GetSelection().Count == 1)
            {
                var p = m_ActionsTree.GetSelectedRow();
                if (p != null && p.hasProperties)
                {
                    m_PropertyView = p.GetPropertiesView(Apply, m_PickerTreeViewState);
                }
            }
        }

        internal void Apply()
        {
            m_ActionAssetManager.SetAssetDirty();
            m_ActionAssetManager.ApplyChanges();
            m_ActionMapsTree.Reload();
            m_InputActionWindowToolbar.RebuildData();
            var selectedActionMap = m_ActionMapsTree.GetSelectedActionMap();
            if (selectedActionMap != null)
            {
                m_ActionsTree.actionMapProperty = m_ActionMapsTree.GetSelectedActionMap().elementProperty;
            }
            else
            {
                m_ActionsTree.actionMapProperty = null;
            }
            m_ActionsTree.Reload();
            OnActionSelection();
        }

        private void OnGUI()
        {
            if (Event.current.type == EventType.KeyDown)
            {
                if (m_ActionMapsTree.HasFocus() && Event.current.keyCode == KeyCode.RightArrow)
                {
                    if (!m_ActionsTree.HasSelection())
                        m_ActionsTree.SelectFirstRow();
                    m_ActionsTree.SetFocus();
                }
            }

            EditorGUILayout.BeginVertical();
            // Toolbar.
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            m_InputActionWindowToolbar.OnGUI();
            GUILayout.Space(5);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            //Draw columns
            EditorGUILayout.BeginHorizontal();
            var columnOneRect = GUILayoutUtility.GetRect(0, 0, 0, 0, Styles.actionTreeBackground, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
            var columnTwoRect = GUILayoutUtility.GetRect(0, 0, 0, 0, Styles.actionTreeBackground, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
            DrawActionMapsColumn(columnOneRect);
            DrawActionsColumn(columnTwoRect);
            DrawPropertiesColumn();
            EditorGUILayout.EndHorizontal();

            // Bottom margin
            GUILayout.Space(3);
            EditorGUILayout.EndVertical();

            if (Event.current.type == EventType.ValidateCommand)
            {
                if (CopyPasteUtility.IsValidCommand(Event.current.commandName))
                {
                    Event.current.Use();
                }
            }
            if (Event.current.type == EventType.ExecuteCommand)
            {
                m_CopyPasteUtility.HandleCommandEvent(Event.current.commandName);
            }
        }

        private void DrawActionMapsColumn(Rect columnRect)
        {
            var labelRect = new Rect(columnRect);
            labelRect.height = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing * 2;
            columnRect.y += labelRect.height;
            columnRect.height -= labelRect.height;

            // Draw header
            var header = EditorGUIUtility.TrTextContent("Action maps");
            EditorGUI.LabelField(labelRect, GUIContent.none, Styles.actionTreeBackground);
            var headerRect = new Rect(labelRect.x + 1, labelRect.y + 1, labelRect.width - 2, labelRect.height - 2);
            EditorGUI.LabelField(headerRect, header, Styles.columnHeaderLabel);

            labelRect.x = labelRect.width - (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
            labelRect.width = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            var plusIconContext = EditorGUIUtility.IconContent("Toolbar Plus");
            if (GUI.Button(labelRect, plusIconContext, GUIStyle.none))
            {
                m_ContextMenu.ShowAddActionMapMenu();
            }

            // Draw border rect
            EditorGUI.LabelField(columnRect, GUIContent.none, Styles.propertiesBackground);
            // Compensate for the border rect
            columnRect.x += 1;
            columnRect.height -= 1;
            columnRect.width -= 2;
            m_ActionMapsTree.OnGUI(columnRect);
        }

        private void DrawActionsColumn(Rect columnRect)
        {
            var labelRect = new Rect(columnRect);
            labelRect.height = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing * 2;
            columnRect.y += labelRect.height;
            columnRect.height -= labelRect.height;

            GUIContent header;
            if (m_InputActionWindowToolbar.searching)
                header = EditorGUIUtility.TrTextContent("Actions (Searching)");
            else
                header = EditorGUIUtility.TrTextContent("Actions");

            EditorGUI.LabelField(labelRect, GUIContent.none, Styles.actionTreeBackground);
            var headerRect = new Rect(labelRect.x + 1, labelRect.y + 1, labelRect.width - 2, labelRect.height - 2);
            EditorGUI.LabelField(headerRect, header, Styles.columnHeaderLabel);

            labelRect.x = labelRect.x + labelRect.width - (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing);
            labelRect.width = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            if (GUI.Button(labelRect, EditorGUIUtility.IconContent("Toolbar Plus"), GUIStyle.none))
            {
                m_ContextMenu.ShowAddActionsMenu();
            }

            // Draw border rect
            EditorGUI.LabelField(columnRect, GUIContent.none, Styles.propertiesBackground);
            // Compensate for the border rect
            columnRect.x += 1;
            columnRect.height -= 1;
            columnRect.width -= 2;
            m_ActionsTree.OnGUI(columnRect);
        }

        private void DrawPropertiesColumn()
        {
            EditorGUILayout.BeginVertical(Styles.propertiesBackground, GUILayout.Width(position.width * (1 / 3f)));

            var rect = GUILayoutUtility.GetRect(0, EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing * 2, GUILayout.ExpandWidth(true));
            rect.x -= 2;
            rect.y -= 1;
            rect.width += 4;

            EditorGUI.LabelField(rect, GUIContent.none, Styles.propertiesBackground);
            var headerRect = new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2);
            EditorGUI.LabelField(headerRect, "Properties", Styles.columnHeaderLabel);

            if (m_PropertyView != null)
            {
                m_PropertyView.OnGUI();
            }
            else
            {
                GUILayout.FlexibleSpace();
            }

            EditorGUILayout.EndVertical();
        }

        public static void RefreshAllOnAssetReimport()
        {
            if (s_RefreshPending)
                return;

            // We don't want to refresh right away but rather wait for the next editor update
            // to then do one pass of refreshing action editor windows.
            EditorApplication.delayCall += RefreshAllOnAssetReimportCallback;
            s_RefreshPending = true;
        }

        private static void RefreshAllOnAssetReimportCallback()
        {
            s_RefreshPending = false;

            // When the asset is modified outside of the editor
            // and the importer settings are visible in the inspector
            // the asset references in the importer inspector need to be force rebuild
            // (otherwise we gets lots of exceptions)
            ActiveEditorTracker.sharedTracker.ForceRebuild();

            var windows = Resources.FindObjectsOfTypeAll<TwoColumnAssetInspectorWindow>();
            foreach (var window in windows)
                window.ReloadAssetFromFile();
        }

        private void ReloadAssetFromFile()
        {
            if (!m_ActionAssetManager.dirty)
            {
                m_ActionAssetManager.CreateWorkingCopyAsset();
                InitializeTrees();
                LoadPropertiesForSelection(false);
                Repaint();
            }
        }

        [OnOpenAsset]
        internal static bool OnOpenAsset(int instanceId, int line)
        {
            var path = AssetDatabase.GetAssetPath(instanceId);
            if (!path.EndsWith(k_FileExtension))
                return false;

            var obj = EditorUtility.InstanceIDToObject(instanceId) as InputActionAsset;
            if (obj == null)
                return false;

            // See if we have an existing editor window that has the asset open.
            var inputManagers = Resources.FindObjectsOfTypeAll<TwoColumnAssetInspectorWindow>();
            var window = inputManagers.FirstOrDefault(w => w.m_ActionAssetManager.ImportedAssetObjectEquals(obj));
            if (window != null)
            {
                window.Show();
                window.Focus();
                return true;
            }

            // No, so create a new window.
            window = CreateInstance<TwoColumnAssetInspectorWindow>();
            window.titleContent = new GUIContent(obj.name + " (Input Manager)");
            window.SetAsset(obj);
            window.Show();

            return true;
        }
    }
}
#endif // UNITY_EDITOR
