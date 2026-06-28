using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace ManusReader.Dialogs;

public partial class RoleAssignDialog : Window
{
    private const string NewRoleSentinel = "⊕ Ny roll...";

    public string SelectedRole { get; set; } = "";

    public RoleAssignDialog(string lineText, IEnumerable<string> existingRoles)
    {
        InitializeComponent();
        DataContext = this;
        LinePreview.Text = lineText;

        RoleComboBox.Items.Add(NewRoleSentinel);
        foreach (var role in existingRoles)
            RoleComboBox.Items.Add(role);

        RoleComboBox.SelectionChanged += RoleComboBox_SelectionChanged;
    }

    private void RoleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RoleComboBox.SelectedItem as string == NewRoleSentinel)
        {
            RoleComboBox.SelectedIndex = -1;
            RoleComboBox.Text = "";
            SelectedRole = "";
            RoleComboBox.Focus();
        }
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
