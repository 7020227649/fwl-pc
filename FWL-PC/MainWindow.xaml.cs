using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace FwlPc;

public partial class MainWindow : Window
{
    private readonly PolicyManager _policy = new();
    private AppSettings _settings;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _policy.LoadSettings();
        NormalizeSites();
        RefreshUi();
    }

    private void NormalizeSites()
    {
        _settings.AllowedSites = _settings.AllowedSites
            .Select(NormalizeDomain)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
    }

    private static string NormalizeDomain(string input)
    {
        var value = input.Trim().ToLowerInvariant();
        if (value.Length == 0) return string.Empty;

        value = Regex.Replace(value, "^https?://", "");
        value = value.Split('/')[0];
        value = value.Split('?')[0];
        value = value.Trim('.');

        if (value.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            value = value[4..];

        return Regex.IsMatch(value, @"^(?=.{1,253}$)([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$")
            ? value
            : string.Empty;
    }

    private void RefreshUi()
    {
        AllowedSitesList.ItemsSource = null;
        AllowedSitesList.ItemsSource = _settings.AllowedSites;
        _settings.Enabled = _policy.IsBackupPresent() || _settings.Enabled;

        if (_settings.Enabled)
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(34, 197, 94));
            StatusText.Text = "Protection ON";
            ToggleButton.Content = "Disable protection";
            ToggleButton.Background = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            HintText.Text = "All normal websites are blocked in Chrome, Edge and Opera. Only the domains above are allowed.";
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            StatusText.Text = "Protection OFF";
            ToggleButton.Content = "Enable protection";
            ToggleButton.Background = new SolidColorBrush(Color.FromRgb(22, 163, 74));
            HintText.Text = "Add the sites you need, then enable protection. Existing browser policy values are backed up before the first enable.";
        }
    }

    private void AddSite_Click(object sender, RoutedEventArgs e)
    {
        var domain = NormalizeDomain(SiteTextBox.Text);
        if (domain.Length == 0)
        {
            MessageBox.Show(this, "Enter a valid domain, for example google.com or canva.com.", "Invalid website", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_settings.AllowedSites.Contains(domain, StringComparer.OrdinalIgnoreCase))
            _settings.AllowedSites.Add(domain);

        _settings.AllowedSites = _settings.AllowedSites.OrderBy(x => x).ToList();
        _policy.SaveSettings(_settings);
        SiteTextBox.Clear();
        RefreshUi();

        if (_settings.Enabled)
        {
            try
            {
                _policy.Enable(_settings.AllowedSites);
                _policy.SaveSettings(_settings);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not update protection", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void RemoveSite_Click(object sender, RoutedEventArgs e)
    {
        if (AllowedSitesList.SelectedItem is not string domain) return;
        _settings.AllowedSites.RemoveAll(x => string.Equals(x, domain, StringComparison.OrdinalIgnoreCase));
        _policy.SaveSettings(_settings);

        if (_settings.Enabled)
        {
            try { _policy.Enable(_settings.AllowedSites); }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Could not update protection", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        RefreshUi();
    }

    private void ToggleProtection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_settings.Enabled || _policy.IsBackupPresent())
            {
                _policy.Disable();
                _settings.Enabled = false;
                _policy.SaveSettings(_settings);
                MessageBox.Show(this, "Protection disabled. The browser policies saved before FWL PC was enabled have been restored.", "FWL PC", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                if (_settings.AllowedSites.Count == 0)
                {
                    MessageBox.Show(this, "Add at least one allowed website before enabling protection.", "FWL PC", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _policy.Enable(_settings.AllowedSites);
                _settings.Enabled = true;
                _policy.SaveSettings(_settings);
                MessageBox.Show(this, "Protection is active. Chrome, Edge and Opera will allow only the websites in your list.", "FWL PC", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            RefreshUi();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"FWL PC could not change the browser policies.\n\n{ex.Message}", "FWL PC error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
