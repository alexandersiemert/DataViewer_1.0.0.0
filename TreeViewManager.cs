using DataViewer_1._0._0._0;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using TreeView = System.Windows.Controls.TreeView;
using System.Windows;
using System.Windows.Media;
using System.Reflection;

namespace DataViewer_1._0._0._0
{
    public static class TreeViewManager
    {
        // Definiere ein statisches Event um das Plotten von Daten in der Main anzustoßen
        public static event Action<string, int> TriggerPlotData;
        public static event Action<string, string> RequestCommand;
        public static event Action<string, bool> SeriesToggleChanged;

        private static Dictionary<string, TreeViewItem> treeViewItems = new Dictionary<string, TreeViewItem>();
        private static TreeView myTreeView;
        private static TreeViewItem activePlotItem;
        private static bool isUpdatingSeriesToggles;
        private static bool seriesAlt = true;
        private static bool seriesTemp = true;
        private static bool seriesAccAbs = true;
        private static bool seriesAccX = true;
        private static bool seriesAccY = true;
        private static bool seriesAccZ = true;

        private sealed class SeriesToggleTag
        {
            public SeriesToggleTag(string key, TreeViewItem parentItem)
            {
                Key = key;
                ParentItem = parentItem;
            }

            public string Key { get; }
            public TreeViewItem ParentItem { get; }
        }

        private static readonly (string Key, string Label)[] SeriesToggleDefinitions = new[]
        {
            ("Alt", "Altitude"),
            ("Temp", "Temperature"),
            ("AccAbs", "3-Axis Acceleration"),
            ("AccX", "Acc X"),
            ("AccY", "Acc Y"),
            ("AccZ", "Acc Z")
        };

        private static readonly Dictionary<string, Brush> SeriesToggleBrushes = new Dictionary<string, Brush>(StringComparer.OrdinalIgnoreCase)
        {
            { "Alt", new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B)) },
            { "Temp", new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)) },
            { "AccAbs", new SolidColorBrush(Color.FromRgb(0xCC, 0x79, 0xA7)) },
            { "AccX", new SolidColorBrush(Color.FromRgb(0x00, 0x72, 0xB2)) },
            { "AccY", new SolidColorBrush(Color.FromRgb(0x00, 0x9E, 0x73)) },
            { "AccZ", new SolidColorBrush(Color.FromRgb(0xE6, 0x9F, 0x00)) }
        };

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
                AddSeriesToggleItems(subItem);
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
                AddSeriesToggleItems(subItem);
                subItem.IsExpanded = true;
                // Event-Handler hinzufügen
                //subItem.MouseRightButtonDown += OnTreeViewSubItemMouseRightButtonClick;
                //subItem.MouseLeftButtonDown += OnTreeViewItemMouseLeftButtonClick;
                parentItem.Items.Add(subItem);
                EnsureSeriesToggleItems(subItem);
                subItem.IsExpanded = true;
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

        public static void SetSeriesStates(bool alt, bool temp, bool accAbs, bool accX, bool accY, bool accZ)
        {
            seriesAlt = alt;
            seriesTemp = temp;
            seriesAccAbs = accAbs;
            seriesAccX = accX;
            seriesAccY = accY;
            seriesAccZ = accZ;
            SyncSeriesToggleStates();
        }

        public static bool TryGetSeriesStates(string portName, int index, out bool alt, out bool temp, out bool accAbs, out bool accX, out bool accY, out bool accZ)
        {
            alt = seriesAlt;
            temp = seriesTemp;
            accAbs = seriesAccAbs;
            accX = seriesAccX;
            accY = seriesAccY;
            accZ = seriesAccZ;

            if (string.IsNullOrWhiteSpace(portName))
            {
                return false;
            }

            if (!treeViewItems.TryGetValue(portName, out TreeViewItem parentItem))
            {
                return false;
            }

            if (index < 0 || index >= parentItem.Items.Count)
            {
                return false;
            }

            TreeViewItem subItem = parentItem.Items[index] as TreeViewItem;
            if (subItem == null)
            {
                return false;
            }

            EnsureSeriesToggleItems(subItem);
            ReadSeriesStates(subItem, ref alt, ref temp, ref accAbs, ref accX, ref accY, ref accZ);
            return true;
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
                EnsureSeriesToggleItems(subItem);
                subItem.IsExpanded = true;
                subItem.IsSelected = true;
                subItem.BringIntoView();
                SetActivePlotItem(subItem);
            }
        }

        private static void EnsureSeriesToggleItems(TreeViewItem subItem)
        {
            if (subItem == null)
            {
                return;
            }

            foreach (object child in subItem.Items)
            {
                if (child is TreeViewItem childItem && childItem.Header is CheckBox)
                {
                    return;
                }
            }

            AddSeriesToggleItems(subItem);
        }

        private static Brush GetSeriesToggleBrush(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return Brushes.Black;
            }

            if (SeriesToggleBrushes.TryGetValue(key, out Brush brush))
            {
                return brush;
            }

            return Brushes.Black;
        }

        private static void AddSeriesToggleItems(TreeViewItem parentItem)
        {
            foreach ((string key, string label) in SeriesToggleDefinitions)
            {
                CheckBox toggle = new CheckBox
                {
                    Content = label,
                    IsChecked = GetSeriesState(key),
                    Foreground = GetSeriesToggleBrush(key),
                    Tag = new SeriesToggleTag(key, parentItem)
                };
                toggle.Checked += SeriesToggle_CheckedChanged;
                toggle.Unchecked += SeriesToggle_CheckedChanged;
                toggle.PreviewMouseDoubleClick += SeriesToggle_PreviewMouseDoubleClick;

                TreeViewItem toggleItem = new TreeViewItem
                {
                    Header = toggle,
                    Tag = new { ItemType = "SeriesToggle", SeriesKey = key }
                };
                parentItem.Items.Add(toggleItem);
            }
        }

        private static string GetSeriesKey(CheckBox toggle)
        {
            if (toggle == null)
            {
                return null;
            }

            if (toggle.Tag is SeriesToggleTag tag)
            {
                return tag.Key;
            }

            return toggle.Tag as string;
        }

        private static void ReadSeriesStates(TreeViewItem subItem, ref bool alt, ref bool temp, ref bool accAbs, ref bool accX, ref bool accY, ref bool accZ)
        {
            if (subItem == null)
            {
                return;
            }

            foreach (object toggleObj in subItem.Items)
            {
                TreeViewItem toggleItem = toggleObj as TreeViewItem;
                if (toggleItem == null)
                {
                    continue;
                }

                CheckBox toggle = toggleItem.Header as CheckBox;
                if (toggle == null)
                {
                    continue;
                }

                string key = GetSeriesKey(toggle);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                bool isChecked = toggle.IsChecked == true;
                switch (key)
                {
                    case "Alt":
                        alt = isChecked;
                        break;
                    case "Temp":
                        temp = isChecked;
                        break;
                    case "AccAbs":
                        accAbs = isChecked;
                        break;
                    case "AccX":
                        accX = isChecked;
                        break;
                    case "AccY":
                        accY = isChecked;
                        break;
                    case "AccZ":
                        accZ = isChecked;
                        break;
                }
            }
        }

        private static bool GetSeriesState(string key)
        {
            switch (key)
            {
                case "Alt":
                    return seriesAlt;
                case "Temp":
                    return seriesTemp;
                case "AccAbs":
                    return seriesAccAbs;
                case "AccX":
                    return seriesAccX;
                case "AccY":
                    return seriesAccY;
                case "AccZ":
                    return seriesAccZ;
                default:
                    return true;
            }
        }

        private static void SetSeriesState(string key, bool value)
        {
            switch (key)
            {
                case "Alt":
                    seriesAlt = value;
                    break;
                case "Temp":
                    seriesTemp = value;
                    break;
                case "AccAbs":
                    seriesAccAbs = value;
                    break;
                case "AccX":
                    seriesAccX = value;
                    break;
                case "AccY":
                    seriesAccY = value;
                    break;
                case "AccZ":
                    seriesAccZ = value;
                    break;
            }
        }

        private static void SeriesToggle_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (isUpdatingSeriesToggles)
            {
                return;
            }

            CheckBox toggle = sender as CheckBox;
            if (toggle == null)
            {
                return;
            }

            SeriesToggleTag tag = toggle.Tag as SeriesToggleTag;
            if (tag == null || tag.ParentItem == null || tag.ParentItem != activePlotItem)
            {
                return;
            }

            string key = tag.Key;
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            bool isChecked = toggle.IsChecked == true;
            SetSeriesState(key, isChecked);
            SeriesToggleChanged?.Invoke(key, isChecked);
        }

        private static void SeriesToggle_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private static void SyncSeriesToggleStates()
        {
            if (myTreeView == null || activePlotItem == null)
            {
                return;
            }

            isUpdatingSeriesToggles = true;
            EnsureSeriesToggleItems(activePlotItem);
            foreach (object toggleObj in activePlotItem.Items)
            {
                TreeViewItem toggleItem = toggleObj as TreeViewItem;
                if (toggleItem == null)
                {
                    continue;
                }

                CheckBox toggle = toggleItem.Header as CheckBox;
                if (toggle == null)
                {
                    continue;
                }

                string key = GetSeriesKey(toggle);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                toggle.IsChecked = GetSeriesState(key);
            }
            isUpdatingSeriesToggles = false;
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
            subItem.IsExpanded = true;
            subItem.BringIntoView();
            SetActivePlotItem(subItem);
            EnsureSeriesToggleItems(subItem);
            TriggerPlotData?.Invoke(portName, index);
            e.Handled = true;
        }

        



    }

}
