using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.Midi;

namespace OctaShift
{
    /// <summary>
    /// Applies the "Limit simultaneous notes" feature to a MIDI event collection.
    /// </summary>
    internal static class NoteSimplifier
    {
        /// <summary>
        /// Auto mode: limits notes that start at the exact same time across all tracks
        /// together, keeping the existing low/middle/high spread.
        /// </summary>
        public static void ApplyAuto(MidiEventCollection events, int maxSimultaneous)
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

            var toRemove = new List<(int Track, NoteOnEvent Note)>();

            foreach (var group in groups)
            {
                var notesAtTime = group
                    .OrderBy(n => n.Note.NoteNumber)
                    .ToList();

                if (notesAtTime.Count <= maxSimultaneous)
                    continue;

                var keep = NoteSelector.SelectKeepNotes(
                    notesAtTime.Select(n => n.Note).ToList(),
                    NoteKeepStrategy.Default,
                    maxSimultaneous);

                var keepSet = new HashSet<NoteOnEvent>(keep);

                toRemove.AddRange(notesAtTime.Where(n => !keepSet.Contains(n.Note)));
            }

            RemoveNotes(events, toRemove);
        }

        /// <summary>
        /// Advanced mode: applies the configured strategy to each track separately.
        /// Must run while the original track layout still exists (before merging).
        /// Tracks without a matching rule are governed by <see cref="AdvancedSimplifyConfig.UnmatchedStrategy"/>.
        /// </summary>
        public static void ApplyPerTrack(MidiEventCollection events, AdvancedSimplifyConfig config)
        {
            if (config == null) return;

            for (int t = 0; t < events.Tracks; t++)
            {
                string? trackName = MidiTrackInspector.GetTrackName(events[t]);
                TrackSimplifyRule? rule = config.FindRule(trackName, t);

                NoteKeepStrategy strategy = rule != null ? rule.Strategy : config.UnmatchedStrategy;
                int limit = rule != null ? rule.Limit : AdvancedSimplifyConfig.DefaultLimit;

                if (strategy == NoteKeepStrategy.Off || limit <= 0)
                    continue;

                ApplyToTrack(events, t, strategy, limit);
            }
        }

        private static void ApplyToTrack(MidiEventCollection events, int track, NoteKeepStrategy strategy, int limit)
        {
            var noteOns = new List<NoteOnEvent>();

            foreach (var ev in events[track])
            {
                if (ev is NoteOnEvent on && on.Channel != 10 && on.Velocity > 0)
                    noteOns.Add(on);
            }

            if (strategy == NoteKeepStrategy.Exclude)
            {
                RemoveNotes(events, noteOns.Select(n => (track, n)).ToList());
                return;
            }

            var toRemove = new List<(int Track, NoteOnEvent Note)>();

            foreach (var group in noteOns.GroupBy(n => n.AbsoluteTime))
            {
                var notesAtTime = group
                    .OrderBy(n => n.NoteNumber)
                    .ToList();

                if (notesAtTime.Count <= limit)
                    continue;

                var keep = NoteSelector.SelectKeepNotes(notesAtTime, strategy, limit);
                var keepSet = new HashSet<NoteOnEvent>(keep);

                toRemove.AddRange(notesAtTime
                    .Where(n => !keepSet.Contains(n))
                    .Select(n => (track, n)));
            }

            RemoveNotes(events, toRemove);
        }

        /// <summary>
        /// Removes the dropped notes together with their matching note-off events and
        /// re-sorts the affected tracks.
        /// </summary>
        private static void RemoveNotes(MidiEventCollection events, List<(int Track, NoteOnEvent Note)> toRemove)
        {
            var touchedTracks = new HashSet<int>();

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
    }
}
