using System.Linq;
using System.Windows;
using ManusReader.Models;
using ManusReader.Services;

namespace ManusReader.Dialogs;

public partial class TtsSettingsDialog : Window
{
    public AppSettings Result { get; private set; }

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

        // Pre-fill Azure fields
        AzureKeyBox.Text = current.Azure.SubscriptionKey == "YOUR_KEY" ? "" : current.Azure.SubscriptionKey;
        AzureRegionBox.Text = current.Azure.Region;
        AzureLanguageBox.Text = current.Azure.Language;

        // Set initial radio + panel state
        RadioNatural.IsChecked = current.Provider == TtsProvider.Natural;
        RadioAzure.IsChecked = current.Provider == TtsProvider.Azure;
        UpdatePanels();
    }

    private void Provider_Changed(object sender, RoutedEventArgs e) => UpdatePanels();

    private void UpdatePanels()
    {
        if (NaturalPanel == null || AzurePanel == null) return;
        var natural = RadioNatural.IsChecked == true;
        NaturalPanel.IsEnabled = natural;
        NaturalPanel.Opacity = natural ? 1.0 : 0.45;
        AzurePanel.IsEnabled = !natural;
        AzurePanel.Opacity = natural ? 0.45 : 1.0;
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
        Result.Provider = RadioNatural.IsChecked == true ? TtsProvider.Natural : TtsProvider.Azure;
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

    private record VoiceItem(string Name, string Display)
    {
        public override string ToString() => Display;
    }
}
