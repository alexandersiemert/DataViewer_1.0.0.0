using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace DataViewer_1._0._0._0
{
    public static class TreeViewManager
    {
        private static Dictionary<string, TreeViewItem> treeViewItems = new Dictionary<string, TreeViewItem>();
        private static TreeView myTreeView;

        public static void Initialize(TreeView treeView)
        {
            myTreeView = treeView;
        }

        public static void AddTreeViewItem(string comPortName, string header)
        {
            if (!treeViewItems.ContainsKey(comPortName))
            {
                TreeViewItem newItem = new TreeViewItem { Header = header};
                myTreeView.Items.Add(newItem);
                treeViewItems[comPortName] = newItem;
            }
        }

        public static void AddSubItem(string parentComPortName, string subItemName)
        {
            if (treeViewItems.TryGetValue(parentComPortName, out TreeViewItem parentItem))
            {
                TreeViewItem subItem = new TreeViewItem { Header = subItemName };
                parentItem.Items.Add(subItem);
            }
        }

        public static TreeViewItem FindTreeViewItem(string comPortName)
        {
            treeViewItems.TryGetValue(comPortName, out TreeViewItem item);
            return item;
        }

        public static void RemoveTreeViewItem(string comPortName)
        {
            if (treeViewItems.TryGetValue(comPortName, out TreeViewItem item))
            {
                myTreeView.Items.Remove(item);
                treeViewItems.Remove(comPortName);
            }
        }

        public static void ClearTreeView()
        {
            myTreeView.Items.Clear();
            treeViewItems.Clear();
        }
    }

}
