﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GlowSequencer.Model;
using System.Xml.Linq;

namespace GlowSequencer.Model
{
    public abstract class Block : Observable, ICloneable
    {
        public const float MIN_DURATION_TECHNICAL_LIMIT = 0.01f;

        protected readonly Timeline timeline;

        private float _startTime = 0;
        private float _duration = MIN_DURATION_TECHNICAL_LIMIT;
        private MusicSegment _segmentContext;
        private ObservableCollection<Track> _tracks = new ObservableCollection<Track>();

        public float StartTime { get { return _startTime; } set { SetProperty(ref _startTime, Math.Max(0, value)); } }
        public float Duration { get { return _duration; } set { SetProperty(ref _duration, Math.Max(GetMinDuration(), value)); } }

        public virtual MusicSegment SegmentContext { get { return _segmentContext; } set { SetProperty(ref _segmentContext, value); } }

        public ObservableCollection<Track> Tracks { get { return _tracks; } }

        /// <summary>Pseudo-property that always returns true but will be notified whenever the Tracks collection changes.</summary>
        public bool TrackNotificationPlaceholder => true;


        public Block(Timeline timeline, params Track[] tracks)
        {
            this.timeline = timeline;

            foreach (var t in tracks)
                _tracks.Add(t);

            _tracks.CollectionChanged += (sender, e) => Notify(nameof(TrackNotificationPlaceholder));

            _segmentContext = timeline.DefaultMusicSegment;
        }

        public float GetEndTime()
        {
            return _startTime + _duration;
        }

        public virtual float GetEndTimeOccupied()
        {
            return GetEndTime();
        }

        public virtual float GetMinDuration()
        {
            return MIN_DURATION_TECHNICAL_LIMIT;
        }

        public bool IsTimeInOccupiedRange(float time)
        {
            return StartTime <= time && time < GetEndTimeOccupied();
        }

        /// <summary>Calculates the block's color at some time during the block.</summary>
        /// <param name="time">must be within range</param>
        /// <param name="track">must be contained in the block</param>
        public GloColor GetColorAtTime(float time, Track track)
        {
            // Performance optimization: only validate args in debug mode.
#if DEBUG
            if (!IsTimeInOccupiedRange(time)) throw new ArgumentOutOfRangeException(nameof(time));
            if (!Tracks.Contains(track)) throw new ArgumentException("invalid track");
#endif

            return GetColorAtLocalTimeCore(time - StartTime, track);
        }

        /// <summary>Note that we use local time here instead of global time.</summary>
        protected abstract GloColor GetColorAtLocalTimeCore(float localTime, Track track);

        public virtual void ExtendToTrack(Track fromTrack, Track toTrack, GuiLabs.Undo.ActionManager am = null)
        {
            if (Tracks.Contains(toTrack))
                throw new InvalidOperationException("block is already part of track");

            //Tracks.Add(toTrack);
            am.RecordAdd(Tracks, toTrack);
        }

        public virtual bool RemoveFromTrack(Track track, GuiLabs.Undo.ActionManager am = null)
        {
            if (Tracks.Count <= 1)
                return false;
            if (!Tracks.Contains(track))
                throw new InvalidOperationException("block is not part of track");

            am.RecordRemove(Tracks, track);
            return true;
        }

        public virtual void ShiftTracks(int delta, GuiLabs.Undo.ActionManager am = null)
        {
            var oldIndices = Tracks.Select(t => t.GetIndex()).ToList();
            var newIndices = oldIndices.Select(i => i + delta).ToList();

            if (newIndices.Any(i => i < 0 || i >= timeline.Tracks.Count))
                throw new ArgumentException("shifting tracks would move them out of range", "delta");

            // runtime complexity is horrendeous, but we don't have that many tracks ...
            foreach (int i in newIndices)
                if (!oldIndices.Contains(i))
                    am.RecordAdd(Tracks, timeline.Tracks[i]);
            foreach (int i in oldIndices)
                if (!newIndices.Contains(i))
                    am.RecordRemove(Tracks, timeline.Tracks[i]);
        }

        public object Clone()
        {
            return FromXML(timeline, ToXML());
        }

        internal virtual IEnumerable<FileSerializer.PrimitiveBlock> BakePrimitive(Track track)
        {
            return Enumerable.Empty<FileSerializer.PrimitiveBlock>();
        }

        [Obsolete("replaced with new algorithm that uses BakePrimitive")]
        public abstract IEnumerable<GloCommand> ToGloCommands(GloSequenceContext context);

        public virtual XElement ToXML()
        {
            XElement elem = new XElement("block");
            elem.SetAttributeValue("type", XMLTYPES_BY_TYPE[GetType()]);
            elem.Add(new XElement("start-time", _startTime));
            elem.Add(new XElement("duration", _duration));
            elem.Add(new XElement("segment-context", _segmentContext.GetIndex()));
            elem.Add(new XElement("affected-tracks", Tracks.Select(t => new XElement("track-reference", t.GetIndex()))));

            return elem;
        }

        protected virtual void PopulateFromXML(XElement element)
        {
            _startTime = (float)element.Element("start-time");
            _duration = (float)element.Element("duration");
            int segmentIndex = (int?)element.Element("segment-context") ?? 0;
            _segmentContext = timeline.MusicSegments[Math.Min(Math.Max(segmentIndex, 0), timeline.MusicSegments.Count - 1)];
        }

        public static Block FromXML(Timeline timeline, XElement element)
        {
            string xmlType = (string)element.Attribute("type");

            Track[] tracks;
            if (element.Element("affected-tracks") != null)
                tracks = element.Element("affected-tracks").Elements("track-reference").Select(g => timeline.Tracks[(int)g]).ToArray();
            else
                tracks = null; // aggregate blocks

            Block b = GENERATORS_BY_XMLTYPE[xmlType](timeline, tracks);
            b.PopulateFromXML(element);

            return b;
        }

        private static readonly IReadOnlyDictionary<Type, string> XMLTYPES_BY_TYPE = new Dictionary<Type, string>
        {
            {typeof(ColorBlock), "color"},
            {typeof(RampBlock), "ramp"},
            {typeof(GroupBlock), "group"},
            {typeof(LoopBlock), "loop"},
            {typeof(SubsequenceBlock), "subsequence"},
        };

        private static readonly IReadOnlyDictionary<string, Func<Timeline, Track[], Block>> GENERATORS_BY_XMLTYPE = new Dictionary<string, Func<Timeline, Track[], Block>>
        {
            {"color", (t, g) => new ColorBlock(t, g)},
            {"ramp", (t, g) => new RampBlock(t, g)},
            //{"group", (t, g) => new GroupBlock(t)},
            {"loop", (t, g) => new LoopBlock(t)},
            {"subsequence", (t, g) => new SubsequenceBlock(t, g)},
        };

    }
}
