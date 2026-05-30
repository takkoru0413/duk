using System.Windows;
using System.Windows.Controls;

namespace duk.App;

public class InputDialog : Window
{
    public string Result { get; private set; } = "";

    public InputDialog(string title, string prompt, string defaultValue = "")
    {
        Title = title;
        Width = 360; Height = 140;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1e1e1e"));

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var label = new TextBlock
        {
            Text = prompt,
            Foreground = System.Windows.Media.Brushes.White,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(label, 0);

        var input = new TextBox
        {
            Text = defaultValue,
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2d2d2d")),
            Foreground = System.Windows.Media.Brushes.White,
            CaretBrush = System.Windows.Media.Brushes.White,
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#454545")),
            BorderThickness = new Thickness(1),
            FontSize = 13,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(input, 1);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetRow(btnPanel, 2);

        var ok = new Button
        {
            Content = "OK", Width = 72, Height = 26, Margin = new Thickness(0, 0, 8, 0),
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0078d4")),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        ok.Click += (_, _) => { Result = input.Text; DialogResult = true; };

        var cancel = new Button
        {
            Content = "キャンセル", Width = 80, Height = 26,
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2d2d2d")),
            Foreground = System.Windows.Media.Brushes.White,
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#454545")),
            BorderThickness = new Thickness(1),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        cancel.Click += (_, _) => { DialogResult = false; };

        input.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };

        btnPanel.Children.Add(ok);
        btnPanel.Children.Add(cancel);
        grid.Children.Add(label);
        grid.Children.Add(input);
        grid.Children.Add(btnPanel);
        Content = grid;

        Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
    }
}
