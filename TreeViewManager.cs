using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;

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

       /* public static void AddTreeViewItem(string comPortName, string header)
        {
            if (!treeViewItems.ContainsKey(comPortName))
            {
                TreeViewItem newItem = new TreeViewItem { Header = header};
                myTreeView.Items.Add(newItem);
                treeViewItems[comPortName] = newItem;
            }
        }*/

        public static void AddTreeViewItem(string comPortName, string header)
        {
            if (!treeViewItems.ContainsKey(comPortName))
            {
                TreeViewItem newItem = new TreeViewItem { Header = header };
                // Speichern von Informationen im Tag
                newItem.Tag = new { ItemType = "ComPort", Name = comPortName };
                // Event-Handler hinzufügen
                newItem.MouseRightButtonDown += OnTreeViewItemMouseRightButtonClick;
                newItem.MouseLeftButtonDown += OnTreeViewItemMouseLeftButtonClick;
                myTreeView.Items.Add(newItem);
                treeViewItems[comPortName] = newItem;
            }
        }


        /*public static void AddSubItem(string parentComPortName, string subItemName)
        {
            if (treeViewItems.TryGetValue(parentComPortName, out TreeViewItem parentItem))
            {
                TreeViewItem subItem = new TreeViewItem { Header = subItemName };
                parentItem.Items.Add(subItem);
            }
        }*/

        public static void AddSubItem(string parentComPortName, string subItemName)
        {
            if (treeViewItems.TryGetValue(parentComPortName, out TreeViewItem parentItem))
            {
                TreeViewItem subItem = new TreeViewItem { Header = subItemName };
                subItem.Tag = new { ItemType = "SubItem", Name = subItemName };
                // Event-Handler hinzufügen
                subItem.MouseRightButtonDown += OnTreeViewItemMouseRightButtonClick;
                subItem.MouseLeftButtonDown += OnTreeViewItemMouseLeftButtonClick;
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

        private static ContextMenu CreateContextMenu(string itemType)
        {
            ContextMenu contextMenu = new ContextMenu();

            // Menüpunkt hinzufügen
            MenuItem menuItem1 = new MenuItem { Header = "Read all" };
            menuItem1.Click += (s, e) => {
                MainWindow.serialPortManager.OpenPort();
                MainWindow.serialPortManager.SendCommand("G"); 
            };

            MenuItem menuItem2 = new MenuItem { Header = "Aktion 2" };
            menuItem2.Click += (s, e) => { /* Logik für Aktion 2 */ };

            contextMenu.Items.Add(menuItem1);
            contextMenu.Items.Add(menuItem2);

            // Weitere Menüpunkte und Logik basierend auf dem itemType hinzufügen

            return contextMenu;
        }


        /*################################ EVENTS ###########################################*/

        public static void OnTreeViewItemMouseLeftButtonClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is TreeViewItem item)
            {
                var tag = item.Tag as dynamic;
                if (tag != null)
                {
                    switch (tag.ItemType)
                    {
                        case "ComPort":
                            // Logik für ComPort-Item
                            break;
                        case "SubItem":
                            // Logik für SubItem
                            break;
                    }
                }
            }
        }

        public static void OnTreeViewItemMouseRightButtonClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is TreeViewItem item)
            {
                var tag = item.Tag as dynamic;
                if (tag != null)
                {
                    switch (tag.ItemType)
                    {
                        case "ComPort":
                            // Logik für ComPort-Item
                            ContextMenu contextMenuItem = CreateContextMenu(tag?.ItemType);
                            contextMenuItem.IsOpen = true;
                            e.Handled = true; // Verhindert, dass das Ereignis weitergeleitet wird
                            break;
                        case "SubItem":
                            // Logik für SubItem
                            ContextMenu contextMenuSubItem = CreateContextMenu(tag?.ItemType);
                            contextMenuSubItem.IsOpen = true;
                            e.Handled = true; // Verhindert, dass das Ereignis weitergeleitet wird
                            break;
                    }
                }
            }
        }


    }

}
