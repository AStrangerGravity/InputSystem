#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

namespace UnityEngine.Experimental.Input.Editor
{
    [Serializable]
    class InputActionWindowToolbar
    {
        public Action<string> OnSearchChanged;

        [SerializeField]
        private int m_SelectedControlSchemeIndex;
        [SerializeField]
        private int m_SelectedDeviceIndex;

        private string[] m_DeviceIdList;
        private string[] m_DeviceNamesList;
        private InputActionAssetManager m_ActionAssetManager;
        private SearchField m_SearchField;
        private string[] m_AllControlSchemeNames;
        private string m_SearchText;
        private Action m_Apply;

        private static readonly GUIContent m_NoControlScheme = EditorGUIUtility.TrTextContent("No Control Scheme");
        private static readonly GUIContent m_AddSchemeGUI = new GUIContent("Add Control Scheme...");
        private static readonly GUIContent m_EditGUI = EditorGUIUtility.TrTextContent("Edit Control Scheme...");
        private static readonly GUIContent m_DuplicateGUI = EditorGUIUtility.TrTextContent("Duplicate Control Scheme...");
        private static readonly GUIContent m_DeleteGUI = EditorGUIUtility.TrTextContent("Delete Control Scheme...");
        private static readonly GUIContent m_SaveAssetGUI = EditorGUIUtility.TrTextContent("Save Asset");
        private static readonly float m_MininumButtonWidth = 110f;

        string selectedControlSchemeName
        {
            get
            {
                return m_SelectedControlSchemeIndex < 0 ? null : m_AllControlSchemeNames[m_SelectedControlSchemeIndex];
            }
        }

        public bool searching
        {
            get
            {
                return !string.IsNullOrEmpty(m_SearchText);
            }
        }

        public string[] deviceFilter
        {
            get
            {
                if (m_SelectedDeviceIndex < 0)
                {
                    return null;
                }
                if (m_SelectedDeviceIndex == 0)
                {
                    // All devices
                    return m_DeviceIdList.Skip(1).ToArray();
                }
                return m_DeviceIdList.Skip(m_SelectedDeviceIndex).Take(1).ToArray();
            }
        }

        public InputActionWindowToolbar(InputActionAssetManager actionAssetManager, Action apply)
        {
            SetReferences(actionAssetManager, apply);
            RebuildData();
        }

        public void SetReferences(InputActionAssetManager actionAssetManager, Action apply)
        {
            m_ActionAssetManager = actionAssetManager;
            m_Apply = apply;
            RebuildData();
            BuildDeviceList();
        }

        public void SelectControlScheme(string inputControlSchemeName)
        {
            m_SelectedControlSchemeIndex = Array.IndexOf(m_AllControlSchemeNames, inputControlSchemeName);
            BuildDeviceList();
        }

        public void RebuildData()
        {
            m_AllControlSchemeNames = m_ActionAssetManager.m_AssetObjectForEditing.controlSchemes.Select(a => a.name).ToArray();
        }

        public void OnGUI()
        {
            if (m_SearchField == null)
                m_SearchField = new SearchField();

            DrawSchemaSelection();
            DrawDeviceFilterSelection();
            DrawSaveButton();
        }

        private void DrawSchemaSelection()
        {
            var selectedSchema = selectedControlSchemeName;
            if (selectedSchema == null)
                selectedSchema = "No Control Scheme";

            if (GUILayout.Button(selectedSchema, EditorStyles.toolbarPopup, GUILayout.MinWidth(m_MininumButtonWidth)))
            {
                var buttonRect = GUILayoutUtility.GetLastRect();
                var menu = new GenericMenu();
                menu.AddItem(m_NoControlScheme, m_SelectedControlSchemeIndex == -1, OnControlSchemeSelected, -1);
                for (int i = 0; i < m_AllControlSchemeNames.Length; i++)
                {
                    menu.AddItem(new GUIContent(m_AllControlSchemeNames[i]), m_SelectedControlSchemeIndex == i, OnControlSchemeSelected, i);
                }
                menu.AddSeparator("");
                menu.AddItem(m_AddSchemeGUI, false, AddControlScheme, buttonRect);
                if (m_SelectedControlSchemeIndex >= 0)
                {
                    menu.AddItem(m_EditGUI, false, EditSelectedControlScheme, buttonRect);
                    menu.AddItem(m_DuplicateGUI, false, DuplicateControlScheme, buttonRect);
                    menu.AddItem(m_DeleteGUI, false, DeleteControlScheme);
                }
                else
                {
                    menu.AddDisabledItem(m_EditGUI, false);
                    menu.AddDisabledItem(m_DuplicateGUI, false);
                    menu.AddDisabledItem(m_DeleteGUI, false);
                }
                menu.ShowAsContext();
            }
        }

        private void OnControlSchemeSelected(object indexObj)
        {
            var index = (int)indexObj;
            if (m_SelectedControlSchemeIndex == index)
                return;
            m_SelectedControlSchemeIndex = index;
            m_SelectedDeviceIndex = 0;
            BuildDeviceList();
        }

        private void DrawDeviceFilterSelection()
        {
            EditorGUI.BeginDisabledGroup(m_SelectedControlSchemeIndex < 0);
            m_SelectedDeviceIndex = EditorGUILayout.Popup(m_SelectedDeviceIndex, m_DeviceNamesList, EditorStyles.toolbarPopup, GUILayout.MinWidth(m_MininumButtonWidth));
            EditorGUI.EndDisabledGroup();
        }

        private void DrawSaveButton()
        {
            EditorGUI.BeginDisabledGroup(!m_ActionAssetManager.dirty);
            EditorGUILayout.Space();
            if (GUILayout.Button(m_SaveAssetGUI, EditorStyles.toolbarButton))
            {
                m_ActionAssetManager.SaveChangesToAsset();
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.FlexibleSpace();
            EditorGUI.BeginChangeCheck();

            m_SearchText = m_SearchField.OnToolbarGUI(m_SearchText, GUILayout.MaxWidth(250));
            if (EditorGUI.EndChangeCheck())
            {
                if (OnSearchChanged != null)
                    OnSearchChanged(m_SearchText);
            }
        }

        private void BuildDeviceList()
        {
            var devices = new List<string>();
            if (m_SelectedControlSchemeIndex >= 0)
            {
                devices.Add("All devices");
                var controlScheme = m_ActionAssetManager.m_AssetObjectForEditing.GetControlScheme(selectedControlSchemeName);
                devices.AddRange(controlScheme.deviceRequirements.Select(a => a.controlPath).ToList());
            }
            m_DeviceIdList = devices.ToArray();
            m_DeviceNamesList = devices.Select(InputControlPath.ToHumanReadableString).ToArray();
        }

        private void AddControlScheme(object position)
        {
            var popup = new AddControlSchemePopup(m_ActionAssetManager, this, m_Apply);
            popup.SetUniqueName();
            PopupWindow.Show((Rect)position, popup);
        }

        private void DeleteControlScheme()
        {
            if (!EditorUtility.DisplayDialog("Delete scheme", "Confirm scheme deletion", "Delete", "Cancel"))
            {
                return;
            }
            m_ActionAssetManager.m_AssetObjectForEditing.RemoveControlScheme(selectedControlSchemeName);
            m_SelectedControlSchemeIndex = -1;
            m_SelectedDeviceIndex = -1;
            m_Apply();
            RebuildData();
        }

        private void DuplicateControlScheme(object position)
        {
            if (m_SelectedControlSchemeIndex == -1)
                return;
            var popup = new AddControlSchemePopup(m_ActionAssetManager, this, m_Apply);
            popup.DuplicateParametersFrom(selectedControlSchemeName);
            // Since it's a callback, we need to manually handle ExitGUIException
            try
            {
                PopupWindow.Show((Rect)position, popup);
            }
            catch (ExitGUIException) {}
        }

        private void EditSelectedControlScheme(object position)
        {
            var popup = new AddControlSchemePopup(m_ActionAssetManager, this, m_Apply);
            popup.SetSchemaForEditing(selectedControlSchemeName);

            // Since it's a callback, we need to manually handle ExitGUIException
            try
            {
                PopupWindow.Show((Rect)position, popup);
            }
            catch (ExitGUIException) {}
        }
    }
}
#endif // UNITY_EDITOR
