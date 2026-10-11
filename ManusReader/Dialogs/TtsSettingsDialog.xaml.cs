using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Navigation;
using ManusReader.Models;
using ManusReader.Services;

namespace ManusReader.Dialogs;

public partial class TtsSettingsDialog : Window
{
    public AppSettings Result { get; private set; }

    private CancellationTokenSource? _downloadCts;

    public TtsSettingsDialog(AppSettings current)
    {
        InitializeComponent();
        Result = new AppSettings
        {
            Provider = current.Provider,
            Azure = new AzureConfig
            {
                SubscriptionKey = current.Azure.SubscriptionKey,
                Region = current.Azure.Region,
                Language = current.Azure.Language
            }
        };

        // Populate voice list
        foreach (var (name, display, _) in WindowsTtsService.GetVoices())
            NaturalVoicesList.Items.Add(new VoiceItem(name, display));

        if (NaturalVoicesList.Items.Count == 0)
            NaturalVoicesList.Items.Add(new VoiceItem("", "(Inga röster installerade)"));

        NaturalVoicesList.SelectedIndex = 0;

        RefreshPiperPanel();

        // Pre-fill Azure fields
        AzureKeyBox.Text = current.Azure.SubscriptionKey == "YOUR_KEY" ? "" : current.Azure.SubscriptionKey;
        AzureRegionBox.Text = current.Azure.Region;
        AzureLanguageBox.Text = current.Azure.Language;

        // Set initial radio + panel state
        RadioNatural.IsChecked = current.Provider == TtsProvider.Natural;
        RadioAzure.IsChecked = current.Provider == TtsProvider.Azure;
        RadioPiper.IsChecked = current.Provider == TtsProvider.Piper;
        UpdatePanels();

        Closing += (_, _) => _downloadCts?.Cancel();
    }

    private void Provider_Changed(object sender, RoutedEventArgs e) => UpdatePanels();

    private void UpdatePanels()
    {
        if (NaturalPanel == null || AzurePanel == null || PiperPanel == null) return;
        SetPanelActive(NaturalPanel, RadioNatural.IsChecked == true);
        SetPanelActive(PiperPanel, RadioPiper.IsChecked == true);
        SetPanelActive(AzurePanel, RadioAzure.IsChecked == true);
    }

    private static void SetPanelActive(FrameworkElement panel, bool active)
    {
        panel.IsEnabled = active;
        panel.Opacity = active ? 1.0 : 0.45;
    }

    private async void TestNatural_Click(object sender, RoutedEventArgs e)
    {
        var item = NaturalVoicesList.SelectedItem as VoiceItem;
        if (item == null || string.IsNullOrEmpty(item.Name)) return;

        var profile = new VoiceProfile { NaturalVoiceName = item.Name };
        using var svc = new WindowsTtsService();
        await svc.SpeakAsync("Hej, det här är ett rösttest.", profile);
    }

    private async void TestAzure_Click(object sender, RoutedEventArgs e)
    {
        var key = AzureKeyBox.Text.Trim();
        var region = AzureRegionBox.Text.Trim();
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(region))
        {
            MessageBox.Show("Fyll i nyckel och region innan du testar.",
                "Ofullständigt", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var cfg = new AzureConfig { SubscriptionKey = key, Region = region };
        var svc = new TtsService();
        svc.Configure(cfg);
        var profile = new VoiceProfile { VoiceName = "sv-SE-MattiasNeural" };
        try
        {
            await svc.SpeakAsync("Hej, Azure fungerar!", profile);
        }
        catch
        {
            MessageBox.Show("Kunde inte ansluta till Azure. Kontrollera nyckel och region.",
                "Azure-fel", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { svc.Dispose(); }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (RadioPiper.IsChecked == true && !Piper.IsReady)
        {
            MessageBox.Show("Ladda ner Piper och minst en röst innan du väljer Piper.",
                "Piper", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Without a female (or male) voice those roles would silently get the
        // other gender's voice.
        if (RadioPiper.IsChecked == true && Piper.MissingGenders() is { Count: > 0 } missing)
        {
            var answer = MessageBox.Show(
                $"Ingen {string.Join(" eller ", missing)} är nedladdad för Piper, så de rollerna " +
                "får en röst av fel kön.\n\nVill du ändå fortsätta? (Välj Nej för att ladda ner fler röster.)",
                "Piper", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }

        Result.Provider = RadioPiper.IsChecked == true ? TtsProvider.Piper
                        : RadioAzure.IsChecked == true ? TtsProvider.Azure
                        : TtsProvider.Natural;
        Result.Azure.SubscriptionKey = string.IsNullOrWhiteSpace(AzureKeyBox.Text)
            ? "YOUR_KEY" : AzureKeyBox.Text.Trim();
        Result.Azure.Region = AzureRegionBox.Text.Trim();
        Result.Azure.Language = string.IsNullOrWhiteSpace(AzureLanguageBox.Text)
            ? "sv-SE" : AzureLanguageBox.Text.Trim();
        DialogResult = true;
    }

    private void AzureHelp_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "För att använda Azure Neural TTS behöver du en Azure-prenumeration (konto på portal.azure.com). " +
            "Det är ett krav från start.\n\n" +
            "Så här skapar du en Speech-resurs och hämtar din nyckel:\n\n" +
            "1. Logga in på portal.azure.com\n" +
            "2. Klicka på \"Skapa en resurs\" och sök efter \"Speech\"\n" +
            "3. Välj \"Speech\" från Microsoft och klicka Skapa\n" +
            "4. Välj din prenumeration, en resursgrupp och en region\n" +
            "   (t.ex. Sweden Central för lägst latens från Sverige)\n" +
            "5. Välj prisnivå – F0 är gratis och räcker för test\n" +
            "   (500 000 tecken per månad ingår)\n" +
            "6. Klicka Granska + skapa, sedan Skapa\n" +
            "7. När resursen är klar, gå till den och klicka på\n" +
            "   \"Keys and Endpoint\" i vänstermenyn\n" +
            "8. Kopiera KEY 1 – det är din prenumerationsnyckel\n" +
            "9. Notera värdet under \"Location\" – det är din region\n\n" +
            "Klistra in nyckel och region i fälten ovan och testa anslutningen.",
            "Hur skapar jag en Azure Speech-nyckel?",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ResetAzure_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Är du säker på att du vill återställa Azure-konfigurationen? Dina uppgifter raderas.",
            "Återställ Azure-konfiguration",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes) return;

        AzureKeyBox.Text = "";
        AzureRegionBox.Text = "swedencentral";
        AzureLanguageBox.Text = "sv-SE";
        RadioNatural.IsChecked = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    // ------------------------------------------------------------------ Piper

    private Piper.CatalogVoice? SelectedPiperVoice =>
        (PiperVoicesList.SelectedItem as VoiceItem) is { } item
            ? Piper.Catalog.FirstOrDefault(v => v.Id == item.Name)
            : null;

    private void RefreshPiperPanel()
    {
        var selectedId = SelectedPiperVoice?.Id;

        PiperVoicesList.Items.Clear();
        foreach (var v in Piper.Catalog)
        {
            var state = Piper.IsVoiceInstalled(v.Id) ? "✔ nedladdad" : "ej nedladdad";
            PiperVoicesList.Items.Add(new VoiceItem(v.Id, $"{v.Display}  –  {state}  ·  {v.Credit}, licens {v.License}"));
        }

        var idx = Piper.Catalog.ToList().FindIndex(v => v.Id == selectedId);
        PiperVoicesList.SelectedIndex = idx >= 0 ? idx : 0;

        PiperStatusText.Text = !Piper.IsEngineInstalled
            ? "Piper är inte nedladdat. Välj en röst och klicka på Ladda ner."
            : Piper.InstalledVoices().Count == 0
                ? "Piper är nedladdat. Ladda ner minst en röst."
                : Piper.MissingGenders() is { Count: > 0 } missing
                    ? $"Piper är redo, men ingen {string.Join(" eller ", missing)} är nedladdad."
                    : "Piper är redo.";

        UpdatePiperButtons();
    }

    private void PiperVoicesList_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e) => UpdatePiperButtons();

    private void UpdatePiperButtons()
    {
        var busy = _downloadCts != null;
        var voice = SelectedPiperVoice;
        var installed = voice != null && Piper.IsVoiceInstalled(voice.Id);

        PiperDownloadButton.IsEnabled = !busy && voice != null && (!installed || !Piper.IsEngineInstalled);
        PiperRecommendedButton.IsEnabled = !busy &&
            (!Piper.IsEngineInstalled || Piper.Recommended.Any(id => !Piper.IsVoiceInstalled(id)));
        PiperTestButton.IsEnabled = !busy && installed && Piper.IsEngineInstalled;
        PiperRemoveButton.IsEnabled = !busy && installed;
        PiperVoicesList.IsEnabled = !busy;
    }

    private async void PiperDownload_Click(object sender, RoutedEventArgs e)
    {
        var voice = SelectedPiperVoice;
        if (voice != null) await DownloadPiperAsync(new[] { voice });
    }

    private async void PiperRecommended_Click(object sender, RoutedEventArgs e) =>
        await DownloadPiperAsync(Piper.Catalog.Where(v => Piper.Recommended.Contains(v.Id)).ToList());

    // Downloads the engine (if needed) and the given voices that aren't installed.
    private async Task DownloadPiperAsync(IReadOnlyList<Piper.CatalogVoice> voices)
    {
        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;
        PiperProgress.Visibility = Visibility.Visible;
        UpdatePiperButtons();

        var progress = new Progress<Piper.DownloadProgress>(p =>
        {
            var mb = p.Done / 1048576.0;
            if (p.Total is > 0)
            {
                PiperProgress.IsIndeterminate = false;
                PiperProgress.Value = 100.0 * p.Done / p.Total.Value;
                PiperStatusText.Text = $"Laddar ner {p.What}: {mb:0} av {p.Total.Value / 1048576.0:0} MB";
            }
            else
            {
                PiperProgress.IsIndeterminate = true;
                PiperStatusText.Text = $"Laddar ner {p.What}: {mb:0} MB";
            }
        });

        try
        {
            if (!Piper.IsEngineInstalled)
                await Piper.InstallEngineAsync(progress, ct);
            foreach (var voice in voices.Where(v => !Piper.IsVoiceInstalled(v.Id)))
                await Piper.InstallVoiceAsync(voice, progress, ct);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            MessageBox.Show($"Nedladdningen misslyckades:\n{ex.Message}",
                "Piper", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _downloadCts = null;
            if (IsLoaded)
            {
                PiperProgress.Visibility = Visibility.Collapsed;
                RefreshPiperPanel();
            }
        }

        if (Piper.IsReady) RadioPiper.IsChecked = true;
    }

    private async void PiperTest_Click(object sender, RoutedEventArgs e)
    {
        var voice = SelectedPiperVoice;
        if (voice == null) return;

        PiperTestButton.IsEnabled = false;
        try
        {
            using var svc = new PiperTtsService();
            await svc.SpeakAsync("Hej, det här är ett rösttest med Piper.",
                new VoiceProfile { PiperVoiceName = voice.Id });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Piper-fel: {ex.Message}", "Piper",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { UpdatePiperButtons(); }
    }

    private void PiperRemove_Click(object sender, RoutedEventArgs e)
    {
        var voice = SelectedPiperVoice;
        if (voice == null) return;

        try { Piper.RemoveVoice(voice.Id); }
        catch (Exception ex)
        {
            MessageBox.Show($"Kunde inte ta bort rösten:\n{ex.Message}", "Piper",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        RefreshPiperPanel();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private record VoiceItem(string Name, string Display)
    {
        public override string ToString() => Display;
    }
}
