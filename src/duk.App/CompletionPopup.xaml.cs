using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using duk.Core.Lsp;

namespace duk.App;

public partial class CompletionPopup : Popup
{
    public event EventHandler<CompletionItem>? ItemAccepted;

    public CompletionPopup()
    {
        InitializeComponent();
    }

    public void Show(IEnumerable<CompletionItem> items, double x, double y)
    {
        ItemsList.ItemsSource = items.Take(50).ToList();
        if (ItemsList.Items.Count == 0) { IsOpen = false; return; }

        ItemsList.SelectedIndex = 0;
        HorizontalOffset = x;
        VerticalOffset    = y;
        IsOpen            = true;
        ItemsList.Focus();
    }

    public void Hide() => IsOpen = false;

    public bool IsVisible => IsOpen;

    public void SelectNext()
    {
        if (ItemsList.SelectedIndex < ItemsList.Items.Count - 1)
            ItemsList.SelectedIndex++;
        ItemsList.ScrollIntoView(ItemsList.SelectedItem);
    }

    public void SelectPrev()
    {
        if (ItemsList.SelectedIndex > 0)
            ItemsList.SelectedIndex--;
        ItemsList.ScrollIntoView(ItemsList.SelectedItem);
    }

    public void AcceptSelected()
    {
        if (ItemsList.SelectedItem is CompletionItem item)
        {
            IsOpen = false;
            ItemAccepted?.Invoke(this, item);
        }
    }

    private void ItemsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ItemsList.SelectedItem is CompletionItem item && !string.IsNullOrEmpty(item.Detail))
        {
            DetailText.Text  = item.Detail;
            DetailBorder.Visibility = Visibility.Visible;
        }
        else
        {
            DetailBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void ItemsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Tab)
        {
            AcceptSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    private void ItemsList_DoubleClick(object sender, MouseButtonEventArgs e) =>
        AcceptSelected();
}
