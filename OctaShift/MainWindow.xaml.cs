using Microsoft.Win32;
using NAudio.Midi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text;

namespace OctaShift
{
    public partial class MainWindow : Window
    {
        // Usual 37 Keys: 48 to 84
        private const int Min37 = 48;
        private const int Max37 = 84;

        // 36 Keys
        private const int Min36 = Min37;
        private const int Max36 = Max37 - 1;

        // 15 Keys, No Half-notes
        private const int Min15 = Min37;
        private const int Max15 = Min37 + 24;
        private static readonly int[] NaturalNotes = { 0, 2, 4, 5, 7, 9, 11 };
        private const int Adjustment15 = 12;

        private bool _isLoading = true;
        private const string RepoOwner = "tigurand";
        private const string RepoName = "OctaShift";
        private const string AssetPrefix = "OctaShift-v";

        public MainWindow()
        {
            InitializeComponent();

            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            this.Title = $"OctaShift - {version}";

            LoadSettings();
            _isLoading = false;

            this.Loaded += (s, e) =>
            {
                _ = MaybeAutoCheckUpdates();

                if (Properties.Settings.Default.WindowWidth > 0)
                {
                    this.Width = Properties.Settings.Default.WindowWidth;
                    this.Height = Properties.Settings.Default.WindowHeight;
                    this.Top = Properties.Settings.Default.WindowTop;
                    this.Left = Properties.Settings.Default.WindowLeft;
                }

                if (Enum.TryParse(Properties.Settings.Default.WindowState, out WindowState state))
                    this.WindowState = state;
            };

            this.Closing += (s, e) =>
            {
                if (this.WindowState == WindowState.Normal)
                {
                    Properties.Settings.Default.WindowWidth = this.Width;
                    Properties.Settings.Default.WindowHeight = this.Height;
                    Properties.Settings.Default.WindowTop = this.Top;
                    Properties.Settings.Default.WindowLeft = this.Left;
                }
                else
                {
                    Properties.Settings.Default.WindowWidth = this.RestoreBounds.Width;
                    Properties.Settings.Default.WindowHeight = this.RestoreBounds.Height;
                    Properties.Settings.Default.WindowTop = this.RestoreBounds.Top;
                    Properties.Settings.Default.WindowLeft = this.RestoreBounds.Left;
                }

                Properties.Settings.Default.WindowState = this.WindowState.ToString();
                Properties.Settings.Default.Save();
            };
        }

        private void LoadSettings()
        {
            if (ClosestModeRadio != null)
                ClosestModeRadio.IsChecked = Properties.Settings.Default.UseClosestMode;

            if (GlobalShiftRadio != null)
                GlobalShiftRadio.IsChecked = !Properties.Settings.Default.UseClosestMode;

            if (AutoUpdateCheck != null)
                AutoUpdateCheck.IsChecked = Properties.Settings.Default.AutoUpdateEnabled;

            if (RemovePercussionCheck != null)
                RemovePercussionCheck.IsChecked = Properties.Settings.Default.RemovePercussion;

            if (MergeTracksCheck != null)
                MergeTracksCheck.IsChecked = Properties.Settings.Default.MergeTracks;

            if (MergeChannelsCheck != null)
                MergeChannelsCheck.IsChecked = Properties.Settings.Default.MergeChannels;

            if (TrimSilenceCheck != null)
                TrimSilenceCheck.IsChecked = Properties.Settings.Default.TrimSilence;

            if (SimplifyNotesCheck != null)
                SimplifyNotesCheck.IsChecked = Properties.Settings.Default.SimplifyNotes;

            if (SimplifyNotesCountTextBox != null)
                SimplifyNotesCountTextBox.Text = Properties.Settings.Default.SimplifyNotesCount.ToString();

            if (TransposeNotesCheck != null)
                TransposeNotesCheck.IsChecked = Properties.Settings.Default.TransposeNotes;

            if (TransposeNotesCountTextBox != null)
                TransposeNotesCountTextBox.Text = Properties.Settings.Default.TransposeNotesCount.ToString();

            if (OutputFolderTextBox != null)
                OutputFolderTextBox.Text = Properties.Settings.Default.OutputFolder;

            string keyMode = Properties.Settings.Default.KeyMode;

            if (Mode15Radio != null &&
                Mode36Radio != null &&
                Mode37Radio != null)
            {
                switch (keyMode)
                {
                    case "15":
                        Mode15Radio.IsChecked = true;
                        break;

                    case "36":
                        Mode36Radio.IsChecked = true;
                        break;

                    default:
                        Mode37Radio.IsChecked = true;
                        break;
                }
            }
        }

        private void SettingsChanged(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            SaveSettings();
        }

        private void SaveSettings()
        {
            Properties.Settings.Default.UseClosestMode = ClosestModeRadio.IsChecked == true;
            Properties.Settings.Default.AutoUpdateEnabled = AutoUpdateCheck.IsChecked == true;
            Properties.Settings.Default.RemovePercussion = RemovePercussionCheck.IsChecked == true;
            Properties.Settings.Default.MergeTracks = MergeTracksCheck.IsChecked == true;
            Properties.Settings.Default.MergeChannels = MergeChannelsCheck.IsChecked == true;
            Properties.Settings.Default.TrimSilence = TrimSilenceCheck.IsChecked == true;

            Properties.Settings.Default.SimplifyNotes = SimplifyNotesCheck.IsChecked == true;
            if (int.TryParse(SimplifyNotesCountTextBox.Text, out int simplifyCount) && simplifyCount > 0)
                Properties.Settings.Default.SimplifyNotesCount = simplifyCount;

            Properties.Settings.Default.TransposeNotes = TransposeNotesCheck.IsChecked == true;
            if (int.TryParse(TransposeNotesCountTextBox.Text, out int transposeCount))
                Properties.Settings.Default.TransposeNotesCount = transposeCount;

            Properties.Settings.Default.OutputFolder = OutputFolderTextBox.Text;

            if (Mode15Radio.IsChecked == true)
                Properties.Settings.Default.KeyMode = "15";
            else if (Mode36Radio.IsChecked == true)
                Properties.Settings.Default.KeyMode = "36";
            else
                Properties.Settings.Default.KeyMode = "37";

            Properties.Settings.Default.Save();
        }

        private async Task MaybeAutoCheckUpdates()
        {
            if (Properties.Settings.Default.AutoUpdateEnabled)
            {
                await CheckForUpdatesAsync(showResultIfUpToDate: false, autoPrompt: true);
            }
        }

        private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            await CheckForUpdatesAsync(showResultIfUpToDate: true, autoPrompt: true);
        }

        private async Task CheckForUpdatesAsync(bool showResultIfUpToDate, bool autoPrompt)
        {
            try
            {
                UpdateStatusText.Text = "Checking releases...";
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("OctaShift-Updater");

                var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
                var resp = await http.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    UpdateStatusText.Text = "Check failed";
                    if (showResultIfUpToDate)
                        MessageBox.Show($"Failed to check updates: {resp.StatusCode}", "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var json = await resp.Content.ReadAsStringAsync();
                var latest = System.Text.Json.JsonDocument.Parse(json).RootElement;
                var latestTag = latest.GetProperty("tag_name").GetString();
                var assets = latest.GetProperty("assets");

                var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
                bool hasNewer = IsNewer(latestTag, current);

                if (!hasNewer)
                {
                    UpdateStatusText.Text = "Up to date";
                    if (showResultIfUpToDate)
                        MessageBox.Show("You already have the latest version.", "Update", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var assetUrl = FindAssetUrl(assets, latestTag);
                if (assetUrl == null)
                {
                    UpdateStatusText.Text = "Asset not found";
                    MessageBox.Show("New version found, but release asset is missing.", "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var result = autoPrompt
                    ? MessageBox.Show($"New version {latestTag} available. Download and install now?", "Update", MessageBoxButton.YesNo, MessageBoxImage.Question)
                    : MessageBoxResult.No;

                if (autoPrompt && result != MessageBoxResult.Yes)
                {
                    UpdateStatusText.Text = "Update skipped";
                    return;
                }

                UpdateStatusText.Text = "Downloading...";
                var tmpZip = Path.Combine(Path.GetTempPath(), $"OctaShift-{latestTag}.zip");
                using (var stream = await http.GetStreamAsync(assetUrl))
                using (var file = File.Create(tmpZip))
                {
                    await stream.CopyToAsync(file);
                }

                var appDir = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                UpdateStatusText.Text = "Installing...";
                int currentPid = Process.GetCurrentProcess().Id;
                ScheduleUpdateInstall(tmpZip, appDir, currentPid);

                UpdateStatusText.Text = "Ready to restart";
                MessageBox.Show("Update downloaded. The app will close and restart to finish installing.", "Update", MessageBoxButton.OK, MessageBoxImage.Information);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                UpdateStatusText.Text = "Update failed";
                MessageBox.Show($"Update failed: {ex.Message}", "Update", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool IsNewer(string? tag, string currentVersion)
        {
            if (string.IsNullOrWhiteSpace(tag)) return false;

            string normalized = tag.TrimStart('v', 'V');
            if (!Version.TryParse(normalized, out var latest)) return false;
            if (!Version.TryParse(currentVersion, out var current)) return true;
            return latest > current;
        }

        private static string? FindAssetUrl(JsonElement assets, string? latestTag)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (!string.IsNullOrEmpty(name) && name.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return asset.GetProperty("browser_download_url").GetString();
                }
            }
            return null;
        }

        private static void ScheduleUpdateInstall(string zipPath, string destinationFolder, int currentPid)
        {
            var updater = Path.Combine(Path.GetTempPath(), $"OctaShift-updater-{Guid.NewGuid():N}.cmd");
            var tmpRoot = Path.Combine(Path.GetTempPath(), $"OctaShift-update-{Guid.NewGuid():N}");

            var script = new StringBuilder();
            script.AppendLine("@echo off");
            script.AppendLine("setlocal");
            script.AppendLine($"set ZIP=\"{zipPath}\"");
            script.AppendLine($"set DEST=\"{destinationFolder}\"");
            script.AppendLine($"set TMPDEST=\"{tmpRoot}\"");
            script.AppendLine($"set PID={currentPid}");
            script.AppendLine("timeout /t 2 /nobreak >nul");
            script.AppendLine(":waitloop");
            script.AppendLine("tasklist /FI \"PID eq %PID%\" | findstr /C:\" %PID% \" >nul");
            script.AppendLine("if %errorlevel%==0 (");
            script.AppendLine("  timeout /t 1 /nobreak >nul");
            script.AppendLine("  goto waitloop");
            script.AppendLine(")");
            script.AppendLine("powershell -NoProfile -Command \"$zip='%ZIP%'; $tmp='%TMPDEST%'; $dest='%DEST%'; Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue; Expand-Archive -Force $zip $tmp; $src=$tmp; $items=Get-ChildItem -LiteralPath $src; if($items.Count -eq 1 -and $items[0].PSIsContainer){$src=$items[0].FullName}; if(!(Test-Path -LiteralPath $dest)){ New-Item -ItemType Directory -Path $dest | Out-Null }; robocopy $src $dest /E /PURGE /NFL /NDL /NJH /NJS | Out-Null\"");
            script.AppendLine("start \"\" \"%DEST%\\OctaShift.exe\"");
            script.AppendLine("powershell -NoProfile -Command \"Remove-Item -Recurse -Force %TMPDEST% -ErrorAction SilentlyContinue\"");
            script.AppendLine("del /f /q %ZIP%");
            script.AppendLine("del /f /q \"%~f0\"");

            File.WriteAllText(updater, script.ToString());

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{updater}\"\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }

        private void AddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "MIDI Files (*.mid)|*.mid",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                    FileListBox.Items.Add(file);
            }
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var files = Directory.GetFiles(dialog.SelectedPath, "*.mid");
                foreach (var file in files)
                    FileListBox.Items.Add(file);
            }
        }

        private void RemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            while (FileListBox.SelectedItems.Count > 0)
                FileListBox.Items.Remove(FileListBox.SelectedItems[0]);
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);

            foreach (var file in files.Where(f => f.EndsWith(".mid", StringComparison.OrdinalIgnoreCase)))
                FileListBox.Items.Add(file);
        }

        private void SelectOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                OutputFolderTextBox.Text = dialog.SelectedPath;
                SaveSettings();
            }
        }

        private async Task FlashStatus()
        {
            for (int i = 0; i < 6; i++)
            {
                StatusText.Visibility = Visibility.Hidden;
                await Task.Delay(250);
                StatusText.Visibility = Visibility.Visible;
                await Task.Delay(250);
            }
        }

        private async void ProcessFiles_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Processing...";

            foreach (string file in FileListBox.Items)
                ProcessMidi(file);

            StatusText.Text = "Processing Completed!";
            StatusText.Foreground = System.Windows.Media.Brushes.Green;

            await FlashStatus();

            StatusText.Text = "Ready";
            StatusText.Foreground = System.Windows.Media.Brushes.Black;
        }

        private MidiEventCollection RemovePercussionProperly(MidiFile original)
        {
            var source = original.Events;
            var cleaned = new MidiEventCollection(original.FileFormat, original.DeltaTicksPerQuarterNote);

            for (int t = 0; t < source.Tracks; t++)
            {
                var newTrack = new List<MidiEvent>();

                foreach (var ev in source[t])
                {
                    // Remove Channel 10 musical events
                    if (ev is NoteEvent ne && ne.Channel == 10)
                        continue;

                    if (ev is PatchChangeEvent pe && pe.Channel == 10)
                        continue;

                    if (ev is ControlChangeEvent ce && ce.Channel == 10)
                        continue;

                    newTrack.Add(ev);
                }

                bool hasNotes = newTrack.OfType<NoteEvent>().Any();
                bool hasTempo = newTrack.OfType<TempoEvent>().Any();
                bool hasMeta = newTrack.OfType<MetaEvent>().Any();

                // Keep track if:
                // - It has notes
                // - OR it has tempo/meta events
                if (hasNotes || hasTempo || hasMeta)
                {
                    cleaned.AddTrack();
                    foreach (var ev in newTrack)
                        cleaned[cleaned.Tracks - 1].Add(ev);
                }
            }

            return cleaned;
        }

        private MidiEventCollection MergeTracksOnly(MidiEventCollection source, int tpqn)
        {
            var merged = new MidiEventCollection(source.MidiFileType, tpqn);
            merged.AddTrack();

            var allEvents = new List<MidiEvent>();

            foreach (var track in source)
                foreach (var ev in track)
                    allEvents.Add(ev);

            foreach (var ev in allEvents.OrderBy(e => e.AbsoluteTime))
                merged[0].Add(ev);

            return merged;
        }

        private void ForceSingleChannel(MidiEventCollection events)
        {
            foreach (var track in events)
            {
                foreach (var ev in track)
                {
                    if (ev is NoteEvent ne)
                        ne.Channel = 1;

                    if (ev is PatchChangeEvent pe)
                        pe.Channel = 1;

                    if (ev is ControlChangeEvent ce)
                        ce.Channel = 1;
                }
            }
        }

        private void TrimLeadingSilence(MidiEventCollection events)
        {
            long earliestNote = long.MaxValue;

            for (int t = 0; t < events.Tracks; t++)
            {
                foreach (var ev in events[t])
                {
                    if (ev is NoteOnEvent noteOn && noteOn.Velocity > 0)
                    {
                        if (ev.AbsoluteTime < earliestNote)
                            earliestNote = ev.AbsoluteTime;
                    }
                }
            }

            if (earliestNote <= 0 || earliestNote == long.MaxValue)
                return;

            for (int t = 0; t < events.Tracks; t++)
            {
                foreach (var ev in events[t])
                {
                    if (ev.AbsoluteTime >= earliestNote)
                        ev.AbsoluteTime -= earliestNote;
                }
            }

            for (int t = 0; t < events.Tracks; t++)
            {
                var sorted = events[t]
                    .OrderBy(e => e.AbsoluteTime)
                    .ThenBy(e => e is MetaEvent ? 0 : 1)
                    .ToList();

                events[t].Clear();

                foreach (var ev in sorted)
                    events[t].Add(ev);
            }
        }

        private MidiEventCollection RebuildCleanMidi(MidiEventCollection source)
        {
            var clean = new MidiEventCollection(source.MidiFileType, source.DeltaTicksPerQuarterNote);

            for (int t = 0; t < source.Tracks; t++)
            {
                clean.AddTrack();
                var notes = source[t]
                    .OrderBy(e => e.AbsoluteTime)
                    .ToList();

                foreach (var e in notes)
                {
                    clean[t].Add(e);
                }

                clean[t].Add(new MetaEvent(MetaEventType.EndTrack, 0, notes.Last().AbsoluteTime + 1));
            }

            clean.PrepareForExport();
            return clean;
        }

        private void Apply15KeyMode(MidiEventCollection events)
        {
            int root = DetectMajorKey(events);

            var activeNotes = new Dictionary<(int channel, int originalNote), Stack<NoteOnEvent>>();

            for (int t = 0; t < events.Tracks; t++)
            {
                foreach (var e in events[t])
                {
                    if (e is NoteOnEvent on && on.Channel != 10)
                    {
                        if (on.Velocity > 0)
                        {
                            int newNote = FoldTo15KeyScale(on.NoteNumber, root);

                            var key = (on.Channel, on.NoteNumber);

                            if (!activeNotes.ContainsKey(key))
                                activeNotes[key] = new Stack<NoteOnEvent>();

                            activeNotes[key].Push(on);

                            on.NoteNumber = newNote;
                        }
                        else
                        {
                            var key = (on.Channel, on.NoteNumber);

                            if (activeNotes.ContainsKey(key) && activeNotes[key].Count > 0)
                            {
                                var originalOn = activeNotes[key].Pop();
                                on.NoteNumber = originalOn.NoteNumber;
                            }
                        }
                    }
                    else if (e is NoteEvent off && off.CommandCode == MidiCommandCode.NoteOff)
                    {
                        var key = (off.Channel, off.NoteNumber);

                        if (activeNotes.ContainsKey(key) && activeNotes[key].Count > 0)
                        {
                            var originalOn = activeNotes[key].Pop();
                            off.NoteNumber = originalOn.NoteNumber;
                        }
                    }
                }
            }
        }

        private void TransposeNotes(MidiEventCollection events, int semitoneShift)
        {
            for (int t = 0; t < events.Tracks; t++)
            {
                foreach (var e in events[t])
                {
                    if (e is NoteEvent ne && ne.Channel != 10)
                    {
                        ne.NoteNumber += semitoneShift;
                    }
                }
            }
        }

        private void ProcessMidi(string path)
        {
            var original = new MidiFile(path, false);
            var sourceEvents = original.Events;

            MidiEventCollection workingEvents;

            if (RemovePercussionCheck.IsChecked == true)
            {
                workingEvents = RemovePercussionProperly(original);
            }
            else
            {
                workingEvents = original.Events;
            }

            if (MergeTracksCheck.IsChecked == true)
            {
                workingEvents = MergeTracksOnly(workingEvents, original.DeltaTicksPerQuarterNote);
            }

            if (MergeChannelsCheck.IsChecked == true)
            {
                ForceSingleChannel(workingEvents);
            }

            if (TrimSilenceCheck.IsChecked == true)
            {
                TrimLeadingSilence(workingEvents);
            }

            if (SimplifyNotesCheck.IsChecked == true)
            {
                int limit = Properties.Settings.Default.SimplifyNotesCount > 0
                    ? Properties.Settings.Default.SimplifyNotesCount
                    : 3;

                ApplyNoteSimplification(workingEvents, limit);
            }

            if (TransposeNotesCheck.IsChecked == true && ((Mode36Radio.IsChecked == true) || (Mode37Radio.IsChecked == true)))
            {
                int semitoneShift = Properties.Settings.Default.TransposeNotesCount;

                TransposeNotes(workingEvents, semitoneShift);
            }

            if (GlobalShiftRadio.IsChecked == true)
            {
                ApplyGlobalShift(workingEvents);
            }

            if (Mode15Radio.IsChecked == true)
            {
                Apply15KeyMode(workingEvents);
                TransposeNotes(workingEvents, Adjustment15);
            }

            for (int track = 0; track < workingEvents.Tracks; track++)
            {
                foreach (var midiEvent in workingEvents[track])
                {
                    if (midiEvent is NoteEvent noteEvent && noteEvent.Channel != 10)
                    {
                        if (Mode36Radio.IsChecked == true)
                        {
                            if (ClosestModeRadio.IsChecked == true)
                                noteEvent.NoteNumber = ClosestMap(noteEvent.NoteNumber, Min36, Max36);
                            else
                                noteEvent.NoteNumber = ClampTo36(noteEvent.NoteNumber);
                        }

                        else
                        {
                            if (ClosestModeRadio.IsChecked == true)
                                noteEvent.NoteNumber = ClosestMap(noteEvent.NoteNumber, Min37, Max37);
                            else
                                noteEvent.NoteNumber = ClampTo37(noteEvent.NoteNumber);
                        }
                    }
                }
            }

            string outputDir = string.IsNullOrWhiteSpace(OutputFolderTextBox.Text)
                ? Path.GetDirectoryName(path)!
                : OutputFolderTextBox.Text;

            string suffix;

            if (Mode15Radio.IsChecked == true)
                suffix = "15";
            else if (Mode36Radio.IsChecked == true)
                suffix = "36";
            else
                suffix = "37";

            string newPath = Path.Combine(
                outputDir,
                Path.GetFileNameWithoutExtension(path) + " " + suffix + ".mid");

            var safeMidi = RebuildCleanMidi(workingEvents);

            safeMidi.PrepareForExport();

            MidiFile.Export(newPath, safeMidi);
        }

        private int ClosestMap(int original, int min, int max)
        {
            int best = original;
            int bestDistance = int.MaxValue;

            for (int octaveShift = -6; octaveShift <= 6; octaveShift++)
            {
                int shifted = original + octaveShift * 12;

                if (shifted >= min && shifted <= max)
                {
                    int distance = Math.Abs(shifted - original);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = shifted;
                    }
                }
            }

            return best;
        }

        private int ClampTo37(int note)
        {
            return ClampToRange(note, Min37, Max37);
        }

        private int ClampTo36(int note)
        {
            return ClampToRange(note, Min36, Max36);
        }

        private int ClampToRange(int note, int min, int max)
        {
            int range = max - min;

            while (note < min)
                note += 12;

            while (note > max)
                note -= 12;

            if (note < min) note = min;
            if (note > max) note = max;

            return note;
        }

        private int DetectMajorKey(MidiEventCollection events)
        {
            int[] pitchCount = new int[12];

            for (int t = 0; t < events.Tracks; t++)
            {
                foreach (var e in events[t])
                {
                    if (e is NoteEvent ne && ne.Channel != 10)
                        pitchCount[ne.NoteNumber % 12]++;
                }
            }

            int bestRoot = 0;
            int bestScore = -1;

            for (int root = 0; root < 12; root++)
            {
                int score = 0;
                int[] majorPattern = { 0, 2, 4, 5, 7, 9, 11 };

                foreach (int interval in majorPattern)
                {
                    int note = (root + interval) % 12;
                    score += pitchCount[note];
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestRoot = root;
                }
            }

            return bestRoot;
        }

        private int FoldTo15KeyScale(int note, int root)
        {
            int[] pattern = { 0, 2, 4, 5, 7, 9, 11 };

            int pitchClass = note % 12;

            int degree = -1;
            int smallestDistance = int.MaxValue;

            for (int i = 0; i < pattern.Length; i++)
            {
                int scaleNote = (root + pattern[i]) % 12;
                int distance = Math.Abs(scaleNote - pitchClass);

                if (distance < smallestDistance)
                {
                    smallestDistance = distance;
                    degree = i;
                }
            }

            int[] cMajor = { 0, 2, 4, 5, 7, 9, 11 };

            int octaveOffset = (note - Min15) / 12;
            if (octaveOffset < 0) octaveOffset = 0;
            if (octaveOffset > 2) octaveOffset = 2;

            int mapped = Min15 + (octaveOffset * 12) + cMajor[degree];

            while (mapped < Min15)
                mapped += 12;

            while (mapped > Max15)
                mapped -= 12;

            return mapped;
        }

        private void NumericSettingTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_isLoading) return;
            SaveSettings();
        }

        private void ApplyNoteSimplification(MidiEventCollection events, int maxSimultaneous)
        {
            if (maxSimultaneous <= 0) return;

            var noteRefs = new List<(int Track, NoteOnEvent Note)>();

            for (int t = 0; t < events.Tracks; t++)
            {
                foreach (var ev in events[t])
                {
                    if (ev is NoteOnEvent on && on.Channel != 10 && on.Velocity > 0)
                    {
                        noteRefs.Add((t, on));
                    }
                }
            }

            var groups = noteRefs
                .GroupBy(n => n.Note.AbsoluteTime)
                .ToList();

            var touchedTracks = new HashSet<int>();

            foreach (var group in groups)
            {
                var notesAtTime = group
                    .OrderBy(n => n.Note.NoteNumber)
                    .ToList();

                if (notesAtTime.Count <= maxSimultaneous)
                    continue;

                var keep = new List<(int Track, NoteOnEvent Note)>();

                if (maxSimultaneous == 1)
                {
                    keep.Add(notesAtTime[notesAtTime.Count / 2]);
                }
                else
                {
                    double step = (notesAtTime.Count - 1) / (double)(maxSimultaneous - 1);
                    for (int i = 0; i < maxSimultaneous; i++)
                    {
                        int idx = (int)Math.Round(i * step);
                        if (idx < 0) idx = 0;
                        if (idx > notesAtTime.Count - 1) idx = notesAtTime.Count - 1;
                        keep.Add(notesAtTime[idx]);
                    }
                }

                var keepSet = new HashSet<NoteOnEvent>(keep.Select(k => k.Note));

                var toRemove = notesAtTime.Where(n => !keepSet.Contains(n.Note)).ToList();

                foreach (var noteRef in toRemove)
                {
                    var trackEvents = events[noteRef.Track];
                    trackEvents.Remove(noteRef.Note);

                    var off = trackEvents
                        .FirstOrDefault(e => e.AbsoluteTime >= noteRef.Note.AbsoluteTime &&
                                             e is NoteEvent ne &&
                                             ne.Channel == noteRef.Note.Channel &&
                                             ne.NoteNumber == noteRef.Note.NoteNumber &&
                                             (ne.CommandCode == MidiCommandCode.NoteOff ||
                                              (ne is NoteOnEvent noe && noe.Velocity == 0)));

                    if (off != null)
                        trackEvents.Remove(off);

                    touchedTracks.Add(noteRef.Track);
                }
            }

            foreach (int t in touchedTracks)
            {
                var sorted = events[t]
                    .OrderBy(e => e.AbsoluteTime)
                    .ThenBy(e => e is MetaEvent ? 0 : 1)
                    .ToList();

                events[t].Clear();
                foreach (var ev in sorted)
                    events[t].Add(ev);
            }
        }

        private void ApplyGlobalShift(MidiEventCollection events)
        {
            var notes = new List<int>();

            foreach (var track in events)
            {
                foreach (var ev in track)
                {
                    if (ev is NoteEvent ne && ne.Channel != 10)
                        notes.Add(ne.NoteNumber);
                }
            }

            if (!notes.Any()) return;

            double avg = notes.Average();
            int shift = 0;

            if (avg < Min37)
                shift = ((Min37 - (int)avg) / 12) * 12;
            else if (avg > Max37)
                shift = -(((int)avg - Max37) / 12) * 12;

            foreach (var track in events)
            {
                foreach (var ev in track)
                {
                    if (ev is NoteEvent ne && ne.Channel != 10)
                        ne.NoteNumber += shift;
                }
            }
        }
    }
}
