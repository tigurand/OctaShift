using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace OctaShift
{
    public partial class AdvancedSimplifyWindow : Window
    {
        private const string CombinedViewLabel = "All files (combined)";

        private readonly List<string> _files;
        private readonly AdvancedSimplifyConfig _config;
        private readonly ObservableCollection<TrackSimplifyRow> _rows = new();
        private bool _loading = true;

        internal AdvancedSimplifyConfig Result { get; private set; }

        internal AdvancedSimplifyWindow(IEnumerable<string> files, AdvancedSimplifyConfig config)
        {
            InitializeComponent();

            _files = (files ?? Enumerable.Empty<string>()).ToList();
            _config = config?.Clone() ?? new AdvancedSimplifyConfig();
            Result = _config;

            TracksGrid.ItemsSource = _rows;
            StrategyColumn.ItemsSource = Enum.GetValues(typeof(NoteKeepStrategy));

            UnmatchedCombo.Items.Add("Leave untouched");
            UnmatchedCombo.Items.Add("Apply Default");
            UnmatchedCombo.SelectedIndex =
                _config.UnmatchedStrategy == NoteKeepStrategy.Default ? 1 : 0;

            FileCombo.Items.Add(CombinedViewLabel);

            foreach (string file in _files)
                FileCombo.Items.Add(Path.GetFileName(file) is { Length: > 0 } name ? name : file);

            if (_files.Count == 0)
            {
                FileSelectorPanel.Visibility = Visibility.Collapsed;
                SkippedTracksText.Text = "No MIDI file available.";
                _loading = false;
                return;
            }

            FileSelectorPanel.Visibility = _files.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

            _loading = false;

            FileCombo.SelectedIndex = _files.Count > 1 ? 0 : 1;
        }

        private void FileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;

            SyncRowsIntoConfig();
            LoadSelectedView();
        }

        private void LoadSelectedView()
        {
            _rows.Clear();

            int view = FileCombo.SelectedIndex;

            if (view < 0)
                return;

            if (view == 0)
            {
                LoadCombinedView();
                return;
            }

            LoadFileView(view - 1);
        }

        private void LoadCombinedView()
        {
            List<TrackSimplifyRow> rows = MidiTrackInspector.BuildCombinedCatalog(
                _files, _config, out int unnamedTracks, out List<string> unreadableFiles);

            foreach (TrackSimplifyRow row in rows)
                _rows.Add(row);

            var notes = new List<string>();

            if (unnamedTracks > 0)
                notes.Add($"{unnamedTracks} unnamed track(s): open a single file to configure them");

            if (unreadableFiles.Count > 0)
                notes.Add("could not read: " + string.Join(", ", unreadableFiles));

            SkippedTracksText.Text = string.Join("; ", notes);
        }

        private void LoadFileView(int index)
        {
            if (index < 0 || index >= _files.Count)
                return;

            try
            {
                List<TrackSimplifyRow> rows = MidiTrackInspector.Inspect(_files[index], _config, out int skippedTracks);

                foreach (TrackSimplifyRow row in rows)
                    _rows.Add(row);

                SkippedTracksText.Text = skippedTracks > 0
                    ? $"{skippedTracks} track(s) without playable notes ignored"
                    : string.Empty;
            }
            catch (Exception ex)
            {
                SkippedTracksText.Text = string.Empty;

                MessageBox.Show(
                    this,
                    $"Could not read the tracks of this MIDI file:\n{ex.Message}",
                    "Per-Track Limit",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void SyncRowsIntoConfig()
        {
            foreach (TrackSimplifyRow row in _rows)
                _config.SetRule(row.TrackName, row.TrackIndex, row.Strategy, row.Limit);
        }

        private void UnmatchedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;

            _config.UnmatchedStrategy = UnmatchedCombo.SelectedIndex == 1
                ? NoteKeepStrategy.Default
                : NoteKeepStrategy.Off;
        }

        private void AllDefault_Click(object sender, RoutedEventArgs e)
        {
            foreach (TrackSimplifyRow row in _rows)
                row.Strategy = NoteKeepStrategy.Default;
        }

        private void AllOff_Click(object sender, RoutedEventArgs e)
        {
            foreach (TrackSimplifyRow row in _rows)
                row.Strategy = NoteKeepStrategy.Off;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            TracksGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            TracksGrid.CommitEdit(DataGridEditingUnit.Row, true);

            SyncRowsIntoConfig();

            Result = _config;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
