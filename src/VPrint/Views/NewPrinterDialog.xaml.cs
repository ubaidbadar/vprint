using System.Windows;
using VPrint.Services;

namespace VPrint.Views;

public partial class NewPrinterDialog : Window
{
    public string PrinterLabel { get; private set; } = "";

    public NewPrinterDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => { PrinterNameInput.Focus(); PrinterNameInput.SelectAll(); };
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PrinterLabel = PrinterManager.ValidateLabel(PrinterNameInput.Text);
            DialogResult = true;
        }
        catch (ArgumentException ex)
        {
            ErrorText.Text = ex.Message;
            PrinterNameInput.Focus();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
