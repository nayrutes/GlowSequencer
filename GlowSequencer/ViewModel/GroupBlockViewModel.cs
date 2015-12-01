﻿using ContinuousLinq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace GlowSequencer.ViewModel
{
    public abstract class GroupBlockViewModel : BlockViewModel
    {
        protected new Model.GroupBlock model;

        private ReadOnlyContinuousCollection<BlockViewModel> _children;

        public ReadOnlyContinuousCollection<BlockViewModel> Children { get { return _children; } }

        public override MusicSegmentViewModel SegmentContext
        {
            get { return base.SegmentContext; }
            set {/* noop */}
        }

        public override bool IsSegmentActive { get { return true; } }

        public GroupBlockViewModel(SequencerViewModel sequencer, Model.GroupBlock model)
            : this(sequencer, model, "Group")
        { }

        protected GroupBlockViewModel(SequencerViewModel sequencer, Model.GroupBlock model, string typeLabel)
            : base(sequencer, model, typeLabel)
        {
            this.model = model;
            _children = model.Children.Select(b => BlockViewModel.FromModel(sequencer, b));
        }

        public override void OnTracksCollectionChanged()
        {
            base.OnTracksCollectionChanged();
            foreach (var child in _children)
                child.OnTracksCollectionChanged();
        }

    }
}