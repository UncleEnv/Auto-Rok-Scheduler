using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoRokScheduler.Models;
using AutoRokScheduler.Services;

namespace AutoRokScheduler.Views;

public partial class ProfileEditWindow : Window
{
    private readonly Profile _model;
    private readonly bool _isNew;

    public ProfileEditWindow(Profile model, string[] existingKeys, bool isNew)
    {
        InitializeComponent();
        _model = model;
        _isNew = isNew;

        Title = isNew ? "Add account" : "Edit account";
        TitleText.Text = Title;

        NameBox.Text = model.Name;
        LoginBox.Text = model.Login;
        DeviceBox.Text = model.DeviceName;
        ProfileKeyBox.Text = model.BrowserProfileKey;
        BrowserCombo.SelectedIndex = model.Browser == BrowserKind.Chrome ? 1 : 0;

        if (!isNew)
        {
            // Prefill the decrypted password so it can be viewed/kept.
            var pw = CredentialProtector.Decrypt(model.EncryptedPassword);
            PassBox.Password = pw;
            PassHint.Visibility = Visibility.Visible;
        }

        if (existingKeys.Length > 0)
            KeyHint.Text = $"Leave blank for its own isolated session. Type the same value as another account to share a login/session.\nExisting: {string.Join(", ", existingKeys)}";

        // Activate() before Focus(): launching msedgedriver/Edge can briefly steal the
        // foreground, and Focus() on a non-foreground window silently does nothing --
        // which looks exactly like a dialog that opened but refuses to accept typing.
        Loaded += (_, _) => { Activate(); NameBox.Focus(); };
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { Error("Account name is required."); return; }

        var login = LoginBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(login)) { Error("Login (email) is required."); return; }

        // Password: required for a brand-new account; optional (keep existing) when editing.
        if (PassBox.Password.Length > 0)
            _model.EncryptedPassword = CredentialProtector.Encrypt(PassBox.Password);
        else if (_isNew)
        { Error("Password is required."); return; }

        _model.Name = name;
        _model.Login = login;
        _model.DeviceName = DeviceBox.Text.Trim();
        _model.BrowserProfileKey = ProfileKeyBox.Text.Trim();
        _model.Browser = (BrowserCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() == "Chrome"
            ? BrowserKind.Chrome : BrowserKind.Edge;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Error(string msg) => ErrorText.Text = msg;
}
