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
        private bool _autoPlay;
        private System.Threading.CancellationTokenSource? _autoCts;
        private System.Threading.CancellationTokenSource? _stepCts;
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
        private CancellationTokenSource? _downloadCts;
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
        // The subset of _voices currently listed in VoiceComboBox (filtered by character).
        private List<(string Name, string Display, VoiceGender Gender)> _shownVoices = new();
        private HashSet<string> _lastVoiceNames = new();

        // ================================================================== constructor
        public MainWindow()
        {
            InitializeComponent();

            ScriptDataGrid.ItemsSource = _scriptLines;
            RolesListBox.ItemsSource = _voiceProfiles;
            CharacterComboBox.ItemsSource = Characters.All.Select(c => c.Display).ToList();

            LoadSettings();
            ApplySettings();

            _aiTimer.Tick += (_, _) => RefreshAiProgressText();
        }

        // ================================================================== progress overlay
        private string _progressVerb = "Analyserar del";

        private void ShowAiProgress(
            string title = "🤖 Analyserar manus med AI",
            string verb = "Analyserar del",
            string preparing = "Förbereder analys…")
        {
            _progressVerb = verb;
            _aiCompleted = 0;
            _aiTotal = 0;
            _aiStopwatch.Restart();
            ProgressOverlayTitle.Text = title;
            AiProgressBar.IsIndeterminate = true;
            AiProgressBar.Value = 0;
            AiProgressStep.Text = preparing;
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
                $"{_progressVerb} {Math.Min(_aiCompleted + 1, _aiTotal)} av {_aiTotal}…";

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
            else if (_settings.Provider == TtsProvider.Piper)
            {
                _ttsService = new PiperTtsService();
                SetStatus(Piper.IsReady
                    ? "Piper-röster aktiverade"
                    : "Piper är valt men inte nedladdat – öppna TTS-inställningar");
            }
            else
            {
                _ttsService = new WindowsTtsService();
                SetStatus("Naturliga Windows-röster aktiverade");
            }

            RefreshVoiceList();

            // Rehearsal pause controls
            PauseBetweenBox.Text = _settings.PauseBetweenSeconds.ToString("0.#",
                System.Globalization.CultureInfo.InvariantCulture);
            MyLineAutoCheck.IsChecked = _settings.MyLineAuto;
            MyLineSecondsBox.Text = _settings.MyLineSeconds.ToString("0.#",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        private void RefreshVoiceList()
        {
            _voices = _settings.Provider switch
            {
                TtsProvider.Natural => WindowsTtsService.GetVoices(),
                TtsProvider.Piper => Piper.InstalledVoices(),
                _ => AzureVoices
            };

            // Roles created under another engine may lack a voice for this one –
            // give them one that fits the character. When new voices became
            // available (e.g. a female Piper voice was downloaded), also move roles
            // that had to make do with a non-fitting voice onto a fitting one.
            var voicesChanged = !_voices.Select(v => v.Name).ToHashSet().SetEquals(_lastVoiceNames);
            _lastVoiceNames = _voices.Select(v => v.Name).ToHashSet();
            if (_voices.Count > 0)
            {
                foreach (var p in _voiceProfiles)
                {
                    var fitting = VoicesFor(p.Character);
                    var hasVoice = _voices.Any(v => v.Name == ActiveVoiceName(p));
                    var misfit = voicesChanged && fitting.Count > 0 && !fitting.Any(v => v.Name == ActiveVoiceName(p));
                    if (hasVoice && !misfit) continue;
                    SetActiveVoiceName(p, PreferredVoices(p.Character).FirstOrDefault().Name ?? _voices[0].Name);
                }
            }

            RefreshProfileEditor();
        }

        // Voices that suit a character type, Swedish first. Children use a real
        // Swedish child voice when the engine has one, otherwise adult female
        // voices (a foreign child voice would mispronounce the Swedish script).
        private List<(string Name, string Display, VoiceGender Gender)> VoicesFor(CharacterType type)
        {
            var info = Characters.Get(type);
            var children = _voices.Where(v => v.Gender == VoiceGender.Barn && IsSwedishVoice(v)).ToList();
            var fitting = Characters.IsChild(type) && children.Count > 0
                ? children
                : _voices.Where(v => v.Gender == info.VoiceGender).ToList();
            if (Characters.IsChild(type))
                fitting.AddRange(_voices.Where(v => v.Gender == VoiceGender.Barn && !fitting.Contains(v)));

            return fitting.OrderBy(v => IsSwedishVoice(v) ? 0 : 1).ToList();
        }

        // Fitting voices if any exist, otherwise everything (Swedish first).
        private List<(string Name, string Display, VoiceGender Gender)> PreferredVoices(CharacterType type)
        {
            var fitting = VoicesFor(type);
            return fitting.Count > 0 ? fitting : _voices.OrderBy(v => IsSwedishVoice(v) ? 0 : 1).ToList();
        }

        // Starting tempo/pitch for a character. A real child voice needs no shift.
        private (double Rate, double Pitch) PresetFor(CharacterType type, string voiceName)
        {
            var info = Characters.Get(type);
            var isChildVoice = _voices.Any(v => v.Name == voiceName && v.Gender == VoiceGender.Barn);
            if (isChildVoice)
                return (info.Rate, type == CharacterType.BarnPojke ? -10 : 0);
            return (info.Rate, info.Pitch);
        }

        // The voice dropdown: voices fitting the role's character, or all voices
        // when "Visa alla röster" is ticked. The current voice is always included.
        private void RefreshVoiceCombo()
        {
            if (_selectedProfile == null) return;

            var active = ActiveVoiceName(_selectedProfile);

            // Tell the user when no installed voice fits (e.g. Piper without a
            // female voice) instead of silently using the other gender.
            var fitsNone = _voices.Count > 0 && VoicesFor(_selectedProfile.Character).Count == 0;
            NoFittingVoiceText.Visibility = fitsNone ? Visibility.Visible : Visibility.Collapsed;
            if (fitsNone)
            {
                var kind = Characters.Get(_selectedProfile.Character).VoiceGender == VoiceGender.Kvinna
                    ? "kvinnoröst" : "mansröst";
                NoFittingVoiceText.Text = _settings.Provider == TtsProvider.Piper
                    ? $"⚠ Ingen {kind} är nedladdad. Ladda ner en i TTS-inställningar (Ctrl+T)."
                    : $"⚠ Ingen {kind} är installerad för den här röstmotorn.";
            }

            _shownVoices = ShowAllVoicesCheck.IsChecked == true
                ? _voices.OrderBy(v => IsSwedishVoice(v) ? 0 : 1).ToList()
                : PreferredVoices(_selectedProfile.Character);

            if (!_shownVoices.Any(v => v.Name == active))
            {
                var current = _voices.FirstOrDefault(v => v.Name == active);
                if (current.Name != null) _shownVoices.Insert(0, current);
            }

            _suppressProfileUpdate = true;
            VoiceComboBox.ItemsSource = _shownVoices.Select(v => v.Display).ToList();
            VoiceComboBox.SelectedIndex = Math.Max(0, _shownVoices.FindIndex(v => v.Name == active));
            _suppressProfileUpdate = false;
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

        // Fills in ages from the script's cast list ("ANNIE 11 år") for roles without
        // one, and sets their character, voice, tempo and pitch to suit the age.
        // Returns how many roles got an age.
        private int ApplyCastAges(string wordPath)
        {
            Dictionary<string, int> ages;
            try { ages = WordScriptParser.ReadCastAges(wordPath); }
            catch { return 0; }

            var count = 0;
            foreach (var p in _voiceProfiles.Where(p => p.Age == null))
            {
                if (!ages.TryGetValue(p.RoleName, out var age)) continue;
                p.Age = age;
                p.Character = Characters.FromAgeAndGender(age, Characters.IsFemale(p.Character));
                var voice = PreferredVoices(p.Character).FirstOrDefault().Name;
                if (voice != null) SetActiveVoiceName(p, voice);
                (p.Rate, p.Pitch) = PresetFor(p.Character, ActiveVoiceName(p));
                count++;
            }
            if (count > 0) RefreshProfileEditor();
            return count;
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
            // Guess the character from the role name (MORMOR, POJKEN, ANNA …);
            // fall back to alternating man/woman if unknown.
            var character = NameGender.GuessCharacter(roleName)
                            ?? (index % 2 == 0 ? CharacterType.VuxenMan : CharacterType.VuxenKvinna);

            // Always prefer a Swedish voice — the script is read aloud in Swedish
            // even when a character has a foreign (e.g. English) name. Only fall
            // back to non-Swedish voices if no fitting Swedish voice exists.
            var fitting = PreferredVoices(character);
            var swedish = fitting.Where(IsSwedishVoice).ToList();
            var pool = swedish.Count > 0 ? swedish : fitting;
            var voice = pool.Count > 0 ? pool[index % pool.Count] : default;

            var (rate, pitch) = PresetFor(character, voice.Name ?? "");
            var profile = new VoiceProfile
            {
                RoleName = roleName, Character = character, Rate = rate, Pitch = pitch
            };

            // Gender-aware Swedish fallback if the voice catalog is empty.
            var defaultAzure = Characters.Get(character).VoiceGender == VoiceGender.Kvinna
                ? "sv-SE-SofieNeural" : "sv-SE-MattiasNeural";

            if (_settings.Provider == TtsProvider.Natural)
                profile.NaturalVoiceName = voice.Name ?? WindowsTtsService.DefaultVoiceName();
            else if (_settings.Provider == TtsProvider.Piper)
                profile.PiperVoiceName = voice.Name ?? "";
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

        private string ActiveVoiceName(VoiceProfile p) => _settings.Provider switch
        {
            TtsProvider.Natural => p.NaturalVoiceName,
            TtsProvider.Piper => p.PiperVoiceName,
            _ => p.VoiceName
        };

        private void SetActiveVoiceName(VoiceProfile p, string name)
        {
            switch (_settings.Provider)
            {
                case TtsProvider.Natural: p.NaturalVoiceName = name; break;
                case TtsProvider.Piper: p.PiperVoiceName = name; break;
                default: p.VoiceName = name; break;
            }
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
            LoadWordScript(dlg.FileName);
        }

        private void LoadWordScript(string path, (List<ScriptLine> Lines, HashSet<string> Roles)? parsed = null)
        {
            SetStatus($"Läser in {Path.GetFileName(path)}...");
            try
            {
                var (lines, roles) = parsed ?? WordScriptParser.Parse(path);

                if (lines.Count == 0)
                {
                    MessageBox.Show("Inga repliker hittades i dokumentet.\n\n" +
                        "Kontrollera att karaktärsnamnen är i VERSALER.",
                        "Inget hittades", MessageBoxButton.OK, MessageBoxImage.Information);
                    SetStatus("Inga repliker hittades");
                    return;
                }

                _currentScriptPath = path;
                _currentLineIndex = -1;
                _scriptLines.Clear();
                foreach (var l in lines) _scriptLines.Add(l);

                SyncVoiceProfiles();
                var aged = ApplyCastAges(path);

                Title = $"ManusReader – {Path.GetFileName(path)}";
                SetStatus($"Klar! {lines.Count} repliker, {_voiceProfiles.Count} roller inlästa." +
                          (aged > 0 ? $" Ålder hämtad från rollistan för {aged} roller." : ""));
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
        // Downloads (first time, after asking) and starts ManusReader's own Ollama
        // with the pinned model. Returns false if the user declined or it failed.
        private async Task<bool> EnsureAiReadyAsync()
        {
            var needEngine = !ManagedOllama.IsEngineInstalled;
            if (needEngine)
            {
                var gb = (ManagedOllama.EngineSizeMb + ManagedOllama.ModelSizeMb) / 1024.0;
                var answer = MessageBox.Show(
                    $"AI-tolkningen kräver en engångsnedladdning på cirka {gb:0.0} GB:\n\n" +
                    $"• Ollama {ManagedOllama.Version} (AI-motorn)\n" +
                    $"• AI-modellen {ManagedOllama.Model}\n\n" +
                    $"Filerna sparas i:\n{ManagedOllama.RootDir}\n\n" +
                    "Ladda ner nu?",
                    "Ladda ner AI", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return false;
            }

            _downloadCts = new CancellationTokenSource();
            var ct = _downloadCts.Token;
            var progress = new Progress<ManagedOllama.DownloadProgress>(SetDownloadProgress);

            ShowAiProgress(title: "🤖 Förbereder AI", preparing: "Startar AI-motorn…");
            AiProgressCancel.Visibility = Visibility.Visible;
            try
            {
                if (needEngine)
                    await ManagedOllama.InstallEngineAsync(progress, ct);

                SetStatus("Startar AI-motorn...");
                await ManagedOllama.EnsureRunningAsync(ct);

                if (!await ManagedOllama.IsModelInstalledAsync())
                {
                    if (!needEngine)
                    {
                        // Engine was already there, so the user hasn't been asked yet.
                        var answer = MessageBox.Show(
                            $"AI-modellen {ManagedOllama.Model} behöver laddas ner " +
                            $"(cirka {ManagedOllama.ModelSizeMb / 1024.0:0.0} GB). Ladda ner nu?",
                            "Ladda ner AI-modell", MessageBoxButton.YesNo, MessageBoxImage.Question);
                        if (answer != MessageBoxResult.Yes) return false;
                    }
                    await ManagedOllama.PullModelAsync(progress, ct);
                }
                return true;
            }
            catch (OperationCanceledException)
            {
                SetStatus("Nedladdningen avbröts");
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"AI-motorn kunde inte förberedas:\n\n{ex.Message}",
                    "AI ej tillgänglig", MessageBoxButton.OK, MessageBoxImage.Warning);
                SetStatus("AI ej tillgänglig");
                return false;
            }
            finally
            {
                _downloadCts = null;
                AiProgressCancel.Visibility = Visibility.Collapsed;
                HideAiProgress();
            }
        }

        private void SetDownloadProgress(ManagedOllama.DownloadProgress p)
        {
            _aiTotal = 0; // keep RefreshAiProgressText from overwriting the text below
            var mb = p.Done / 1048576.0;
            if (p.Total is > 0)
            {
                AiProgressBar.IsIndeterminate = false;
                AiProgressBar.Maximum = p.Total.Value;
                AiProgressBar.Value = p.Done;
                AiProgressStep.Text = $"Laddar ner {p.What}: {mb:0} av {p.Total.Value / 1048576.0:0} MB";
                AiProgressPercent.Text = $"{100.0 * p.Done / p.Total.Value:0}%";
            }
            else
            {
                AiProgressBar.IsIndeterminate = true;
                AiProgressStep.Text = p.Done > 0 ? $"{p.What}: {mb:0} MB" : $"{p.What}…";
                AiProgressPercent.Text = "";
            }
            AiProgressEta.Text = "";
        }

        private void AiProgressCancel_Click(object sender, RoutedEventArgs e) => _downloadCts?.Cancel();

        private async void OpenWordWithAI_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Word-dokument (*.docx)|*.docx|Alla filer (*.*)|*.*",
                Title = "Öppna Word-manus"
            };
            if (dlg.ShowDialog() != true) return;

            // A script with clear structure (bold role names) is read better, and in
            // seconds, by the rule-based import than by the small local AI model.
            try
            {
                var ruleBased = WordScriptParser.Parse(dlg.FileName);
                var speeches = ruleBased.Lines.Count(l => !l.IsStageDirection && !l.IsUnknown);
                if (ruleBased.Roles.Count >= 2 && speeches >= 10)
                {
                    var answer = MessageBox.Show(
                        "Manuset har tydlig struktur (rollnamn i fetstil). Den vanliga inläsningen " +
                        $"hittade {ruleBased.Roles.Count} roller och {speeches} repliker.\n\n" +
                        "För sådana manus är den vanliga inläsningen både snabbare och säkrare än AI.\n\n" +
                        "Ja = använd vanlig inläsning (rekommenderas)\nNej = tolka ändå med AI",
                        "Tydlig struktur hittad", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                    if (answer == MessageBoxResult.Cancel) return;
                    if (answer == MessageBoxResult.Yes)
                    {
                        LoadWordScript(dlg.FileName, ruleBased);
                        return;
                    }
                }
            }
            catch { /* unreadable for the rules – let the AI try */ }

            // Make sure our own Ollama and its model are downloaded and running.
            if (!await EnsureAiReadyAsync()) return;

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
                ApplyCastAges(dlg.FileName);

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
                        NaturalVoiceName = pd.NaturalVoiceName,
                        PiperVoiceName = pd.PiperVoiceName,
                        Character = pd.Character is int c
                            ? (CharacterType)c
                            : Characters.FromLegacyGender((VoiceGender)pd.Gender),
                        Age = pd.Age,
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
                        NaturalVoiceName = p.NaturalVoiceName,
                        PiperVoiceName = p.PiperVoiceName,
                        Gender = (int)Characters.ToLegacyGender(p.Character),
                        Character = (int)p.Character,
                        Age = p.Age,
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

            CharacterComboBox.SelectedIndex =
                Characters.All.ToList().FindIndex(c => c.Type == _selectedProfile.Character);
            AgeBox.Text = _selectedProfile.Age?.ToString() ?? "";
            RefreshVoiceCombo();
            _suppressProfileUpdate = true;

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
            if (idx >= 0 && idx < _shownVoices.Count)
                SetActiveVoiceName(_selectedProfile, _shownVoices[idx].Name);
        }

        private void ShowAllVoices_Changed(object sender, RoutedEventArgs e) => RefreshVoiceCombo();

        private void AgeBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile == null) return;
            _selectedProfile.Age = int.TryParse(AgeBox.Text.Trim(), out var age)
                ? Math.Clamp(age, 1, 110) : null;
            AgeBox.Text = _selectedProfile.Age?.ToString() ?? "";
        }

        private void RandomizeVoice_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProfile == null) return;
            AgeBox_LostFocus(sender, e); // pick up an age typed but not yet committed
            RandomizeVoice(_selectedProfile);
            RefreshProfileEditor();

            var voice = _voices.FirstOrDefault(v => v.Name == ActiveVoiceName(_selectedProfile)).Display;
            SetStatus($"{_selectedProfile.RoleName}: {voice}, tempo {FormatPct(_selectedProfile.Rate)}, " +
                      $"pitch {FormatPct(_selectedProfile.Pitch)} – justera gärna med reglagen");
        }

        // Rolls a voice for the role's gender and age (the entered age, or a typical
        // one for its character): a random fitting voice, preferring Swedish ones
        // and a different one than now, plus tempo/pitch within the age's range.
        private void RandomizeVoice(VoiceProfile p)
        {
            var female = Characters.IsFemale(p.Character);
            var age = p.Age ?? Characters.DefaultAge(p.Character);
            p.Character = Characters.FromAgeAndGender(age, female);

            var pool = PreferredVoices(p.Character);
            var swedish = pool.Where(IsSwedishVoice).ToList();
            if (swedish.Count > 0) pool = swedish;
            var others = pool.Where(v => v.Name != ActiveVoiceName(p)).ToList();
            if (others.Count > 0) pool = others;
            if (pool.Count > 0)
                SetActiveVoiceName(p, pool[Random.Shared.Next(pool.Count)].Name);

            var (rateMin, rateMax, pitchMin, pitchMax) = Characters.RangeFor(age, female);
            p.Rate = Math.Round(rateMin + Random.Shared.NextDouble() * (rateMax - rateMin));
            p.Pitch = Math.Round(pitchMin + Random.Shared.NextDouble() * (pitchMax - pitchMin));
        }

        // Choosing a character picks a fitting voice (if the current one doesn't
        // fit) and applies the character's tempo/pitch preset.
        private void CharacterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressProfileUpdate || _selectedProfile == null) return;
            var idx = CharacterComboBox.SelectedIndex;
            if (idx < 0) return;

            var type = Characters.All[idx].Type;
            _selectedProfile.Character = type;
            // An entered age that contradicts the new character no longer applies.
            if (_selectedProfile.Age is int age
                && Characters.FromAgeAndGender(age, Characters.IsFemale(type)) != type)
                _selectedProfile.Age = null;

            var fitting = VoicesFor(type);
            if (fitting.Count > 0 && !fitting.Any(v => v.Name == ActiveVoiceName(_selectedProfile)))
                SetActiveVoiceName(_selectedProfile, fitting[0].Name);

            var (rate, pitch) = PresetFor(type, ActiveVoiceName(_selectedProfile));
            _selectedProfile.Rate = rate;
            _selectedProfile.Pitch = pitch;

            RefreshProfileEditor();
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
            _autoCts?.Cancel();          // stop rehearsal autoplay if running
            _ttsService.StopSpeaking();
            UpdateStartStopButton();

            if (_currentLineIndex >= 0 && _currentLineIndex < _scriptLines.Count)
                _scriptLines[_currentLineIndex].IsCurrent = false;

            SetStatus("Manusläsning stoppad");
            CurrentLineText.Text = "";
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Menu shortcuts (shown as InputGestureText in the menus).
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                RoutedEventHandler? action = e.Key switch
                {
                    Key.O => OpenScript_Click,
                    Key.W => OpenWord_Click,
                    Key.S => SaveProject_Click,
                    Key.T => OpenTtsSettings_Click,
                    _ => null
                };
                if (action != null)
                {
                    e.Handled = true;
                    action(this, new RoutedEventArgs());
                }
                return;
            }

            if (!_isPlaying) return;

            // Only react to the keys we care about.
            bool forward = e.Key is Key.Space or Key.Right or Key.Down;
            bool back = e.Key is Key.Left or Key.Up;
            if (!forward && !back) return;

            // Ignore navigation keys if a text-input control has focus
            if (Keyboard.FocusedElement is TextBox or ComboBox) return;

            e.Handled = true;

            if (_autoPlay)
            {
                // In rehearsal mode, Space (or forward keys) skips ahead.
                if (forward) SkipStep();
                return;
            }

            // Manual mode: arrows step back/forward, Space goes forward.
            PlayInDirection(forward ? +1 : -1);
        }

        // ================================================================== rehearsal (autoplay)
        private void Rehearse_Click(object sender, RoutedEventArgs e)
        {
            if (_isPlaying) { StopPlayback(); return; }
            StartRehearsal();
        }

        private async void StartRehearsal()
        {
            if (_scriptLines.Count == 0)
            {
                MessageBox.Show("Öppna ett manus först.",
                    "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!EnsureTtsConfigured()) return;

            // Read pause settings from the UI and persist them.
            var pauseBetween = ParseSeconds(PauseBetweenBox.Text, 0.7);
            var myAuto = MyLineAutoCheck.IsChecked == true;
            var myFixed = ParseSeconds(MyLineSecondsBox.Text, 3.0);
            _settings.PauseBetweenSeconds = pauseBetween;
            _settings.MyLineAuto = myAuto;
            _settings.MyLineSeconds = myFixed;

            // Start from the selected row, otherwise from the beginning.
            int start = 0;
            if (ScriptDataGrid.SelectedItem is ScriptLine sel)
            {
                var i = _scriptLines.IndexOf(sel);
                if (i >= 0) start = i;
            }

            _autoCts?.Cancel();
            _autoCts = new System.Threading.CancellationTokenSource();
            var sessionToken = _autoCts.Token;

            _isPlaying = true;
            _autoPlay = true;
            UpdateStartStopButton();

            try
            {
                for (int i = start; i < _scriptLines.Count; i++)
                {
                    sessionToken.ThrowIfCancellationRequested();

                    using var stepCts =
                        System.Threading.CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
                    _stepCts = stepCts;
                    var stepToken = stepCts.Token;

                    var line = _scriptLines[i];
                    HighlightLine(i);

                    try
                    {
                        if (line.ShouldRead)
                        {
                            // Computer's line — speak it, then pause.
                            var profile = ResolveProfile(line);
                            SetStatus($"💻 Datorn läser: {line.RoleName} (Mellanslag = hoppa)");
                            await _ttsService.SpeakAsync(line.SpokenText, profile);
                            stepToken.ThrowIfCancellationRequested();
                            await Task.Delay(MsFromSeconds(pauseBetween), stepToken);
                        }
                        else if (!line.IsStageDirection && !line.IsUnknown)
                        {
                            // Your line — wait so you can say it out loud.
                            var ms = myAuto ? EstimateMyLineMs(line.SpokenText) : MsFromSeconds(myFixed);
                            SetStatus($"🎤 Din replik: {line.RoleName} – säg den nu " +
                                      $"({ms / 1000.0:0.#}s, Mellanslag = klar)");
                            await Task.Delay(ms, stepToken);
                        }
                        else
                        {
                            // Stage direction / unassigned line — skip briefly.
                            await Task.Delay(150, stepToken);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Whole session stopped → exit; single step skipped → continue.
                        if (sessionToken.IsCancellationRequested) throw;
                    }
                }

                SetStatus("Repetition klar! 🎉");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Repetition stoppad");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"TTS-fel:\n{ex.Message}", "Fel",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _stepCts = null;
                _autoPlay = false;
                _isPlaying = false;
                UpdateStartStopButton();
                if (_currentLineIndex >= 0 && _currentLineIndex < _scriptLines.Count)
                    _scriptLines[_currentLineIndex].IsCurrent = false;
                _currentLineIndex = -1;
                CurrentLineText.Text = "";
            }
        }

        // Advances the autoplay loop past the current line (speech + pause).
        private void SkipStep()
        {
            _ttsService.StopSpeaking();
            _stepCts?.Cancel();
        }

        private void HighlightLine(int index)
        {
            if (_currentLineIndex >= 0 && _currentLineIndex < _scriptLines.Count)
                _scriptLines[_currentLineIndex].IsCurrent = false;

            _currentLineIndex = index;
            if (index < 0 || index >= _scriptLines.Count) return;

            var line = _scriptLines[index];
            line.IsCurrent = true;
            ScriptDataGrid.ScrollIntoView(line);
            ScriptDataGrid.SelectedItem = line;
            CurrentLineText.Text = $"[{line.RoleName}]  {line.Text}";
            UpdateProgress();
        }

        private VoiceProfile ResolveProfile(ScriptLine line)
        {
            var profile = _voiceProfiles.FirstOrDefault(p =>
                string.Equals(p.RoleName, line.RoleName, StringComparison.OrdinalIgnoreCase));
            if (profile == null)
            {
                profile = CreateDefaultProfile(line.RoleName, _voiceProfiles.Count);
                _voiceProfiles.Add(profile);
            }
            return profile;
        }

        // Estimates how long a spoken line takes, to pace the pause for the user's
        // own lines. ~130 words/min plus a small buffer, clamped to 1.5–20 s.
        private static int EstimateMyLineMs(string text)
        {
            var words = text.Split((char[]?)null,
                StringSplitOptions.RemoveEmptyEntries).Length;
            var seconds = words / 130.0 * 60.0 + 0.8;
            return (int)(Math.Clamp(seconds, 1.5, 20.0) * 1000);
        }

        private static int MsFromSeconds(double s) =>
            (int)(Math.Clamp(s, 0, 60) * 1000);

        private static double ParseSeconds(string text, double fallback) =>
            double.TryParse(text?.Replace(',', '.'),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0
                ? v : fallback;

        // ================================================================== audio export (MP3)
        private async void ExportAudio_Click(object sender, RoutedEventArgs e)
        {
            if (_scriptLines.Count == 0)
            {
                MessageBox.Show("Öppna ett manus först.",
                    "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_settings.Provider == TtsProvider.Azure && !_ttsService.IsConfigured)
            {
                MessageBox.Show(
                    "Azure-rösterna är inte konfigurerade.\n\n" +
                    "Ange nyckel och region i TTS-inställningar, eller växla till " +
                    "Windows-röster, för att exportera.",
                    "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_settings.Provider == TtsProvider.Piper && !_ttsService.IsConfigured)
            {
                MessageBox.Show(
                    "Piper är inte nedladdat ännu.\n\n" +
                    "Ladda ner Piper och minst en röst i TTS-inställningar för att exportera.",
                    "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_isPlaying) StopPlayback();

            // Same pause settings and line logic as rehearsal mode.
            var pauseBetween = ParseSeconds(PauseBetweenBox.Text, 0.7);
            var myAuto = MyLineAutoCheck.IsChecked == true;
            var myFixed = ParseSeconds(MyLineSecondsBox.Text, 3.0);

            var segments = new List<AudioExportService.Segment>();
            foreach (var line in _scriptLines)
            {
                if (line.ShouldRead)
                {
                    var profile = ResolveProfile(line);
                    segments.Add(new AudioExportService.SpeakSegment(line.SpokenText, profile));
                    segments.Add(new AudioExportService.SilenceSegment(MsFromSeconds(pauseBetween)));
                }
                else if (!line.IsStageDirection && !line.IsUnknown)
                {
                    var ms = myAuto ? EstimateMyLineMs(line.SpokenText) : MsFromSeconds(myFixed);
                    segments.Add(new AudioExportService.SilenceSegment(ms));
                }
            }

            var speakCount = segments.Count(s => s is AudioExportService.SpeakSegment);
            if (speakCount == 0)
            {
                MessageBox.Show(
                    "Inga repliker är markerade för datorn att läsa, så ljudfilen " +
                    "skulle bli tyst. Markera minst en replik som ska läsas.",
                    "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "MP3-ljud (*.mp3)|*.mp3",
                Title = "Exportera repetition som MP3",
                FileName = (_currentScriptPath != null
                    ? Path.GetFileNameWithoutExtension(_currentScriptPath)
                    : "repetition") + ".mp3",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
            };
            if (dlg.ShowDialog() != true) return;
            var path = dlg.FileName;

            ShowAiProgress(
                title: "🎧 Exporterar repetition till MP3",
                verb: "Spelar in replik",
                preparing: "Förbereder inspelning…");
            SetAiProgress(0, speakCount);

            try
            {
                var progress = new Progress<AudioExportService.ExportProgress>(p =>
                    Dispatcher.Invoke(() => SetAiProgress(p.Done, p.Total)));

                // Pick the renderer matching the active voice engine.
                AudioExportService.IPcmRenderer renderer = _settings.Provider switch
                {
                    TtsProvider.Azure => new AudioExportService.AzurePcmRenderer(_settings.Azure),
                    TtsProvider.Piper => new AudioExportService.PiperPcmRenderer(),
                    _ => new AudioExportService.WindowsPcmRenderer()
                };

                await Task.Run(() =>
                {
                    using (renderer)
                        AudioExportService.ExportMp3(path, segments, renderer, 96, progress);
                });

                HideAiProgress();

                var sizeMb = new FileInfo(path).Length / 1024.0 / 1024.0;
                var res = MessageBox.Show(
                    $"Klart! Repetitionen sparades som MP3:\n{path}\n\n" +
                    $"• Repliker inspelade: {speakCount}\n" +
                    $"• Filstorlek: {sizeMb:0.0} MB\n\n" +
                    "Vill du öppna mappen?",
                    "Export klar", MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (res == MessageBoxResult.Yes)
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ljudexport misslyckades:\n{ex.Message}",
                    "Fel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                HideAiProgress();
            }
        }

        private void AdvanceToNextLine() => PlayInDirection(+1);

        // Moves to the next/previous readable line (direction +1 forward, -1 back)
        // and speaks it. Used by Space and the arrow keys in manual playback.
        private async void PlayInDirection(int direction)
        {
            // Find the next readable line in the requested direction.
            int target = _currentLineIndex + direction;
            while (target >= 0 && target < _scriptLines.Count && !_scriptLines[target].ShouldRead)
                target += direction;

            // Reached the end going forward → finish.
            if (direction > 0 && target >= _scriptLines.Count)
            {
                if (_currentLineIndex >= 0 && _currentLineIndex < _scriptLines.Count)
                    _scriptLines[_currentLineIndex].IsCurrent = false;
                _isPlaying = false;
                UpdateStartStopButton();
                _currentLineIndex = -1;
                SetStatus("Manuset är klart! 🎉");
                CurrentLineText.Text = "";
                ProgressText.Text = "";
                return;
            }

            // Reached the start going back → stay put, keep current highlight.
            if (direction < 0 && target < 0)
            {
                SetStatus("Redan vid första repliken");
                return;
            }

            int myVersion = ++_speechVersion;
            _ttsService.StopSpeaking();

            // Move highlight from the old line to the target line.
            if (_currentLineIndex >= 0 && _currentLineIndex < _scriptLines.Count)
                _scriptLines[_currentLineIndex].IsCurrent = false;

            _currentLineIndex = target;
            var line = _scriptLines[_currentLineIndex];
            line.IsCurrent = true;

            ScriptDataGrid.ScrollIntoView(line);
            ScriptDataGrid.SelectedItem = line;
            CurrentLineText.Text = $"[{line.RoleName}]  {line.Text}";
            UpdateProgress();

            var profile = ResolveProfile(line);

            SetStatus($"Läser: {line.RoleName} — replik {_currentLineIndex + 1}");

            try
            {
                await _ttsService.SpeakAsync(line.SpokenText, profile);
            }
            catch (Exception ex)
            {
                if (myVersion == _speechVersion && _isPlaying)
                {
                    MessageBox.Show($"TTS-fel:\n{ex.Message}", "Fel",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    StopPlayback();
                }
            }
            finally
            {
                if (myVersion == _speechVersion && _isPlaying)
                    SetStatus($"Klar – Mellanslag/pil för nästa, pil vänster tillbaka ({line.RoleName})");
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
                // Show the relevant keyboard hint right in the button while playing.
                StartStopButton.Content = _autoPlay
                    ? "⏹  Stoppa  ·  Mellanslag = hoppa"
                    : "⏹  Stoppa  ·  Mellanslag/→ nästa  ·  ← tillbaka";
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
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            MessageBox.Show(
                $"ManusReader {version?.ToString(3)}\n\n" +
                "Öva in dina repliker – datorn läser de andra rollerna\n" +
                "med Windows-röster, Piper eller Azure Neural TTS.\n\n" +
                "Kortkommandon:\n" +
                "  Mellanslag / →   – Nästa replik\n" +
                "  ←                – Föregående replik (manuellt läge)\n" +
                "  Ctrl+O           – Öppna textmanus\n" +
                "  Ctrl+W           – Öppna Word-manus\n" +
                "  Ctrl+S           – Spara projekt\n" +
                "  Ctrl+T           – TTS-inställningar\n\n" +
                "Högerklicka i manuslistan för fler alternativ.\n\n" +
                "github.com/finbom/ManusTraning",
                "Om ManusReader", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        protected override void OnClosed(EventArgs e)
        {
            SaveSettings();
            _ttsService.Dispose();
            _downloadCts?.Cancel();
            ManagedOllama.Stop();
            base.OnClosed(e);
        }
    }
}
