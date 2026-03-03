using Microsoft.Win32;
using NAudio.Midi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace OctaShift
{
    public partial class MainWindow : Window
    {
        // Usual 37 Keys: 48 to 84
        private const int Min37 = 48;
        private const int Max37 = 84;

        private const int Min36 = Min37;
        private const int Max36 = Max37 - 1;  // remove highest C

        private const int Min15 = Min37;
        private const int Max15 = Min37 + 24; // 2 octaves up (C → C)
        private static readonly int[] NaturalNotes = { 0, 2, 4, 5, 7, 9, 11 };

        private bool _isLoading = true;

        public MainWindow()
        {
            InitializeComponent();

            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            this.Title = $"OctaShift - {version}";

            LoadSettings();
            _isLoading = false;

            this.Loaded += (s, e) =>
            {
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

            if (RemovePercussionCheck != null)
                RemovePercussionCheck.IsChecked = Properties.Settings.Default.RemovePercussion;

            if (MergeTracksCheck != null)
                MergeTracksCheck.IsChecked = Properties.Settings.Default.MergeTracks;

            if (MergeChannelsCheck != null)
                MergeChannelsCheck.IsChecked = Properties.Settings.Default.MergeChannels;

            if (TrimSilenceCheck != null)
                TrimSilenceCheck.IsChecked = Properties.Settings.Default.TrimSilence;

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
            Properties.Settings.Default.RemovePercussion = RemovePercussionCheck.IsChecked == true;
            Properties.Settings.Default.MergeTracks = MergeTracksCheck.IsChecked == true;
            Properties.Settings.Default.MergeChannels = MergeChannelsCheck.IsChecked == true;
            Properties.Settings.Default.TrimSilence = TrimSilenceCheck.IsChecked == true;
            Properties.Settings.Default.OutputFolder = OutputFolderTextBox.Text;

            if (Mode15Radio.IsChecked == true)
                Properties.Settings.Default.KeyMode = "15";
            else if (Mode36Radio.IsChecked == true)
                Properties.Settings.Default.KeyMode = "36";
            else
                Properties.Settings.Default.KeyMode = "37";

            Properties.Settings.Default.Save();
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
                    if (e is NoteOnEvent on && on.Channel != 9)
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

            if (GlobalShiftRadio.IsChecked == true)
            {
                ApplyGlobalShift(workingEvents);
            }

            if (Mode15Radio.IsChecked == true)
            {
                Apply15KeyMode(workingEvents);
            }

            for (int track = 0; track < workingEvents.Tracks; track++)
            {
                foreach (var midiEvent in workingEvents[track])
                {
                    if (midiEvent is NoteEvent noteEvent && noteEvent.Channel != 9)
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
                    if (e is NoteEvent ne && ne.Channel != 9)
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

        private void ApplyGlobalShift(MidiEventCollection events)
        {
            var notes = new List<int>();

            foreach (var track in events)
            {
                foreach (var ev in track)
                {
                    if (ev is NoteEvent ne && ne.Channel != 9)
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
                    if (ev is NoteEvent ne && ne.Channel != 9)
                        ne.NoteNumber += shift;
                }
            }
        }
    }
}
