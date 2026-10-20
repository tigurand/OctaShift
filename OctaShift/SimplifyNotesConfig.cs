using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using NAudio.Midi;

namespace OctaShift
{
    internal enum SimplifyNotesMode
    {
        Off,
        Auto,
        Advanced
    }

    /// <summary>
    /// How notes are chosen when more than the limit start at the same time.
    /// </summary>
    internal enum NoteKeepStrategy
    {
        /// <summary>Track is left untouched.</summary>
        Off,

        /// <summary>Existing behaviour: evenly spread picks from lowest to highest (middle when limit is 1).</summary>
        Default,

        /// <summary>Keep the lowest notes (e.g. power chords).</summary>
        Lowest,

        /// <summary>Keep the highest notes.</summary>
        Highest,

        /// <summary>Drop every note of the track (the track is silenced).</summary>
        Exclude
    }

    internal sealed class TrackSimplifyRule
    {
        public string Name { get; set; } = string.Empty;

        public int Index { get; set; }

        public NoteKeepStrategy Strategy { get; set; } = NoteKeepStrategy.Off;

        public int Limit { get; set; } = 3;
    }

    /// <summary>
    /// Per-track configuration for the Advanced mode, persisted as JSON in
    /// <c>Properties.Settings.Default.SimplifyNotesAdvanced</c>.
    /// </summary>
    internal sealed class AdvancedSimplifyConfig
    {
        public const int MinLimit = 1;
        public const int MaxLimit = 127;
        public const int DefaultLimit = 3;

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>Strategy used for tracks that have no matching rule (Off or Default only).</summary>
        public NoteKeepStrategy UnmatchedStrategy { get; set; } = NoteKeepStrategy.Off;

        public List<TrackSimplifyRule> Rules { get; set; } = new();

        public static AdvancedSimplifyConfig Deserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new AdvancedSimplifyConfig();

            try
            {
                var parsed = JsonSerializer.Deserialize<AdvancedSimplifyConfig>(json, SerializerOptions);
                if (parsed == null)
                    return new AdvancedSimplifyConfig();

                parsed.Normalize();
                return parsed;
            }
            catch (JsonException)
            {
                return new AdvancedSimplifyConfig();
            }
            catch (NotSupportedException)
            {
                return new AdvancedSimplifyConfig();
            }
        }

        public string Serialize()
        {
            Normalize();
            return JsonSerializer.Serialize(this, SerializerOptions);
        }

        public AdvancedSimplifyConfig Clone()
        {
            return Deserialize(Serialize());
        }

        public void Normalize()
        {
            if (UnmatchedStrategy != NoteKeepStrategy.Off && UnmatchedStrategy != NoteKeepStrategy.Default)
                UnmatchedStrategy = NoteKeepStrategy.Off;

            Rules ??= new List<TrackSimplifyRule>();

            foreach (var rule in Rules)
            {
                if (rule.Limit < MinLimit || rule.Limit > MaxLimit)
                    rule.Limit = DefaultLimit;

                if (!Enum.IsDefined(typeof(NoteKeepStrategy), rule.Strategy))
                    rule.Strategy = NoteKeepStrategy.Off;

                rule.Name = NormalizeName(rule.Name);

                if (rule.Index < 0)
                    rule.Index = 0;
            }
        }

        public static string NormalizeName(string? name)
        {
            return (name ?? string.Empty).Trim();
        }

        private static string MatchKey(string? name)
        {
            return NormalizeName(name).ToLowerInvariant();
        }

        /// <summary>
        /// Looser variant used when no exact name match exists, so that tracks called
        /// "Piano 1", "Piano.2" or "Piano #3" in other files still match a "Piano" rule.
        /// </summary>
        private static string LooseMatchKey(string? name)
        {
            string key = MatchKey(name);

            if (key.Length == 0)
                return key;

            return System.Text.RegularExpressions.Regex.Replace(key, @"[\s._\-]*#?\d+$", string.Empty);
        }

        /// <summary>
        /// Key used to group tracks with equivalent names (e.g. "Piano 1" and "Piano 2")
        /// so a batch can be configured with a single row per instrument.
        /// </summary>
        public static string GroupKey(string? name)
        {
            return LooseMatchKey(name);
        }

        public TrackSimplifyRule? FindRule(string? trackName, int trackIndex)
        {
            if (Rules == null || Rules.Count == 0)
                return null;

            string key = MatchKey(trackName);

            if (key.Length > 0)
            {
                // Named tracks are matched by name, so a rule configured for one file
                // never leaks onto a differently named track of another file.
                // An explicit Off rule still matches, so it can override the fallback.
                var exact = Rules.FirstOrDefault(r => MatchKey(r.Name) == key);
                if (exact != null)
                    return exact;

                string looseKey = LooseMatchKey(trackName);
                if (looseKey.Length > 0)
                {
                    var loose = Rules.FirstOrDefault(r =>
                        MatchKey(r.Name).Length > 0 && LooseMatchKey(r.Name) == looseKey);

                    if (loose != null)
                        return loose;
                }

                return null;
            }

            // Tracks without a name fall back to their position in the collection.
            return Rules.FirstOrDefault(r => string.IsNullOrWhiteSpace(r.Name) && r.Index == trackIndex);
        }

        public void SetRule(string? trackName, int trackIndex, NoteKeepStrategy strategy, int limit)
        {
            Rules ??= new List<TrackSimplifyRule>();

            string name = NormalizeName(trackName);

            var existing = Rules.FirstOrDefault(r =>
                name.Length > 0
                    ? MatchKey(r.Name) == MatchKey(name)
                    : string.IsNullOrWhiteSpace(r.Name) && r.Index == trackIndex);

            if (existing == null)
            {
                existing = new TrackSimplifyRule
                {
                    Name = name,
                    Index = trackIndex
                };
                Rules.Add(existing);
            }

            existing.Name = name;

            // Position only identifies unnamed tracks; named tracks are matched by name.
            if (name.Length == 0)
                existing.Index = trackIndex;

            existing.Strategy = strategy;
            existing.Limit = ClampLimit(limit);
        }

        public static int ClampLimit(int limit)
        {
            if (limit < MinLimit) return MinLimit;
            if (limit > MaxLimit) return MaxLimit;
            return limit;
        }
    }

    internal static class NoteSelector
    {
        /// <summary>
        /// Picks which notes survive when more than <paramref name="limit"/> notes start at
        /// the same time. <paramref name="sortedAscending"/> must be ordered from lowest to highest.
        /// </summary>
        public static List<NoteOnEvent> SelectKeepNotes(
            List<NoteOnEvent> sortedAscending,
            NoteKeepStrategy strategy,
            int limit)
        {
            if (strategy == NoteKeepStrategy.Exclude)
                return new List<NoteOnEvent>();

            if (strategy == NoteKeepStrategy.Off || limit <= 0 || sortedAscending.Count <= limit)
                return new List<NoteOnEvent>(sortedAscending);

            if (strategy == NoteKeepStrategy.Lowest)
                return sortedAscending.Take(limit).ToList();

            if (strategy == NoteKeepStrategy.Highest)
                return sortedAscending.Skip(sortedAscending.Count - limit).ToList();

            if (limit == 1)
                return new List<NoteOnEvent> { sortedAscending[sortedAscending.Count / 2] };

            var keep = new List<NoteOnEvent>(limit);
            double step = (sortedAscending.Count - 1) / (double)(limit - 1);

            for (int i = 0; i < limit; i++)
            {
                int idx = (int)Math.Round(i * step);
                if (idx < 0) idx = 0;
                if (idx > sortedAscending.Count - 1) idx = sortedAscending.Count - 1;
                keep.Add(sortedAscending[idx]);
            }

            return keep;
        }
    }

    /// <summary>
    /// Editable row used by the per-track configuration window.
    /// </summary>
    internal sealed class TrackSimplifyRow : INotifyPropertyChanged
    {
        private NoteKeepStrategy _strategy = NoteKeepStrategy.Off;
        private int _limit = AdvancedSimplifyConfig.DefaultLimit;

        public TrackSimplifyRow(int trackIndex, string? trackName, string channels, int maxSimultaneous, string filesText = "")
        {
            TrackIndex = trackIndex;
            TrackName = AdvancedSimplifyConfig.NormalizeName(trackName);
            Channels = channels;
            MaxSimultaneous = maxSimultaneous;
            FilesText = filesText;
        }

        public int TrackIndex { get; }

        public string TrackName { get; }

        public string Channels { get; }

        public int MaxSimultaneous { get; }

        /// <summary>How many of the queued files contain this track (combined view only).</summary>
        public string FilesText { get; }

        public string DisplayName => TrackName.Length > 0 ? TrackName : $"Track {TrackIndex + 1}";

        public NoteKeepStrategy Strategy
        {
            get => _strategy;
            set
            {
                if (_strategy == value) return;
                _strategy = value;
                OnPropertyChanged(nameof(Strategy));
                OnPropertyChanged(nameof(IsLimitEnabled));
            }
        }

        public int Limit
        {
            get => _limit;
            set
            {
                int clamped = AdvancedSimplifyConfig.ClampLimit(value);
                if (_limit == clamped) return;
                _limit = clamped;
                OnPropertyChanged(nameof(Limit));
            }
        }

        public bool IsLimitEnabled =>
            _strategy != NoteKeepStrategy.Off && _strategy != NoteKeepStrategy.Exclude;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    internal static class MidiTrackInspector
    {
        /// <summary>
        /// Lists the non-percussion tracks of a MIDI file together with the configuration
        /// that currently applies to each of them.
        /// </summary>
        public static List<TrackSimplifyRow> Inspect(string path, AdvancedSimplifyConfig config, out int skippedTracks)
        {
            var rows = new List<TrackSimplifyRow>();
            var midi = new MidiFile(path, false);

            skippedTracks = 0;

            for (int t = 0; t < midi.Events.Tracks; t++)
            {
                string? trackName = null;
                var noteOns = new List<NoteOnEvent>();

                foreach (var ev in midi.Events[t])
                {
                    if (trackName == null &&
                        ev is TextEvent text &&
                        text.MetaEventType == MetaEventType.SequenceTrackName)
                    {
                        trackName = text.Text;
                    }

                    if (ev is NoteOnEvent on && on.Channel != 10 && on.Velocity > 0)
                        noteOns.Add(on);
                }

                if (noteOns.Count == 0)
                {
                    skippedTracks++;
                    continue;
                }

                int maxSimultaneous = noteOns
                    .GroupBy(n => n.AbsoluteTime)
                    .Select(g => g.Count())
                    .DefaultIfEmpty(0)
                    .Max();

                string channels = string.Join(", ",
                    noteOns.Select(n => n.Channel).Distinct().OrderBy(c => c));

                var row = new TrackSimplifyRow(t, trackName, channels, maxSimultaneous);

                var rule = config.FindRule(trackName, t);

                if (rule != null)
                {
                    row.Strategy = rule.Strategy;
                    row.Limit = rule.Limit;
                }
                else
                {
                    row.Strategy = config.UnmatchedStrategy;
                    row.Limit = AdvancedSimplifyConfig.DefaultLimit;
                }

                rows.Add(row);
            }

            return rows;
        }

        private sealed class CombinedTrack
        {
            public CombinedTrack(string name)
            {
                Name = name;
            }

            public string Name { get; }

            public int FileCount { get; set; }

            public int MaxSimultaneous { get; set; }

            public HashSet<int> Channels { get; } = new();
        }

        /// <summary>
        /// Builds one row per distinct named track across every queued file, so a batch
        /// can be configured once. Unnamed tracks cannot be identified reliably across
        /// files and are reported instead of listed; they are configured per file.
        /// </summary>
        public static List<TrackSimplifyRow> BuildCombinedCatalog(
            IEnumerable<string> paths,
            AdvancedSimplifyConfig config,
            out int unnamedTracks,
            out List<string> unreadableFiles)
        {
            unnamedTracks = 0;
            unreadableFiles = new List<string>();

            var allPaths = (paths ?? Enumerable.Empty<string>()).ToList();
            var order = new List<string>();
            var aggregate = new Dictionary<string, CombinedTrack>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in allPaths)
            {
                List<TrackSimplifyRow> rows;

                try
                {
                    rows = Inspect(path, config, out _);
                }
                catch
                {
                    unreadableFiles.Add(Path.GetFileName(path));
                    continue;
                }

                foreach (TrackSimplifyRow row in rows)
                {
                    if (row.TrackName.Length == 0)
                    {
                        unnamedTracks++;
                        continue;
                    }

                    string key = AdvancedSimplifyConfig.GroupKey(row.TrackName);

                    if (!aggregate.TryGetValue(key, out CombinedTrack? combined))
                    {
                        combined = new CombinedTrack(row.TrackName);
                        aggregate[key] = combined;
                        order.Add(key);
                    }

                    combined.FileCount++;
                    combined.MaxSimultaneous = Math.Max(combined.MaxSimultaneous, row.MaxSimultaneous);

                    foreach (string channel in row.Channels.Split(','))
                    {
                        if (int.TryParse(channel.Trim(), out int channelNumber))
                            combined.Channels.Add(channelNumber);
                    }
                }
            }

            var result = new List<TrackSimplifyRow>(order.Count);

            foreach (string name in order)
            {
                CombinedTrack combined = aggregate[name];

                string channels = string.Join(", ", combined.Channels.OrderBy(c => c));
                string filesText = allPaths.Count > 0
                    ? $"{combined.FileCount}/{allPaths.Count} files"
                    : $"{combined.FileCount} files";

                var row = new TrackSimplifyRow(-1, combined.Name, channels, combined.MaxSimultaneous, filesText);
                TrackSimplifyRule? rule = config.FindRule(combined.Name, -1);

                row.Strategy = rule != null ? rule.Strategy : config.UnmatchedStrategy;
                row.Limit = rule != null ? rule.Limit : AdvancedSimplifyConfig.DefaultLimit;

                result.Add(row);
            }

            return result;
        }

        /// <summary>Returns the display name for a track of an already-loaded collection.</summary>
        public static string? GetTrackName(IEnumerable<MidiEvent> trackEvents)
        {
            foreach (var ev in trackEvents)
            {
                if (ev is TextEvent text && text.MetaEventType == MetaEventType.SequenceTrackName)
                    return string.IsNullOrWhiteSpace(text.Text) ? null : text.Text;
            }

            return null;
        }
    }
}
