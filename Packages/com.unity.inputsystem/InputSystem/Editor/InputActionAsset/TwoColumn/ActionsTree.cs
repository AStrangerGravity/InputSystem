#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

namespace UnityEngine.Experimental.Input.Editor
{
    internal class ActionsTree : InputActionTreeBase
    {
        private string m_GroupFilter;
        private string m_NameFilter;
        private Action m_ApplyAction;

        public Action OnSelectionChanged;
        public Action<SerializedProperty> OnContextClick;

        public SerializedProperty actionMapProperty;

        public static ActionsTree CreateFromSerializedObject(Action applyAction, ref TreeViewState treeViewState)
        {
            if (treeViewState == null)
            {
                treeViewState = new TreeViewState();
            }
            var treeView = new ActionsTree(applyAction, treeViewState);
            treeView.Reload();
            treeView.ExpandAll();
            return treeView;
        }

        static bool OnFoldoutDraw(Rect position, bool expandedState, GUIStyle style)
        {
            var indent = (int)(position.x / 15);
            position.x = 6 * indent + 8;
            return EditorGUI.Foldout(position, expandedState, GUIContent.none, style);
        }

        protected ActionsTree(Action applyAction, TreeViewState state)
            : base(state)
        {
            m_ApplyAction = applyAction;
            ////REVIEW: good enough like this for 2018.2?
            #if UNITY_2018_3_OR_NEWER
            foldoutOverride += OnFoldoutDraw;
            #endif
            Reload();
        }

        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem
            {
                id = 0,
                depth = -1
            };
            root.children = new List<TreeViewItem>();
            if (actionMapProperty != null)
            {
                ParseActionMap(root, actionMapProperty, 0);
                // is searching
                if (!string.IsNullOrEmpty(m_NameFilter))
                {
                    FilterResults(root);
                }
            }
            return root;
        }

        // Return true is the child node should be removed from the parent
        private bool FilterResults(TreeViewItem root)
        {
            if (root.hasChildren)
            {
                var listToRemove = new List<TreeViewItem>();
                foreach (var child in root.children)
                {
                    if (root.displayName != null && root.displayName.ToLower().Contains(m_NameFilter))
                    {
                        continue;
                    }

                    if (FilterResults(child))
                    {
                        listToRemove.Add(child);
                    }
                }
                foreach (var item in listToRemove)
                {
                    root.children.Remove(item);
                }

                return !root.hasChildren;
            }

            if (root.displayName == null)
                return false;
            return !root.displayName.ToLower().Contains(m_NameFilter);
        }

        protected void ParseActionMap(TreeViewItem parentTreeItem, SerializedProperty actionMapProperty, int depth)
        {
            var actionsArrayProperty = actionMapProperty.FindPropertyRelative("m_Actions");
            for (var i = 0; i < actionsArrayProperty.arraySize; i++)
            {
                ParseAction(parentTreeItem, actionMapProperty, actionsArrayProperty, i, depth);
            }
        }

        private void ParseAction(TreeViewItem parentTreeItem, SerializedProperty actionMapProperty, SerializedProperty actionsArrayProperty, int index, int depth)
        {
            var bindingsArrayProperty = actionMapProperty.FindPropertyRelative("m_Bindings");
            var actionMapName = actionMapProperty.FindPropertyRelative("m_Name").stringValue;
            var actionProperty = actionsArrayProperty.GetArrayElementAtIndex(index);

            var actionItem = new ActionTreeItem(actionMapProperty, actionProperty, index);
            actionItem.depth = depth;
            var actionName = actionItem.actionName;

            ParseBindings(actionItem, actionMapName, actionName, bindingsArrayProperty, depth + 1);
            parentTreeItem.AddChild(actionItem);
        }

        protected void ParseBindings(TreeViewItem parent, string actionMapName, string actionName, SerializedProperty bindingsArrayProperty, int depth)
        {
            var bindingsCount = InputActionSerializationHelpers.GetBindingCount(bindingsArrayProperty, actionName);
            CompositeGroupTreeItem compositeGroupTreeItem = null;
            for (var j = 0; j < bindingsCount; j++)
            {
                var bindingProperty = InputActionSerializationHelpers.GetBinding(bindingsArrayProperty, actionName, j);
                var bindingsItem = new BindingTreeItem(actionMapName, bindingProperty, j);
                bindingsItem.depth = depth;
                if (!string.IsNullOrEmpty(m_GroupFilter) && !bindingsItem.groups.Split(';').Contains(m_GroupFilter))
                {
                    continue;
                }
                if (bindingsItem.isComposite)
                {
                    compositeGroupTreeItem = new CompositeGroupTreeItem(actionMapName, bindingProperty, j);
                    compositeGroupTreeItem.depth = depth;
                    parent.AddChild(compositeGroupTreeItem);
                    continue;
                }
                if (bindingsItem.isPartOfComposite)
                {
                    var compositeItem = new CompositeTreeItem(actionMapName, bindingProperty, j);
                    compositeItem.depth = depth + 1;
                    if (compositeGroupTreeItem != null)
                        compositeGroupTreeItem.AddChild(compositeItem);
                    continue;
                }
                compositeGroupTreeItem = null;
                parent.AddChild(bindingsItem);
            }
        }

        protected override void ContextClicked()
        {
            OnContextClick(null);
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            if (!HasSelection())
                return;
            if (OnSelectionChanged != null)
            {
                OnSelectionChanged();
            }
        }

        protected override float GetCustomRowHeight(int row, TreeViewItem item)
        {
            return 18;
        }

        protected override bool CanRename(TreeViewItem item)
        {
            return item is CompositeGroupTreeItem || item is ActionTreeViewItem && !(item is BindingTreeItem);
        }

        protected override void DoubleClickedItem(int id)
        {
            var item = FindItem(id, rootItem);
            if (item == null)
                return;
            if (item is BindingTreeItem && !(item is CompositeGroupTreeItem))
                return;
            BeginRename(item);
            ((ActionTreeViewItem)item).renaming = true;
        }

        protected override void RenameEnded(RenameEndedArgs args)
        {
            var actionItem = FindItem(args.itemID, rootItem) as ActionTreeViewItem;
            if (actionItem == null)
                return;

            actionItem.renaming = false;

            if (!args.acceptedRename || args.originalName == args.newName)
                return;

            if (actionItem is ActionTreeItem)
            {
                ((ActionTreeItem)actionItem).Rename(args.newName);
            }
            else if (actionItem is ActionMapTreeItem)
            {
                ((ActionMapTreeItem)actionItem).Rename(args.newName);
            }
            else if (actionItem is CompositeGroupTreeItem)
            {
                ((CompositeGroupTreeItem)actionItem).Rename(args.newName);
            }
            else
            {
                Debug.LogAssertion("Cannot rename: " + actionItem);
            }

            var newId = actionItem.GetIdForName(args.newName);
            SetSelection(new[] {newId});
            SetExpanded(newId, IsExpanded(actionItem.id));
            m_ApplyAction();

            actionItem.displayName = args.newName;
            Reload();
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            // We try to predict the indentation
            var indent = (args.item.depth + 2) * 6 + 10;
            var item = (args.item as ActionTreeViewItem);
            if (item != null)
            {
                item.OnGUI(args.rowRect, args.selected, args.focused, indent);
            }
        }

        public TreeViewItem GetRootElement()
        {
            return rootItem;
        }
    }
}
#endif // UNITY_EDITOR
