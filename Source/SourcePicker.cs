using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Aureo;
internal sealed class SourcePicker : Window
{
    readonly Func<string, List<SourceItem>> listSources;
    readonly Func<string, SourceItem, bool> apply;
    readonly ComboBox sources = new() { DisplayMemberPath = "Label", Margin = new Thickness(0, 17, 0, 12), MaxWidth = 294 };
    readonly TextBlock hint = new() { Foreground = new SolidColorBrush(Color.FromRgb(141, 149, 162)), FontSize = 11, Margin = new Thickness(0, 0, 0, 14), MinHeight = 35 };
    readonly Button confirm = new() { Height = 38, Padding = new Thickness(8), Style = (Style)System.Windows.Application.Current.FindResource("GoldButton") };
    readonly Dictionary<string, Button> modes = new();
    string mode;
    readonly bool? recordingWindow;
    internal string Mode => mode;
    internal ComboBox Sources => sources;
    internal Button ConfirmButton => confirm;
    internal bool Excluded { get; private set; }
    public SourcePicker(string currentMode, SourceItem? current, Func<string, List<SourceItem>> listSources, Func<string, SourceItem, bool> apply, bool? recordingWindow = null)
    {
        var textStyle = new Style(typeof(TextBlock));
        textStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.NoWrap));
        textStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        textStyle.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 238.0));
        sources.Resources.Add(typeof(TextBlock), textStyle);
        this.listSources = listSources; this.apply = apply; mode = currentMode; this.recordingWindow = recordingWindow;
        Title = "Amos錄影與截圖 · 選擇來源"; Width = 334; Height = 278;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = Brushes.Transparent; ShowInTaskbar = false; Topmost = true;
        var border = new Border { Background = new SolidColorBrush(Color.FromRgb(19, 21, 25)), BorderBrush = new SolidColorBrush(Color.FromRgb(130, 108, 67)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(19, 15, 19, 17) };
        var content = new StackPanel();
        var header = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new TextBlock { Text = "擷取來源", FontSize = 16, Foreground = new SolidColorBrush(Color.FromRgb(229, 207, 159)), VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Content = "×", Padding = new Thickness(5, 0, 5, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "關閉來源選單" };
        close.Click += (_, _) => Close(); header.Children.Add(close); content.Children.Add(header);
        var row = new Grid();
        foreach (var entry in new[] { ("Screen", "螢幕"), ("Region", "範圍"), ("Window", "視窗") }) {
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var button = new Button { Content = entry.Item2, Padding = new Thickness(0, 10, 0, 10), FontSize = 12, Margin = new Thickness(modes.Count == 0 ? 0 : 5, 0, 0, 0) };
            button.IsEnabled = recordingWindow == null || (entry.Item1 == "Window") == recordingWindow;
            if (!button.IsEnabled) button.ToolTip = "跨螢幕／視窗模式請先停止錄影。";
            Grid.SetColumn(button, modes.Count); modes.Add(entry.Item1, button); row.Children.Add(button);
            button.Click += (_, _) => SelectMode(entry.Item1);
        }
        content.Children.Add(row); content.Children.Add(sources); content.Children.Add(hint); content.Children.Add(confirm);
        sources.SelectionChanged += (_, _) => confirm.IsEnabled = sources.SelectedItem is SourceItem;
        confirm.Click += (_, _) => {
            if (sources.SelectedItem is not SourceItem selected) return;
            Hide();
            if (apply(mode, selected)) Close(); else { Show(); Activate(); }
        };
        border.Child = content; Content = border;
        SourceInitialized += (_, _) => Excluded = Native.Exclude(this);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        SelectMode(mode, current);
    }
    internal void SelectMode(string value, SourceItem? preferred = null)
    {
        if (recordingWindow != null && (value == "Window") != recordingWindow) return;
        mode = value;
        foreach (var entry in modes) {
            bool selected = entry.Key == mode;
            entry.Value.Background = new SolidColorBrush(selected ? Color.FromRgb(55, 46, 29) : Color.FromRgb(32, 35, 41));
            entry.Value.BorderBrush = new SolidColorBrush(selected ? Color.FromRgb(200, 169, 109) : Color.FromRgb(52, 55, 62));
        }
        try {
            var items = listSources(mode); sources.ItemsSource = items;
            sources.SelectedIndex = items.Count == 0 ? -1 : Math.Max(0, items.FindIndex(x => mode == "Window" ? x.Handle == preferred?.Handle : x.Device == preferred?.Device));
            confirm.Content = mode == "Region" ? "框選範圍" : "使用這個來源";
            confirm.IsEnabled = items.Count > 0;
            hint.Text = items.Count == 0 ? "沒有可用來源，請開啟視窗後重新選擇模式。" : mode switch {
                "Region" => "選擇螢幕後拖曳框選。錄影與截圖共用此範圍。",
                "Window" => "選擇應用程式視窗。瀏覽器使用此視窗目前顯示的分頁。",
                _ => "錄影與截圖共用此螢幕，切換程式時會一起擷取。"
            };
        } catch (Exception ex) { sources.ItemsSource = null; confirm.IsEnabled = false; hint.Text = "無法列出來源：" + ex.Message; }
    }
}
