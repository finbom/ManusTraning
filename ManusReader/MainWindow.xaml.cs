using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Newtonsoft.Json;
using ManusReader.Dialogs;
using ManusReader.Models;
using ManusReader.Services;

namespace ManusReader
{
    public partial class MainWindow : Window
    {
        // ------------------------------------------------------------------ state
        private readonly ObservableCollection<ScriptLine> _scriptLines = new();
        private readonly ObservableCollection<VoiceProfile> _voiceProfiles = new();
        private readonly OllamaScriptParser _ollamaParser = new();
        private VoiceProfile? _selectedProfile;
        private int _currentLineIndex = -1;
        private bool _isPlaying;
        private int _speechVersion;
        private bool _suppressProfileUpdate;
        private string? _currentProjectPath;
        private string? _currentScriptPath;
        private AppSettings _settings = new();
        private ITtsService _ttsService = new WindowsTtsService();

        // ------------------------------------------------------------------ AI progress
        private readonly System.Windows.Threading.DispatcherTimer _aiTimer =
            new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly System.Diagnostics.Stopwatch _aiStopwatch = new();
        private int _aiCompleted;
        private int _aiTotal;

        // ------------------------------------------------------------------ voice catalog (dynamic)
        private static readonly List<(string Name, string Display, VoiceGender Gender)> AzureVoices = new()
        {
            ("sv-SE-MattiasNeural",  "Mattias – Man (SV)",        VoiceGender.Man),
            ("sv-SE-SofieNeural",    "Sofie – Kvinna (SV)",       VoiceGender.Kvinna),
            ("sv-SE-HilleviNeural",  "Hillevi – Kvinna (SV)",     VoiceGender.Kvinna),
            ("en-US-GuyNeural",      "Guy – Man (EN-US)",         VoiceGender.Man),
            ("en-US-JennyNeural",    "Jenny – Kvinna (EN-US)",    VoiceGender.Kvinna),
            ("en-US-AriaNeural",     "Aria – Kvinna (EN-US)",     VoiceGender.Kvinna),
            ("en-US-AnaNeural",      "Ana – Barn (EN-US)",        VoiceGender.Barn),
            ("en-GB-RyanNeural",     "Ryan – Man (EN-GB)",        VoiceGender.Man),
            ("en-GB-SoniaNeural",    "Sonia – Kvinna (EN-GB)",    VoiceGender.Kvinna),
            ("de-DE-ConradNeural",   "Conrad – Man (DE)",         VoiceGender.Man),
            ("de-DE-KatjaNeural",    "Katja – Kvinna (DE)",       VoiceGender.Kvinna),
            ("fr-FR-HenriNeural",    "Henri – Man (FR)",          VoiceGender.Man),
            ("fr-FR-DeniseNeural",   "Denise – Kvinna (FR)",      VoiceGender.Kvinna),
        };

        private List<(string Name, string Display, VoiceGender Gender)> _voices = new();

        // ================================================================== constructor
        public MainWindow()
        {
            InitializeComponent();

            ScriptDataGrid.ItemsSource = _scriptLines;
            RolesListBox.ItemsSource = _voiceProfiles;

            LoadSettings();
            ApplySettings();

            _aiTimer.Tick += (_, _) => RefreshAiProgressText();
        }

        // ================================================================== AI progress overlay
        private void ShowAiProgress()
        {
            _aiCompleted = 0;
            _aiTotal = 0;
            _aiStopwatch.Restart();
            AiProgressBar.IsIndeterminate = true;
            AiProgressBar.Value = 0;
            AiProgressStep.Text = "Förbereder analys…";
            AiProgressPercent.Text = "";
            AiProgressEta.Text = "";
            AiProgressElapsed.Text = "";
            AiProgressOverlay.Visibility = Visibility.Visible;
            MainContentGrid.IsEnabled = false;
            _aiTimer.Start();
        }

        private void SetAiProgress(int completed, int total)
        {
            _aiCompleted = completed;
            _aiTotal = total;
            if (total > 0)
            {
                AiProgressBar.IsIndeterminate = false;
                AiProgressBar.Maximum = total;
                AiProgressBar.Value = completed;
            }
            RefreshAiProgressText();
        }

        private void HideAiProgress()
        {
            _aiTimer.Stop();
            _aiStopwatch.Stop();
            AiProgressOverlay.Visibility = Visibility.Collapsed;
            MainContentGrid.IsEnabled = true;
        }

        private void RefreshAiProgressText()
        {
            var elapsed = _aiStopwatch.Elapsed;
            AiProgressElapsed.Text = $"Förfluten tid: {FormatDuration(elapsed)}";

            if (_aiTotal <= 0) return;

            var pct = (double)_aiCompleted / _aiTotal * 100;
            AiProgressPercent.Text = $"{pct:F0}%";
            AiProgressStep.Text =
                $"Analyserar del {Math.Min(_aiCompleted + 1, _aiTotal)} av {_aiTotal}…";

            if (_aiCompleted > 0)
            {
                var perChunk = elapsed.TotalSeconds / _aiCompleted;
                var remaining = TimeSpan.FromSeconds(perChunk * (_aiTotal - _aiCompleted));
                AiProgressEta.Text = remaining.TotalSeconds < 1
                    ? "Klar strax…"
                    : $"Cirka {FormatDuration(remaining)} kvar";
            }
            else
            {
                AiProgressEta.Text = "Beräknar tid kvar…";
            }
        }

        private static string FormatDuration(TimeSpan t) =>
            t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
                : $"{t.Minutes}:{t.Seconds:D2}";

        // Shows a popup summarising how the AI import went, including any problems.
        private void ShowImportSummary(string fileName, OllamaScriptParser.ParseStats s)
        {
            var problems = s.FailedChunks > 0 || s.UnknownLines > 0;

            var msg =
                $"Manus: {fileName}\n" +
                $"AI-modell: {_ollamaParser.Model}\n" +
                $"\n" +
                $"• Repliker/rader inlästa: {s.ParsedLines}\n" +
                $"• Roller identifierade: {s.Roles}\n" +
                $"• Textstycken analyserade: {s.Chunks}\n";

            if (s.UnknownLines > 0)
                msg += $"• Okända rader (orange, behöver roll): {s.UnknownLines}\n";

            if (s.FailedChunks > 0)
                msg +=
                    $"\n⚠ {s.FailedChunks} stycke(n) kunde AI:n inte tolka och räddades\n" +
                    $"   med regelbaserad tolkning ({s.SalvagedLines} rader).\n" +
                    $"   Dessa kan behöva ses över.\n";

            msg += "\nTips: ser resultatet fel ut kan \"Öppna Word-manus\" " +
                   "(utan AI) ge ett stabilare resultat.";

            MessageBox.Show(
                msg,
                problems ? "Import klar – med anmärkningar" : "Import klar",
                MessageBoxButton.OK,
                problems ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }

        // ================================================================== settings
        private string SettingsPath =>
            Path.Combine(
                Path.GetDirectoryName(Environment.ProcessPath)
                    ?? AppDomain.CurrentDomain.BaseDirectory,
                "settings.json");

        private void LoadSettings()
        {
            // Migrate legacy azure-config.json if settings.json doesn't exist
            var legacyPath = Path.Combine(
                Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory,
                "azure-config.json");

            if (!File.Exists(SettingsPath) && File.Exists(legacyPath))
            {
                try
                {
                    var legacy = JsonConvert.DeserializeObject<AzureConfig>(File.ReadAllText(legacyPath));
                    if (legacy != null) _settings.Azure = legacy;
                }
                catch { }
            }
            else if (File.Exists(SettingsPath))
            {
                try
                {
                    _settings = JsonConvert.DeserializeObject<AppSettings>(
                        File.ReadAllText(SettingsPath)) ?? new AppSettings();
                }
                catch { }
            }
        }

        private void SaveSettings()
        {
            try { File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(_settings, Formatting.Indented)); }
            catch { }
        }

        private void ApplySettings()
        {
            _ttsService.Dispose();

            if (_settings.Provider == TtsProvider.Azure)
            {
                var svc = new TtsService();
                svc.Configure(_settings.Azure);
                _ttsService = svc;
                SetStatus("Azure Neural TTS aktiverat");
            }
            else
            {
                _ttsService = new WindowsTtsService();
                SetStatus("Naturliga Windows-röster aktiverade");
            }

            RefreshVoiceList();
        }

        private void RefreshVoiceList()
        {
            _voices = _settings.Provider == TtsProvider.Natural
                ? WindowsTtsService.GetVoices()
                : AzureVoices;

            _suppressProfileUpdate = true;
            VoiceComboBox.ItemsSource = _voices.Select(v => v.Display).ToList();
            _suppressProfileUpdate = false;

            RefreshProfileEditor();
        }

        private void OpenTtsSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TtsSettingsDialog(_settings) { Owner = this };
            if (dialog.ShowDialog() != true) return;

            _settings = dialog.Result;
            SaveSettings();
            ApplySettings();
        }

        // ================================================================== file operations
        private void OpenScript_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Textfiler (*.txt)|*.txt|Alla filer (*.*)|*.*",
                Title = "Öppna manus"
            };
            if (dlg.ShowDialog() == true) LoadScript(dlg.FileName);
        }

        private void LoadScript(string filePath)
        {
            try
            {
                var (lines, _) = ScriptParser.Parse(filePath);
                _currentScriptPath = filePath;
                _currentLineIndex = -1;

                _scriptLines.Clear();
                foreach (var l in lines) _scriptLines.Add(l);

                SyncVoiceProfiles();

                Title = $"ManusReader – {Path.GetFileName(filePath)}";
                SetStatus($"Manus öppnat: {lines.Count} repliker, {_voiceProfiles.Count} roller");
                UpdateProgress();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kunde inte öppna filen:\n{ex.Message}",
                    "Fel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Ensures every unique, non-direction role in script lines has a voice profile,
        // and removes profiles for roles that are no longer used.
        private void SyncVoiceProfiles()
        {
            var rolesInScript = _scriptLines
                .Where(l => !string.IsNullOrWhiteSpace(l.RoleName)
                         && !l.IsStageDirection
                         && !l.IsUnknown)
                .Select(l => l.RoleName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var existing = new HashSet<string>(
                _voiceProfiles.Select(p => p.RoleName), StringComparer.OrdinalIgnoreCase);

            foreach (var role in rolesInScript.Where(r => !existing.Contains(r)))
                _voiceProfiles.Add(CreateDefaultProfile(role, _voiceProfiles.Count));

            var inScript = new HashSet<string>(rolesInScript, StringComparer.OrdinalIgnoreCase);
            var toRemove = _voiceProfiles.Where(p => !inScript.Contains(p.RoleName)).ToList();
            foreach (var p in toRemove) _voiceProfiles.Remove(p);
        }

        private VoiceProfile CreateDefaultProfile(string roleName, int index)
        {
            // Guess gender from the role name; fall back to alternating if unknown.
            var gender = NameGender.Guess(roleName)
                         ?? (index % 2 == 0 ? VoiceGender.Man : VoiceGender.Kvinna);

            // Always prefer a Swedish voice — the script is read aloud in Swedish
            // even when a character has a foreign (e.g. English) name. Only fall
            // back to non-Swedish voices if no Swedish voice of that gender exists.
            var sameGender = _voices.Where(v => v.Gender == gender).ToList();
            var swedishSameGender = sameGender.Where(IsSwedishVoice).ToList();
            var pool = swedishSameGender.Count > 0 ? swedishSameGender
                     : sameGender.Count > 0 ? sameGender
                     : _voices;
            var voice = pool.Count > 0 ? pool[index % pool.Count] : default;

            var profile = new VoiceProfile { RoleName = roleName, Gender = gender };

            // Gender-aware Swedish fallback if the voice catalog is empty.
            var defaultAzure = gender == VoiceGender.Kvinna
                ? "sv-SE-SofieNeural" : "sv-SE-MattiasNeural";

            if (_settings.Provider == TtsProvider.Natural)
                profile.NaturalVoiceName = voice.Name ?? WindowsTtsService.DefaultVoiceName();
            else
                profile.VoiceName = voice.Name ?? defaultAzure;

            return profile;
        }

        // True if a voice is Swedish (Azure names start with "sv-"; Windows voices
        // carry their culture in the display name, e.g. "Microsoft Bengt (SV)").
        private static bool IsSwedishVoice((string Name, string Display, VoiceGender Gender) v) =>
            (v.Name?.StartsWith("sv-", StringComparison.OrdinalIgnoreCase) ?? false)
            || (v.Display?.Contains("(SV)", StringComparison.OrdinalIgnoreCase) ?? false)
            || (v.Display?.Contains("swedish", StringComparison.OrdinalIgnoreCase) ?? false)
            || (v.Name?.Contains("swedish", StringComparison.OrdinalIgnoreCase) ?? false);

        private string ActiveVoiceName(VoiceProfile p) =>
            _settings.Provider == TtsProvider.Natural ? p.NaturalVoiceName : p.VoiceName;

        private void SetActiveVoiceName(VoiceProfile p, string name)
        {
            if (_settings.Provider == TtsProvider.Natural) p.NaturalVoiceName = name;
            else p.VoiceName = name;
        }

        // ================================================================== Word import (rule-based)
        private void OpenWord_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Word-dokument (*.docx)|*.docx|Alla filer (*.*)|*.*",
                Title = "Öppna Word-manus"
            };
            if (dlg.ShowDialog() != true) return;

            SetStatus($"Läser in {Path.GetFileName(dlg.FileName)}...");
            try
            {
                var (lines, roles) = WordScriptParser.Parse(dlg.FileName);

                if (lines.Count == 0)
                {
                    MessageBox.Show("Inga repliker hittades i dokumentet.\n\n" +
                        "Kontrollera att karaktärsnamnen är i VERSALER.",
                        "Inget hittades", MessageBoxButton.OK, MessageBoxImage.Information);
                    SetStatus("Inga repliker hittades");
                    return;
                }

                _currentScriptPath = dlg.FileName;
                _currentLineIndex = -1;
                _scriptLines.Clear();
                foreach (var l in lines) _scriptLines.Add(l);

                SyncVoiceProfiles();

                Title = $"ManusReader – {Path.GetFileName(dlg.FileName)}";
                SetStatus($"Klar! {lines.Count} repliker, {_voiceProfiles.Count} roller inlästa.");
                UpdateProgress();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kunde inte läsa Word-filen:\n{ex.Message}",
                    "Fel", MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("Fel vid inläsning");
            }
        }

        // ================================================================== Word + AI import
        private async void OpenWordWithAI_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Word-dokument (*.docx)|*.docx|Alla filer (*.*)|*.*",
                Title = "Öppna Word-manus"
            };
            if (dlg.ShowDialog() != true) return;

            // Check that Ollama is running
            SetStatus("Kontrollerar Ollama...");
            if (!await _ollamaParser.IsAvailableAsync())
            {
                MessageBox.Show(
                    "Ollama verkar inte köra.\n\n" +
                    "Starta Ollama (ollama serve) och se till att modellen är nedladdad.\n" +
                    "Kör setup-ollama.bat i projektmappen om du inte installerat det ännu.",
                    "Ollama ej tillgänglig", MessageBoxButton.OK, MessageBoxImage.Warning);
                SetStatus("Ollama ej tillgänglig");
                return;
            }

            // Pick best available model
            var models = await _ollamaParser.GetAvailableModelsAsync();
            if (models.Count == 0)
            {
                MessageBox.Show(
                    "Inga modeller hittades i Ollama.\n\n" +
                    "Ladda ner en modell med:\n  ollama pull llama3.2",
                    "Ingen modell", MessageBoxButton.OK, MessageBoxImage.Warning);
                SetStatus("Ingen Ollama-modell hittades");
                return;
            }

            // Prefer llama3.2 or llama3.1, otherwise first available
            _ollamaParser.Model = models.FirstOrDefault(m =>
                m.StartsWith("llama3.2") || m.StartsWith("llama3.1")) ?? models[0];

            string rawText;
            try
            {
                SetStatus($"Läser in Word-dokument: {Path.GetFileName(dlg.FileName)}...");
                rawText = WordReader.ReadText(dlg.FileName);
                if (string.IsNullOrWhiteSpace(rawText))
                {
                    MessageBox.Show("Dokumentet verkar tomt.", "Tomt dokument",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    SetStatus("Dokumentet är tomt");
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kunde inte läsa Word-filen:\n{ex.Message}",
                    "Fel", MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("Fel vid läsning av Word-fil");
                return;
            }

            // Show progress overlay during parsing
            ShowAiProgress();
            try
            {
                var progress = new Progress<OllamaScriptParser.ParseProgress>(p =>
                    Dispatcher.Invoke(() =>
                    {
                        SetAiProgress(p.Completed, p.Total);
                        SetStatus($"AI analyserar: del {Math.Min(p.Completed + 1, p.Total)} av {p.Total}…");
                    }));
                var result = await _ollamaParser.ParseAsync(rawText, progress);
                var lines = result.Lines;

                _currentScriptPath = dlg.FileName;
                _currentLineIndex = -1;
                _scriptLines.Clear();
                foreach (var l in lines) _scriptLines.Add(l);

                SyncVoiceProfiles();

                Title = $"ManusReader – {Path.GetFileName(dlg.FileName)} (AI)";

                var unknownCount = lines.Count(l => l.IsUnknown);
                if (unknownCount > 0)
                    SetStatus($"Klar! {lines.Count} rader, {_voiceProfiles.Count} roller. " +
                              $"{unknownCount} okända rader (orange) – högerklicka för att tilldela roll.");
                else
                    SetStatus($"Klar! {lines.Count} repliker, {_voiceProfiles.Count} roller inlästa.");

                UpdateProgress();
                HideAiProgress();
                ShowImportSummary(Path.GetFileName(dlg.FileName), result.Stats);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"AI-analys misslyckades:\n{ex.Message}",
                    "Fel", MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("AI-analys misslyckades");
            }
            finally
            {
                HideAiProgress();
            }
        }

        // ================================================================== assign unknown role
        private void AssignRole_Click(object sender, RoutedEventArgs e)
        {
            if (ScriptDataGrid.SelectedItem is not ScriptLine line) return;

            var existingRoles = _voiceProfiles
                .Select(p => p.RoleName)
                .OrderBy(r => r)
                .ToList();

            var dialog = new RoleAssignDialog(line.Text, existingRoles) { Owner = this };
            if (dialog.ShowDialog() != true) return;

            var newRole = dialog.SelectedRole.Trim().ToUpper();

            line.RoleName = newRole;
            line.IsUnknown = false;
            line.ShouldRead = true;

            SyncVoiceProfiles();

            var remaining = _scriptLines.Count(l => l.IsUnknown);
            SetStatus(remaining > 0
                ? $"Roll tilldelad. {remaining} okända rader kvar."
                : "Alla rader har nu en roll tilldelad.");
        }

        private void RenameRoleFromGrid_Click(object sender, RoutedEventArgs e)
        {
            if (ScriptDataGrid.SelectedItem is not ScriptLine line) return;
            var oldName = line.RoleName;
            if (string.IsNullOrWhiteSpace(oldName)) return;

            var newName = ShowRenameDialog(oldName);
            if (newName == null || newName == oldName) return;

            // Update all lines that share the old role name
            foreach (var l in _scriptLines.Where(l =>
                string.Equals(l.RoleName, oldName, StringComparison.OrdinalIgnoreCase)))
            {
                l.RoleName = newName;
                l.IsUnknown = false;
            }

            // Rename or merge voice profile
            var oldProfile = _voiceProfiles.FirstOrDefault(p =>
                string.Equals(p.RoleName, oldName, StringComparison.OrdinalIgnoreCase));
            var newProfile = _voiceProfiles.FirstOrDefault(p =>
                string.Equals(p.RoleName, newName, StringComparison.OrdinalIgnoreCase));

            if (oldProfile != null)
            {
                if (newProfile == null)
                    oldProfile.RoleName = newName;
                else
                    _voiceProfiles.Remove(oldProfile);
            }

            SyncVoiceProfiles();
            SetStatus($"Roll omdöpt: {oldName} → {newName}");
        }

        private void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "ManusReader Projekt (*.mrp)|*.mrp|Alla filer (*.*)|*.*",
                Title = "Öppna projekt",
                InitialDirectory = _currentProjectPath != null
                    ? Path.GetDirectoryName(_currentProjectPath)
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dlg.ShowDialog() == true) LoadProject(dlg.FileName);
        }

        private void LoadProject(string filePath)
        {
            try
            {
                var cfg = JsonConvert.DeserializeObject<ProjectConfig>(
                    File.ReadAllText(filePath)) ?? new ProjectConfig();

                _currentProjectPath = filePath;

                // Load the script file
                if (!string.IsNullOrEmpty(cfg.ScriptFilePath) && File.Exists(cfg.ScriptFilePath))
                {
                    var (lines, _) = ScriptParser.Parse(cfg.ScriptFilePath);
                    _currentScriptPath = cfg.ScriptFilePath;
                    _scriptLines.Clear();
                    foreach (var l in lines) _scriptLines.Add(l);
                }

                // Restore voice profiles
                _voiceProfiles.Clear();
                foreach (var pd in cfg.VoiceProfiles)
                {
                    _voiceProfiles.Add(new VoiceProfile
                    {
                        RoleName = pd.RoleName,
                        VoiceName = pd.VoiceName,
                        Gender = (VoiceGender)pd.Gender,
                        Rate = pd.Rate,
                        Pitch = pd.Pitch
                    });
                }

                // Restore checkbox states
                foreach (var line in _scriptLines)
                {
                    if (cfg.LineReadStates.TryGetValue(line.LineNumber, out bool val))
                        line.ShouldRead = val;
                }

                _currentLineIndex = -1;
                Title = $"ManusReader – {Path.GetFileName(filePath)}";
                SetStatus("Projekt öppnat");
                UpdateProgress();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kunde inte öppna projektet:\n{ex.Message}",
                    "Fel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveProject_Click(object sender, RoutedEventArgs e)
        {
            if (_currentProjectPath == null) SaveProjectAs_Click(sender, e);
            else SaveProject(_currentProjectPath);
        }

        private void SaveProjectAs_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "ManusReader Projekt (*.mrp)|*.mrp",
                Title = "Spara projekt som",
                InitialDirectory = _currentProjectPath != null
                    ? Path.GetDirectoryName(_currentProjectPath)
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };
            if (dlg.ShowDialog() == true)
            {
                _currentProjectPath = dlg.FileName;
                SaveProject(dlg.FileName);
            }
        }

        private void SaveProject(string filePath)
        {
            try
            {
                var cfg = new ProjectConfig
                {
                    ScriptFilePath = _currentScriptPath ?? "",
                    VoiceProfiles = _voiceProfiles.Select(p => new VoiceProfileData
                    {
                        RoleName = p.RoleName,
                        VoiceName = p.VoiceName,
                        Gender = (int)p.Gender,
                        Rate = p.Rate,
                        Pitch = p.Pitch
                    }).ToList(),
                    LineReadStates = _scriptLines.ToDictionary(l => l.LineNumber, l => l.ShouldRead)
                };
                File.WriteAllText(filePath, JsonConvert.SerializeObject(cfg, Formatting.Indented));
                SetStatus($"Projekt sparat: {Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kunde inte spara:\n{ex.Message}",
                    "Fel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => Close();

        // ================================================================== voice profile editor
        private void RolesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedProfile = RolesListBox.SelectedItem as VoiceProfile;
            RefreshProfileEditor();
        }

        private void RefreshProfileEditor()
        {
            if (_selectedProfile == null)
            {
                SelectedRoleLabel.Text = "— välj en roll ovan —";
                VoiceProfileEditor.IsEnabled = false;
                return;
            }

            _suppressProfileUpdate = true;
            VoiceProfileEditor.IsEnabled = true;
            SelectedRoleLabel.Text = $"Roll: {_selectedProfile.RoleName}";

            var idx = _voices.FindIndex(v => v.Name == ActiveVoiceName(_selectedProfile));
            VoiceComboBox.SelectedIndex = idx >= 0 ? idx : 0;

            GenderMan.IsChecked = _selectedProfile.Gender == VoiceGender.Man;
            GenderKvinna.IsChecked = _selectedProfile.Gender == VoiceGender.Kvinna;
            GenderBarn.IsChecked = _selectedProfile.Gender == VoiceGender.Barn;

            TempoSlider.Value = _selectedProfile.Rate;
            PitchSlider.Value = _selectedProfile.Pitch;
            TempoValueLabel.Text = FormatPct(_selectedProfile.Rate);
            PitchValueLabel.Text = FormatPct(_selectedProfile.Pitch);

            _suppressProfileUpdate = false;
        }

        private void RolesListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_selectedProfile != null) DoRenameRole();
        }

        private void RenameRole_Click(object sender, RoutedEventArgs e) => DoRenameRole();

        private void DoRenameRole()
        {
            if (_selectedProfile == null) return;

            var oldName = _selectedProfile.RoleName;
            var newName = ShowRenameDialog(oldName);
            if (newName == null || newName == oldName) return;

            // If new name already exists, merge by removing the old profile
            var conflicting = _voiceProfiles.FirstOrDefault(p =>
                string.Equals(p.RoleName, newName, StringComparison.OrdinalIgnoreCase)
                && p != _selectedProfile);
            if (conflicting != null)
                _voiceProfiles.Remove(conflicting);

            _selectedProfile.RoleName = newName;

            foreach (var line in _scriptLines.Where(l =>
                string.Equals(l.RoleName, oldName, StringComparison.OrdinalIgnoreCase)))
                line.RoleName = newName;

            RefreshProfileEditor();
            SetStatus($"Roll omdöpt: {oldName} → {newName}");
        }

        private string? ShowRenameDialog(string currentName)
        {
            string? result = null;

            var tb = new TextBox
            {
                Text = currentName,
                Margin = new Thickness(0, 0, 0, 12),
                SelectionStart = 0,
                SelectionLength = currentName.Length
            };

            var ok = new Button
            {
                Content = "OK",
                Width = 75,
                IsDefault = true,
                Margin = new Thickness(0, 0, 8, 0)
            };
            var cancel = new Button { Content = "Avbryt", Width = 75, IsCancel = true };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var stack = new StackPanel { Margin = new Thickness(16) };
            stack.Children.Add(new TextBlock { Text = "Nytt rollnamn:", Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(tb);
            stack.Children.Add(buttons);

            var win = new Window
            {
                Title = "Byt namn på roll",
                Content = stack,
                Width = 320,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };

            ok.Click += (_, _) =>
            {
                var name = tb.Text.Trim().ToUpper();
                if (string.IsNullOrEmpty(name))
                {
                    MessageBox.Show("Rollnamnet får inte vara tomt.", "Ogiltigt namn",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                result = name;
                win.DialogResult = true;
            };
            cancel.Click += (_, _) => win.DialogResult = false;

            win.ShowDialog();
            return result;
        }

        private void VoiceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressProfileUpdate || _selectedProfile == null) return;
            var idx = VoiceComboBox.SelectedIndex;
            if (idx >= 0 && idx < _voices.Count)
                SetActiveVoiceName(_selectedProfile, _voices[idx].Name);
        }

        private void Gender_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressProfileUpdate || _selectedProfile == null) return;

            var gender = GenderMan.IsChecked == true ? VoiceGender.Man
                       : GenderKvinna.IsChecked == true ? VoiceGender.Kvinna
                       : VoiceGender.Barn;

            _selectedProfile.Gender = gender;

            // Auto-select a matching voice
            var preferred = _voices.FirstOrDefault(v => v.Gender == gender);
            if (preferred.Name != null)
            {
                SetActiveVoiceName(_selectedProfile, preferred.Name);
                _suppressProfileUpdate = true;
                VoiceComboBox.SelectedIndex = _voices.FindIndex(v => v.Name == preferred.Name);
                _suppressProfileUpdate = false;
            }

            // Raise pitch for child
            if (gender == VoiceGender.Barn && _selectedProfile.Pitch == 0)
            {
                _selectedProfile.Pitch = 30;
                _suppressProfileUpdate = true;
                PitchSlider.Value = 30;
                PitchValueLabel.Text = "+30%";
                _suppressProfileUpdate = false;
            }
        }

        private void TempoSlider_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (TempoValueLabel != null) TempoValueLabel.Text = FormatPct(e.NewValue);
            if (_suppressProfileUpdate || _selectedProfile == null) return;
            _selectedProfile.Rate = e.NewValue;
        }

        private void PitchSlider_ValueChanged(object sender,
            RoutedPropertyChangedEventArgs<double> e)
        {
            if (PitchValueLabel != null) PitchValueLabel.Text = FormatPct(e.NewValue);
            if (_suppressProfileUpdate || _selectedProfile == null) return;
            _selectedProfile.Pitch = e.NewValue;
        }

        private async void TestVoice_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile == null)
            {
                MessageBox.Show("Välj en roll i listan.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!EnsureTtsConfigured()) return;

            try
            {
                SetStatus($"Testar röst för {_selectedProfile.RoleName}...");
                await _ttsService.SpeakAsync(
                    $"Hej, jag är {_selectedProfile.RoleName}. Så här låter min röst.",
                    _selectedProfile);
                SetStatus("Rösttest klart");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"TTS-fel: {ex.Message}", "Fel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("Rösttest misslyckades");
            }
        }

        // ================================================================== script selection
        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var l in _scriptLines) l.ShouldRead = true;
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var l in _scriptLines) l.ShouldRead = false;
        }

        private void ScrollToCurrent_Click(object sender, RoutedEventArgs e)
        {
            if (_currentLineIndex >= 0 && _currentLineIndex < _scriptLines.Count)
                ScriptDataGrid.ScrollIntoView(_scriptLines[_currentLineIndex]);
        }

        // ================================================================== context menu
        private void ScriptDataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var line = ScriptDataGrid.SelectedItem as ScriptLine;
            RenameRoleMenuItem.IsEnabled = line != null && !line.IsStageDirection;
            DeleteLineMenuItem.IsEnabled = line != null;
        }

        private void DeleteLine_Click(object sender, RoutedEventArgs e)
        {
            if (ScriptDataGrid.SelectedItem is not ScriptLine line) return;

            var result = MessageBox.Show(
                $"Radera repliken?\n\n\"{(line.Text.Length > 80 ? line.Text[..80] + "…" : line.Text)}\"\n\n" +
                "OBS! Det går inte att ångra detta.",
                "Radera replik", MessageBoxButton.OKCancel, MessageBoxImage.Warning);

            if (result != MessageBoxResult.OK) return;

            // If this was the current line, clear the highlight
            if (_currentLineIndex == _scriptLines.IndexOf(line))
            {
                line.IsCurrent = false;
                _currentLineIndex--;
            }
            else if (_scriptLines.IndexOf(line) < _currentLineIndex)
            {
                _currentLineIndex--;
            }

            _scriptLines.Remove(line);
            SyncVoiceProfiles();
            UpdateProgress();
            SetStatus("Replik raderad.");
        }

        private void StartFromHere_Click(object sender, RoutedEventArgs e)
        {
            if (ScriptDataGrid.SelectedItem is ScriptLine line)
            {
                if (!EnsureTtsConfigured()) return;

                // Clear old current marker
                if (_currentLineIndex >= 0 && _currentLineIndex < _scriptLines.Count)
                    _scriptLines[_currentLineIndex].IsCurrent = false;

                // Position just before this line so AdvanceToNextLine lands on it
                _currentLineIndex = _scriptLines.IndexOf(line) - 1;
                _isPlaying = true;
                UpdateStartStopButton();
                AdvanceToNextLine();
            }
        }

        private void MarkShouldRead_Click(object sender, RoutedEventArgs e)
        {
            if (ScriptDataGrid.SelectedItem is ScriptLine l) l.ShouldRead = true;
        }

        private void MarkShouldNotRead_Click(object sender, RoutedEventArgs e)
        {
            if (ScriptDataGrid.SelectedItem is ScriptLine l) l.ShouldRead = false;
        }

        // ================================================================== playback
        private void StartStop_Click(object sender, RoutedEventArgs e)
        {
            if (_isPlaying) StopPlayback();
            else StartPlayback();
        }

        private void StartPlayback()
        {
            if (_scriptLines.Count == 0)
            {
                MessageBox.Show("Öppna ett manus först.",
                    "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!EnsureTtsConfigured()) return;

            // If the user has selected a row, start from there instead of resuming
            // from the last played position.
            if (ScriptDataGrid.SelectedItem is ScriptLine selected)
            {
                var idx = _scriptLines.IndexOf(selected);
                if (idx >= 0)
                {
                    if (_currentLineIndex >= 0 && _currentLineIndex < _scriptLines.Count)
                        _scriptLines[_currentLineIndex].IsCurrent = false;
                    _currentLineIndex = idx - 1;
                }
            }

            _isPlaying = true;
            UpdateStartStopButton();
            SetStatus("Manusläsning startad – tryck Mellanslag för nästa replik");
            AdvanceToNextLine();
        }

        private void StopPlayback()
        {
            _speechVersion++;
            _isPlaying = false;
            _ttsService.StopSpeaking();
            UpdateStartStopButton();

            if (_currentLineIndex >= 0 && _currentLineIndex < _scriptLines.Count)
                _scriptLines[_currentLineIndex].IsCurrent = false;

            SetStatus("Manusläsning stoppad");
            CurrentLineText.Text = "";
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space || !_isPlaying) return;

            // Ignore space if a text-input control has focus
            if (Keyboard.FocusedElement is TextBox or ComboBox) return;

            e.Handled = true;
            _speechVersion++;                  // cancel current speech
            _ttsService.StopSpeaking();
            AdvanceToNextLine();
        }

        private async void AdvanceToNextLine()
        {
            int myVersion = ++_speechVersion;

            // Clear old highlight
            if (_currentLineIndex >= 0 && _currentLineIndex < _scriptLines.Count)
                _scriptLines[_currentLineIndex].IsCurrent = false;

            // Find next readable line
            int next = _currentLineIndex + 1;
            while (next < _scriptLines.Count && !_scriptLines[next].ShouldRead)
                next++;

            if (next >= _scriptLines.Count)
            {
                _isPlaying = false;
                UpdateStartStopButton();
                _currentLineIndex = -1;
                SetStatus("Manuset är klart! 🎉");
                CurrentLineText.Text = "";
                ProgressText.Text = "";
                return;
            }

            _currentLineIndex = next;
            var line = _scriptLines[_currentLineIndex];
            line.IsCurrent = true;

            Dispatcher.Invoke(() =>
            {
                ScriptDataGrid.ScrollIntoView(line);
                ScriptDataGrid.SelectedItem = line;
                CurrentLineText.Text = $"[{line.RoleName}]  {line.Text}";
                UpdateProgress();
            });

            var profile = _voiceProfiles.FirstOrDefault(p =>
                string.Equals(p.RoleName, line.RoleName, StringComparison.OrdinalIgnoreCase));

            if (profile == null)
            {
                profile = CreateDefaultProfile(line.RoleName, _voiceProfiles.Count);
                Dispatcher.Invoke(() => _voiceProfiles.Add(profile));
            }

            SetStatus($"Läser: {line.RoleName} — replik {_currentLineIndex + 1}");

            try
            {
                await _ttsService.SpeakAsync(line.Text, profile);
            }
            catch (Exception ex)
            {
                if (myVersion == _speechVersion && _isPlaying)
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"TTS-fel:\n{ex.Message}", "Fel",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        StopPlayback();
                    });
                }
            }
            finally
            {
                // If this version is still current, show "ready for next" status
                if (myVersion == _speechVersion && _isPlaying)
                    Dispatcher.Invoke(() => SetStatus(
                        $"Klar – tryck Mellanslag för nästa ({line.RoleName})"));
            }
        }

        // ================================================================== helpers
        private bool EnsureTtsConfigured()
        {
            if (_ttsService.IsConfigured) return true;

            MessageBox.Show(
                "TTS är inte konfigurerat.\n\n" +
                "Öppna Inställningar → TTS-inställningar och välj motor.",
                "TTS ej konfigurerat", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private void UpdateStartStopButton()
        {
            if (_isPlaying)
            {
                StartStopButton.Content = "⏹  Stoppa manusläsning";
                StartStopButton.Background = new SolidColorBrush(Color.FromRgb(229, 57, 53));
                StartStopButton.BorderBrush = new SolidColorBrush(Color.FromRgb(183, 28, 28));
            }
            else
            {
                StartStopButton.Content = "▶  Starta manusläsning";
                StartStopButton.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                StartStopButton.BorderBrush = new SolidColorBrush(Color.FromRgb(56, 142, 60));
            }
        }

        private void UpdateProgress()
        {
            if (_scriptLines.Count == 0) { ProgressText.Text = ""; return; }
            var readable = _scriptLines.Count(l => l.ShouldRead);
            var done = _currentLineIndex >= 0 ? _scriptLines.Take(_currentLineIndex + 1).Count(l => l.ShouldRead) : 0;
            ProgressText.Text = $"{done} / {readable} repliker";
        }

        private void SetStatus(string msg) => StatusText.Text = msg;

        private static string FormatPct(double v) =>
            v >= 0 ? $"+{v:F0}%" : $"{v:F0}%";

        private void About_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "ManusReader 1.0\n\n" +
                "Öva musikal-repliker med Azure Neural TTS.\n\n" +
                "Kortkommandon:\n" +
                "  Mellanslag   – Nästa replik (under pågående läsning)\n" +
                "  Ctrl+O       – Öppna manus\n" +
                "  Ctrl+S       – Spara projekt\n\n" +
                "Högerklicka i manuslistan för fler alternativ.",
                "Om ManusReader", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        protected override void OnClosed(EventArgs e)
        {
            SaveSettings();
            _ttsService.Dispose();
            base.OnClosed(e);
        }
    }
}
