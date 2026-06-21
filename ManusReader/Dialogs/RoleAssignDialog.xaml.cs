using System.Collections.Generic;
using System.Windows;

namespace ManusReader.Dialogs;

public partial class RoleAssignDialog : Window
{
    public string SelectedRole { get; set; } = "";

    public RoleAssignDialog(string lineText, IEnumerable<string> existingRoles)
    {
        InitializeComponent();
        DataContext = this;
        LinePreview.Text = lineText;
        foreach (var role in existingRoles)
            RoleComboBox.Items.Add(role);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedRole))
        {
            MessageBox.Show("Ange ett rollnamn.", "Ogiltigt val",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) =>
        DialogResult = false;
}
