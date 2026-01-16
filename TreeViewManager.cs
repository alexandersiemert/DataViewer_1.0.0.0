using DataViewer_1._0._0._0;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Diagnostics;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using TreeView = System.Windows.Controls.TreeView;
using System.Windows;
using System.Reflection;

namespace DataViewer_1._0._0._0
{
    public static class TreeViewManager
    {
        // Definiere ein statisches Event um das Plotten von Daten in der Main anzustoßen
        public static event Action<string, int> TriggerPlotData;
        public static event Action<string, string> RequestCommand;

        private static Dictionary<string, TreeViewItem> treeViewItems = new Dictionary<string, TreeViewItem>();
        private static TreeView myTreeView;
        private static TreeViewItem activePlotItem;

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
                newItem.Tag = new { ItemType = "ComPort", Name = comPortName, PortName = comPortName };
                newItem.ContextMenu = CreateItemContextMenu(comPortName);
                // Event-Handler hinzufügen
               // newItem.MouseRightButtonDown += OnTreeViewItemMouseRightButtonClick;
                //newItem.MouseLeftButtonDown += OnTreeViewItemMouseLeftButtonClick;
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
                subItem.Tag = new { ItemType = "SubItem", Name = subItemName, PortName = parentComPortName };
                subItem.ContextMenu = CreateSubItemContextMenu();
                subItem.MouseDoubleClick += OnTreeViewSubItemMouseDoubleClick;
                // Event-Handler hinzufügen
                //subItem.MouseRightButtonDown += OnTreeViewSubItemMouseRightButtonClick;
                //subItem.MouseLeftButtonDown += OnTreeViewItemMouseLeftButtonClick;
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
                if (activePlotItem != null && (item == activePlotItem || item.Items.Contains(activePlotItem)))
                {
                    activePlotItem.FontWeight = FontWeights.Normal;
                    activePlotItem = null;
                }

                myTreeView.Items.Remove(item);
                treeViewItems.Remove(comPortName);
            }
        }

        public static void ClearTreeView()
        {
            myTreeView.Items.Clear();
            treeViewItems.Clear();
            activePlotItem = null;
        }

        public static void SelectSubItem(string portName, int index)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                return;
            }

            if (!treeViewItems.TryGetValue(portName, out TreeViewItem parentItem))
            {
                return;
            }

            if (index < 0 || index >= parentItem.Items.Count)
            {
                return;
            }

            if (parentItem.Items[index] is TreeViewItem subItem)
            {
                parentItem.IsExpanded = true;
                subItem.IsSelected = true;
                subItem.BringIntoView();
                SetActivePlotItem(subItem);
            }
        }

        private static void SetActivePlotItem(TreeViewItem item)
        {
            if (activePlotItem != null)
            {
                activePlotItem.FontWeight = FontWeights.Normal;
            }

            activePlotItem = item;

            if (activePlotItem != null)
            {
                activePlotItem.FontWeight = FontWeights.Bold;
            }
        }

        private static ContextMenu CreateItemContextMenu(string comPortName)
        {
            ContextMenu contextMenu = new ContextMenu();

          
            // Menüpunkt hinzufügen
            MenuItem menuItem1 = new MenuItem { Header = "Read all" };
            menuItem1.Click += (s, e) =>
            {
                RequestCommand?.Invoke(comPortName, "G");
            };

            MenuItem menuItem2 = new MenuItem { Header = "Read last" };
            menuItem2.Click += (s, e) =>
            {
                RequestCommand?.Invoke(comPortName, "S");
            };

            MenuItem menuItem3 = new MenuItem { Header = "Aktion einfügen" };
            menuItem3.Click += (s, e) => { /* Logik für Aktion 3 */ };

            contextMenu.Items.Add(menuItem1);
            contextMenu.Items.Add(menuItem2);
            contextMenu.Items.Add(menuItem3);

            // Weitere Menüpunkte und Logik basierend auf dem itemType hinzufügen

            return contextMenu;
        }

        private static ContextMenu CreateSubItemContextMenu()
        {
            ContextMenu contextMenu = new ContextMenu();

            // Menüpunkt hinzufügen
            MenuItem menuItem1 = new MenuItem { Header = "Plot Data" };
            menuItem1.Click += (s, e) =>
            {
                if (s is MenuItem menuItem)
                {
                    // Finde das ContextMenu des MenuItems
                    ContextMenu _contextMenu = menuItem.Parent as ContextMenu;
                    if (_contextMenu != null)
                    {
                        // Finde das TreeViewItem, das das ContextMenu geöffnet hat
                        TreeViewItem subItem = _contextMenu.PlacementTarget as TreeViewItem;

                        TreeViewItem parentItem = LogicalTreeHelper.GetParent(subItem) as TreeViewItem;
                        if (parentItem != null)
                        {
                            
                            // Event auslösen
                            string portName = null;
                            if (subItem?.Tag != null)
                            {
                                dynamic tag = subItem.Tag;
                                portName = tag.PortName;
                            }
                            if (string.IsNullOrWhiteSpace(portName) && parentItem.Tag != null)
                            {
                                dynamic parentTag = parentItem.Tag;
                                portName = parentTag.PortName ?? parentTag.Name;
                            }

                            TriggerPlotData?.Invoke(portName, parentItem.Items.IndexOf(subItem));
                        }
                    }
                }

            };

            MenuItem menuItem2 = new MenuItem { Header = "Export Data" };
            menuItem2.Click += (s, e) =>
            {
                /* Logik für Aktion 2 */
            };

            MenuItem menuItem3 = new MenuItem { Header = "Aktion einfügen" };
            menuItem3.Click += (s, e) => { /* Logik für Aktion 3 */ };

            contextMenu.Items.Add(menuItem1);
            contextMenu.Items.Add(menuItem2);
            contextMenu.Items.Add(menuItem3);
    
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

        private static void OnTreeViewSubItemMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            if (!(sender is TreeViewItem subItem))
            {
                return;
            }

            TreeViewItem parentItem = LogicalTreeHelper.GetParent(subItem) as TreeViewItem;
            if (parentItem == null)
            {
                return;
            }

            string portName = null;
            if (subItem.Tag != null)
            {
                dynamic tag = subItem.Tag;
                portName = tag.PortName;
            }

            if (string.IsNullOrWhiteSpace(portName) && parentItem.Tag != null)
            {
                dynamic parentTag = parentItem.Tag;
                portName = parentTag.PortName ?? parentTag.Name;
            }

            int index = parentItem.Items.IndexOf(subItem);
            if (index < 0)
            {
                return;
            }

            subItem.IsSelected = true;
            subItem.BringIntoView();
            SetActivePlotItem(subItem);
            TriggerPlotData?.Invoke(portName, index);
            e.Handled = true;
        }

        



    }

}
