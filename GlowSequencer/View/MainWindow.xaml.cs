﻿using GlowSequencer.Util;
using GlowSequencer.ViewModel;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace GlowSequencer.View
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private enum ScrollIntoViewMode
        {
            Edge, Center, ForPlayback
        }
        private enum BlockDragMode
        {
            None, Block, Start, End
        }
        private class DraggedBlockData
        {
            public BlockViewModel block;
            public float initialStartTime;
            public float initialDuration;
        }

        private const int SELECTION_DRAG_INITIAL_THRESHOLD = 10;
        private const int DRAG_INITIAL_THRESHOLD = 10;
        private const int DRAG_START_END_PIXEL_WINDOW = 6;
        private const int DRAG_START_END_PIXEL_WINDOW_TOUCH = 12;
        private const double TIMELINE_CURSOR_PADDING_LEFT_PX = 1;
        private const double TIMELINE_CURSOR_PADDING_RIGHT_PX = 3;

        private readonly GlobalViewParameters globalParams = (GlobalViewParameters)Application.Current.FindResource("vm_Global");
        private readonly MainViewModel main;
        private SequencerViewModel sequencer { get { return main.CurrentDocument; } }

        // Multi-selection box.
        private Point? selectionDragStart = null;
        private bool selectionIsDragging = false;
        private CompositionMode selectionCompositionMode = CompositionMode.None;

        // Dragging of blocks.
        private BlockDragMode dragMode = BlockDragMode.None;
        private Point dragStart = new Point(); // start of the drag, relative to the timeline
        private bool dragNeedsToOvercomeThreshold = false;
        private int dragTrackBaseline = -1;
        private List<DraggedBlockData> draggedBlocks = null;

        public MainWindow()
        {
            InitializeComponent();
            main = (MainViewModel)DataContext;

            SourceInitialized += (sender, e) => { RestoreWorkspaceSettings(); };
            Closing += (sender, e) => { if (!e.Cancel) SaveWorkspaceSettings(); };

            sequencer.SetViewportState(trackBlocksScroller.HorizontalOffset, trackBlocksScroller.ActualWidth);
            timeline.Focus();
        }

        private void RestoreWorkspaceSettings()
        {
            var settings = Properties.Settings.Default;
            if (settings.PropsPoppedOut)
            {
                ButtonPopOut_Click(this, default);

            }

            main.CurrentDocument.Visualization.IsEnabled = settings.VisualizationEnabled;
            if (settings.VisualizationPoppedOut)
            {
                ButtonPopOut2_Click(this, default);
            }
        }

        private void SaveWorkspaceSettings()
        {
            var settings = Properties.Settings.Default;
            settings.PropsPoppedOut = Mastermind.PoppedOutSelectionDataWindow != null;
            settings.VisualizationPoppedOut = Mastermind.PoppedOutVisualizationWindow != null;
            settings.VisualizationEnabled = main.CurrentDocument.Visualization.IsEnabled;
            settings.Save();

            Mastermind.PoppedOutSelectionDataWindow?.Close();
            Mastermind.PoppedOutVisualizationWindow?.Close();
        }

        private void trackLabelsScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            trackBlocksScroller.ScrollToVerticalOffset(e.VerticalOffset);
        }

        private void trackBlocksScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            trackLabelsScroller.ScrollToVerticalOffset(e.VerticalOffset);

            // Update viewport state in VM.
            if (e.HorizontalChange != 0)
                sequencer.SetViewportState(trackBlocksScroller.HorizontalOffset, trackBlocksScroller.ActualWidth);
        }

        private void trackBlocksScroller_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Update viewport state in VM.
            if (e.WidthChanged)
                sequencer.SetViewportState(trackBlocksScroller.HorizontalOffset, trackBlocksScroller.ActualWidth);
        }


        private void timelineTrack_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            TrackViewModel track = ((FrameworkElement)sender).DataContext as TrackViewModel;
            sequencer.SelectedTrack = track;
        }

        private void timelineTrackLabel_MouseDown(object sender, MouseButtonEventArgs e)
        {
            TrackViewModel track = ((FrameworkElement)sender).DataContext as TrackViewModel;

            // double click --> rename
            if (e.ClickCount == 2)
                SequencerCommands.RenameTrack.Execute(track, (FrameworkElement)sender);
        }




        private void timelineBlock_QueryCursor(object sender, QueryCursorEventArgs e)
        {
            FrameworkElement control = (FrameworkElement)sender;
            BlockViewModel block = (BlockViewModel)control.DataContext;

            if (sequencer.IsPipetteActive)
            {
                HandlePipetteQueryCursor(block, e);
                return;
            }

            switch (dragMode)
            {
                case BlockDragMode.Block:
                    e.Cursor = Cursors.SizeAll;
                    break;
                case BlockDragMode.Start:
                case BlockDragMode.End:
                    e.Cursor = Cursors.SizeWE;
                    break;

                case BlockDragMode.None:
                    FrameworkElement controlBlock = (FrameworkElement)VisualTreeHelper.GetChild(control, 0);
                    var localMouse = e.GetPosition(controlBlock);


                    // if the block start/end is already being dragged or if the mouse is at the left or right edge
                    if (localMouse.X < DRAG_START_END_PIXEL_WINDOW ||
                        (localMouse.X > controlBlock.ActualWidth - DRAG_START_END_PIXEL_WINDOW && localMouse.X < controlBlock.ActualWidth + DRAG_START_END_PIXEL_WINDOW))
                        e.Cursor = Cursors.SizeWE;

                    break;
            }
        }


        private CompositionMode CompositionModeFromKeyboard()
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                return CompositionMode.Additive;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                return CompositionMode.Subtractive;

            return CompositionMode.None;
        }

        private void timelineBlock_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left && e.ChangedButton != MouseButton.Right)
                return;

            FrameworkElement control = (FrameworkElement)sender;
            // Get to the actual list item, because for some reason capturing the mouse on the ContentPresenter
            // wrapper (ItemContainer) messes up the reported coordinates during MouseMove.
            FrameworkElement controlBlock = (FrameworkElement)VisualTreeHelper.GetChild(control, 0);
            BlockViewModel block = (control.DataContext as BlockViewModel);

            if (sequencer.IsPipetteActive)
            {
                HandlePipetteClick(block, controlBlock, e);
                return;
            }

            var localMouse = e.GetPosition(controlBlock);

            BlockDragMode mode;
            if (e.RightButton == MouseButtonState.Pressed)
                mode = BlockDragMode.Block;
            else if (localMouse.X > controlBlock.ActualWidth - DRAG_START_END_PIXEL_WINDOW && localMouse.X < controlBlock.ActualWidth + DRAG_START_END_PIXEL_WINDOW)
                mode = BlockDragMode.End;
            else if (localMouse.X < DRAG_START_END_PIXEL_WINDOW)
                mode = BlockDragMode.Start;
            else
                mode = BlockDragMode.None;


            if (mode == BlockDragMode.None)
            {
                IEnumerable<BlockViewModel> toSelect;
                if (e.ClickCount == 2 && block is ColorBlockViewModel)
                    toSelect = sequencer.AllBlocks.OfType<ColorBlockViewModel>().Where(other => other.Color == ((ColorBlockViewModel)block).Color);
                else if (e.ClickCount == 2 && block is RampBlockViewModel)
                    toSelect = sequencer.AllBlocks.OfType<RampBlockViewModel>().Where(other => other.StartColor == ((RampBlockViewModel)block).StartColor
                                                                                               && other.EndColor == ((RampBlockViewModel)block).EndColor);
                else
                    toSelect = Enumerable.Repeat(block, 1);

                sequencer.SelectBlocks(toSelect, CompositionModeFromKeyboard());
            }
            else
            {
                // Make sure the clicked block is always selected before dragging.
                if (!block.IsSelected)
                {
                    var compositionMode = CompositionModeFromKeyboard();
                    if (compositionMode == CompositionMode.Subtractive)
                        compositionMode = CompositionMode.None;
                    sequencer.SelectBlock(block, compositionMode);
                }

                // record initial information
                dragMode = mode;
                dragStart = e.GetPosition(timeline); // always relative to timeline
                dragNeedsToOvercomeThreshold = true;
                dragTrackBaseline = GetTrackIndexFromOffset(dragStart.Y);
                draggedBlocks = sequencer.SelectedBlocks.Select(b => new DraggedBlockData { block = b, initialDuration = b.Duration, initialStartTime = b.StartTime }).ToList();

                controlBlock.CaptureMouse();
                e.Handled = true;
            }

            //control.Focus();
        }

        private void timelineBlock_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left && e.ChangedButton != MouseButton.Right)
                return;

            if (dragMode != BlockDragMode.None)
            {
                FrameworkElement control = (FrameworkElement)sender;
                FrameworkElement controlBlock = (FrameworkElement)VisualTreeHelper.GetChild(control, 0);
                //BlockViewModel block = (BlockViewModel)control.DataContext;

                // Suppres context menu after drag.
                if (!dragNeedsToOvercomeThreshold)
                    e.Handled = true;

                // this would be the place to record the undo/redo action (1 for all blocks)

                // reset drag state
                dragMode = BlockDragMode.None;
                dragStart = new Point();
                dragTrackBaseline = -1;
                draggedBlocks = null;

                controlBlock.ReleaseMouseCapture();
            }
        }

        private void timelineBlock_MouseMove(object sender, MouseEventArgs e)
        {
            FrameworkElement control = (FrameworkElement)sender;
            FrameworkElement controlBlock = (FrameworkElement)VisualTreeHelper.GetChild(control, 0);
            BlockViewModel block = (BlockViewModel)control.DataContext;

            if (dragMode == BlockDragMode.None)
                return;
            if (!controlBlock.IsMouseCaptured) // the dragging was started by manipulation events (i guess)
                return;

            Vector delta = e.GetPosition(timeline) - dragStart;
            HandleDrag(block, delta);
        }


        private void timelineBlock_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            // Handle pipette as normal click event.
            if (sequencer.IsPipetteActive)
                return;

            FrameworkElement control = (FrameworkElement)sender;
            FrameworkElement controlBlock = (FrameworkElement)VisualTreeHelper.GetChild(control, 0);
            BlockViewModel block = (control.DataContext as BlockViewModel);

            // Make sure the clicked block is always selected before dragging.
            if (!block.IsSelected)
            {
                var compositionMode = CompositionModeFromKeyboard();
                if (compositionMode == CompositionMode.Subtractive)
                    compositionMode = CompositionMode.None;
                sequencer.SelectBlock(block, compositionMode);
            }

            var localPos = e.Manipulators.First().GetPosition(controlBlock);

            BlockDragMode mode;
            if (localPos.X > controlBlock.ActualWidth - DRAG_START_END_PIXEL_WINDOW_TOUCH && localPos.X < controlBlock.ActualWidth + DRAG_START_END_PIXEL_WINDOW_TOUCH)
                mode = BlockDragMode.End;
            else if (localPos.X < DRAG_START_END_PIXEL_WINDOW_TOUCH)
                mode = BlockDragMode.Start;
            else
                mode = BlockDragMode.Block;

            // record initial information
            dragMode = mode;
            dragStart = e.Manipulators.First().GetPosition(timeline); // always relative to timeline
            dragNeedsToOvercomeThreshold = true;
            dragTrackBaseline = GetTrackIndexFromOffset(dragStart.Y);
            draggedBlocks = sequencer.SelectedBlocks.Select(b => new DraggedBlockData { block = b, initialDuration = b.Duration, initialStartTime = b.StartTime }).ToList();


            e.Mode = ManipulationModes.Translate;
            e.Handled = true;
            // TODO [low] handling ManipulationStarting prevents double tap (to select similar) from working with touch, but not handling it stops firing ManipulationCompleted (because of PanningMode of the scroller)
        }

        private void timelineBlock_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
        {
            if (dragMode == BlockDragMode.None)
                return;

            BlockViewModel block = ((sender as FrameworkElement).DataContext as BlockViewModel);

            // this would be the place to record the undo/redo action (1 for all blocks)

            // reset drag state
            dragMode = BlockDragMode.None;
            dragStart = new Point();
            dragTrackBaseline = -1;
            draggedBlocks = null;

            e.Handled = true;
        }

        private void timelineBlock_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            if (dragMode == BlockDragMode.None)
                return;

            BlockViewModel block = ((sender as FrameworkElement).DataContext as BlockViewModel);
            // don't use CumulativeManipulation, that gets messed up when the block changes position
            HandleDrag(block, (e.Manipulators.First().GetPosition(timeline) - dragStart));

            e.Handled = true;
        }

        private void HandleDrag(BlockViewModel principal, Vector delta)
        {
            float deltaT = (float)(delta.X / sequencer.TimePixelScale);

            DraggedBlockData principalData = draggedBlocks.Single(db => db.block == principal);

            // adjust delta according to constraints
            // - grid snapping on the principal
            // - respect min duration on all blocks
            // - non-negative start time on the earliest block
            float minDurationDelta, minStartTime, snappedStartTime;
            switch (dragMode)
            {
                case BlockDragMode.Start:
                    snappedStartTime = SnapValue(principalData.initialStartTime + deltaT);
                    deltaT = snappedStartTime - principalData.initialStartTime;

                    minDurationDelta = -draggedBlocks.Min(b => b.initialDuration - b.block.GetModel().GetMinDuration());
                    if (-deltaT < minDurationDelta)
                        deltaT = -minDurationDelta;

                    minStartTime = draggedBlocks.Min(b => b.initialStartTime);
                    if (deltaT < -minStartTime)
                        deltaT = -minStartTime;
                    break;

                case BlockDragMode.End:
                    float snappedEndTime = SnapValue(principalData.initialStartTime + principalData.initialDuration + deltaT);
                    deltaT = snappedEndTime - principalData.initialStartTime - principalData.initialDuration;

                    minDurationDelta = -draggedBlocks.Min(b => b.initialDuration - b.block.GetModel().GetMinDuration());
                    if (deltaT < minDurationDelta)
                        deltaT = minDurationDelta;
                    break;
                case BlockDragMode.Block:
                    // adjust back to zero if in orthogonal drag mode
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)/* && dragTrackBaseline != GetTrackIndexFromOffset(dragStart.Y)*/)
                        deltaT = 0;
                    else
                    {
                        snappedStartTime = SnapValue(principalData.initialStartTime + deltaT);
                        deltaT = snappedStartTime - principalData.initialStartTime;

                        minStartTime = draggedBlocks.Min(b => b.initialStartTime);
                        if (deltaT < -minStartTime)
                            deltaT = -minStartTime;
                    }
                    break;
            }

            if (dragNeedsToOvercomeThreshold)
            {
                if (Math.Abs(delta.X) < DRAG_INITIAL_THRESHOLD)
                    // Note that the transaction below will still be created despite the reset,
                    // but it will be empty as no values are actually changed.
                    deltaT = 0;
                else
                    dragNeedsToOvercomeThreshold = false;
            }

            using (sequencer.ActionManager.CreateTransaction())
            {
                // apply delta to all dragged blocks
                switch (dragMode)
                {
                    case BlockDragMode.Start:
                        foreach (var b in draggedBlocks)
                        {
                            b.block.StartTime = b.initialStartTime + deltaT;
                            b.block.Duration = b.initialDuration - deltaT;
                        }
                        break;

                    case BlockDragMode.End:
                        foreach (var b in draggedBlocks)
                        {
                            b.block.Duration = b.initialDuration + deltaT;
                        }
                        break;
                    case BlockDragMode.Block:
                        foreach (var b in draggedBlocks)
                        {
                            b.block.StartTime = b.initialStartTime + deltaT;
                        }
                        break;
                }
            }


            // vertical drag
            int currentTrackIndex = GetTrackIndexFromOffset(dragStart.Y + delta.Y);
            if (dragMode == BlockDragMode.Block && currentTrackIndex != dragTrackBaseline)
            {
                int trackDelta = currentTrackIndex - dragTrackBaseline;

                int rangeMin = draggedBlocks.Min(b => b.block.GetModel().Tracks.Min(t => t.GetIndex()));
                int rangeMax = draggedBlocks.Max(b => b.block.GetModel().Tracks.Max(t => t.GetIndex()));
                Debug.WriteLine("clamping {0} to {1}", -rangeMin, sequencer.Tracks.Count - rangeMax - 1);
                trackDelta = MathUtil.Clamp(trackDelta, -rangeMin, sequencer.Tracks.Count - rangeMax - 1);

                if (trackDelta != 0)
                {
                    using (sequencer.ActionManager.CreateTransaction(false))
                    {
                        foreach (var b in draggedBlocks)
                        {
                            b.block.GetModel().ShiftTracks(trackDelta, sequencer.ActionManager);
                        }
                    }

                    // set current index as the new baseline
                    dragTrackBaseline = currentTrackIndex;
                }
            }
        }

        private bool GetIsSnapping()
        {
            return globalParams.EnableSnapping != Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        }

        private float SnapValue(float v)
        {
            if (GetIsSnapping())
            {
                float interval = sequencer.GridInterval;
                float offset = sequencer.GetGridOffset();
                return (float)Math.Round((v - offset) / interval) * interval + offset;
            }
            else
                return v;
        }

        /// <summary>Returns the (clamped) index of the track represented by an offset.</summary>
        /// <param name="offset">y offset relative to the timeline</param>
        /// <returns></returns>
        private int GetTrackIndexFromOffset(double offset)
        {
            return MathUtil.Clamp(MathUtil.FloorToInt(offset / globalParams.TrackDisplayHeight), 0, sequencer.Tracks.Count - 1);
        }

        private void waveform_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            CommandBinding_ExecuteMusicLoadFile(sender, null);
        }

        private void waveform_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left && e.ChangedButton != MouseButton.Right)
                return;
            sequencer.CursorPosition = SnapValue((float)e.GetPosition(timeline).X / sequencer.TimePixelScale);
            timeline.Focus();
        }

        private void timeline_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            int trackIndex = MathUtil.FloorToInt(e.GetPosition(timeline).Y / globalParams.TrackDisplayHeight);
            if (trackIndex >= 0 && trackIndex < sequencer.Tracks.Count)
                sequencer.SelectedTrack = sequencer.Tracks[trackIndex];
        }

        private void timeline_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left && e.ChangedButton != MouseButton.Right)
                return;
            if (sequencer.IsPipetteActive)
                return;

            // If the click happened on an empty part of the timeline and we are not delta selecting, select nothing.
            var originalDataContext = ((FrameworkElement)e.OriginalSource).DataContext;
            if (CompositionModeFromKeyboard() == CompositionMode.None
                && !(originalDataContext is BlockViewModel))
            {
                sequencer.SelectBlock(null, CompositionMode.None);
            }

            selectionDragStart = e.GetPosition(timeline);
        }

        private void timeline_MouseMove(object sender, MouseEventArgs e)
        {
            if (selectionDragStart == null)
                return; // TODO why?

            if (selectionIsDragging)
            {
                Point p1 = e.GetPosition(timeline);
                Point p2 = selectionDragStart.Value;
                Rect r = new Rect(p1, p2);

                dragSelectionRect.Visibility = Visibility.Visible;
                Canvas.SetLeft(dragSelectionRect, r.Left);
                Canvas.SetTop(dragSelectionRect, r.Top);
                dragSelectionRect.Width = r.Width;
                dragSelectionRect.Height = r.Height;

                MultiSelectBlocks(r);
                e.Handled = true;
            }
            else if (selectionDragStart != null)
            {
                Vector delta = e.GetPosition(timeline) - selectionDragStart.Value;
                if (delta.LengthSquared >= SELECTION_DRAG_INITIAL_THRESHOLD * SELECTION_DRAG_INITIAL_THRESHOLD)
                {
                    selectionIsDragging = true;
                    selectionCompositionMode = CompositionModeFromKeyboard();
                    timeline.CaptureMouse();
                    e.Handled = true;
                }
            }
        }

        private void timeline_MouseUp(object sender, MouseButtonEventArgs e)
        {
            selectionDragStart = null;

            if (selectionIsDragging)
            {
                selectionIsDragging = false;
                dragSelectionRect.Visibility = Visibility.Hidden;

                if (selectionCompositionMode != CompositionMode.None)
                {
                    sequencer.ConfirmSelectionDelta();
                    selectionCompositionMode = CompositionMode.None;
                }

                timeline.ReleaseMouseCapture();
            }
            else
            {
                // If it was a non-dragging click on empty space, move the cursor.
                var originalDataContext = ((FrameworkElement)e.OriginalSource).DataContext;
                if ((e.ChangedButton == MouseButton.Left || e.ChangedButton == MouseButton.Right)
                    && !(originalDataContext is BlockViewModel))
                {
                    sequencer.CursorPosition = SnapValue((float)e.GetPosition(timeline).X / sequencer.TimePixelScale);
                }
            }
        }

        private void waveform_MouseWheel(object sender, MouseWheelEventArgs e) => timeline_MouseWheel(sender, e);
        private void timeline_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                ChangeZoom(e.Delta * 0.1f, e);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                trackBlocksScroller.ScrollToHorizontalOffset(trackBlocksScroller.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        }

        /// <summary>Changes the zoom level by the given amount. It zooms centered around the mouseEvent if given, otherwise
        /// around the cursor position.</summary>
        private void ChangeZoom(float exponentDelta, MouseEventArgs mouseEvent = null)
        {
            double zoomCenterSeconds;
            double offsetFromEdgePixels; // where on the screen the center was located so scrolling can center onto zoomCenter properly
            if (mouseEvent != null)
            {
                double mousePosPixels = mouseEvent.GetPosition(timeline).X;
                zoomCenterSeconds = mousePosPixels / sequencer.TimePixelScale;
                offsetFromEdgePixels = mouseEvent.GetPosition(trackBlocksScroller).X;
            }
            else
            {
                zoomCenterSeconds = sequencer.CursorPosition;
                double cursorFromEdgePixels = sequencer.CursorPixelPosition - trackBlocksScroller.HorizontalOffset;
                offsetFromEdgePixels = MathUtil.Clamp(cursorFromEdgePixels,
                    TIMELINE_CURSOR_PADDING_LEFT_PX, trackBlocksScroller.ActualWidth - TIMELINE_CURSOR_PADDING_RIGHT_PX);
            }

            double currExponent = Math.Log(sequencer.TimePixelScale, 1.04f);
            sequencer.TimePixelScale = (float)Math.Pow(1.04f, currExponent + exponentDelta);

            trackBlocksScroller.ScrollToHorizontalOffset(zoomCenterSeconds * sequencer.TimePixelScale - offsetFromEdgePixels);
        }


        private void MultiSelectBlocks(Rect r)
        {
            double timeRangeStart = r.Left / sequencer.TimePixelScale;
            double timeRangeEnd = r.Right / sequencer.TimePixelScale;

            int trackRangeStart = (int)Math.Floor(r.Top / globalParams.TrackDisplayHeight); // inclusive
            int trackRangeEnd = (int)Math.Ceiling(r.Bottom / globalParams.TrackDisplayHeight); // exclusive

            sequencer.SelectBlocksDelta(sequencer.AllBlocks
                .Where(b => timeRangeStart <= b.EndTimeOccupied && timeRangeEnd >= b.StartTime
                            && b.GetModel().Tracks.Select(t => t.GetIndex()).Any(i => trackRangeStart <= i && trackRangeEnd > i)
                ),
                selectionCompositionMode
            );

            //Debug.WriteLine("[{0} - {1} | {2:0.00} - {3:0.00}]", trackRangeStart, trackRangeEnd, timeRangeStart, timeRangeEnd);
        }


        private void TimelineScrollers_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Home:
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                    {
                        sequencer.SelectedTrack = sequencer.Tracks[0];
                        ScrollSelectedTrackIntoView();
                    }
                    else
                    {
                        sequencer.CursorPosition = 0;
                        ScrollCursorIntoView(ScrollIntoViewMode.Edge);
                    }
                    e.Handled = true;
                    break;
                case Key.End:
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                    {
                        sequencer.SelectedTrack = sequencer.Tracks[sequencer.Tracks.Count];
                        ScrollSelectedTrackIntoView();
                    }
                    else
                    {
                        sequencer.CursorPixelPosition = timeline.ActualWidth - TIMELINE_CURSOR_PADDING_RIGHT_PX;
                        ScrollCursorIntoView(ScrollIntoViewMode.Edge);
                    }
                    e.Handled = true;
                    break;
                case Key.Left:
                    sequencer.CursorPosition =
                        GetIsSnapping()
                            ? SnapValue(sequencer.CursorPosition - sequencer.GridInterval * 0.51f)
                            : sequencer.CursorPosition - 2 / sequencer.TimePixelScale;

                    ScrollCursorIntoView(ScrollIntoViewMode.Edge);
                    e.Handled = true;
                    break;
                case Key.Right:
                    sequencer.CursorPosition =
                        GetIsSnapping()
                            ? SnapValue(sequencer.CursorPosition + sequencer.GridInterval * 0.51f)
                            : sequencer.CursorPosition + 2 / sequencer.TimePixelScale;

                    if (sequencer.CursorPixelPosition > timeline.ActualWidth - TIMELINE_CURSOR_PADDING_RIGHT_PX)
                        sequencer.CursorPixelPosition = timeline.ActualWidth - TIMELINE_CURSOR_PADDING_RIGHT_PX;
                    ScrollCursorIntoView(ScrollIntoViewMode.Edge);
                    e.Handled = true;
                    break;
                case Key.Up:
                    sequencer.SelectRelativeTrack(-1);
                    ScrollSelectedTrackIntoView();
                    e.Handled = true;
                    break;
                case Key.Down:
                    sequencer.SelectRelativeTrack(+1);
                    ScrollSelectedTrackIntoView();
                    e.Handled = true;
                    break;
            }
        }

        private void ScrollCursorIntoView(ScrollIntoViewMode mode)
        {
            double scrollPos = trackBlocksScroller.HorizontalOffset;
            double cursorPos = sequencer.CursorPixelPosition;
            double viewportWidth = trackBlocksScroller.ActualWidth;
            bool offToLeft = (cursorPos < scrollPos + TIMELINE_CURSOR_PADDING_LEFT_PX);
            bool offToRight = (cursorPos > scrollPos + viewportWidth - TIMELINE_CURSOR_PADDING_RIGHT_PX);

            switch (mode)
            {
                case ScrollIntoViewMode.Edge:
                    if (offToLeft)
                        trackBlocksScroller.ScrollToHorizontalOffset(cursorPos - TIMELINE_CURSOR_PADDING_LEFT_PX);
                    else if (offToRight)
                        trackBlocksScroller.ScrollToHorizontalOffset(cursorPos - viewportWidth + TIMELINE_CURSOR_PADDING_RIGHT_PX);
                    break;
                case ScrollIntoViewMode.Center:
                    if (offToLeft || offToRight)
                        trackBlocksScroller.ScrollToHorizontalOffset(cursorPos - 0.5 * viewportWidth);
                    break;
                case ScrollIntoViewMode.ForPlayback:
                    if (offToRight)
                        trackBlocksScroller.ScrollToHorizontalOffset(cursorPos - 0.3 * viewportWidth);
                    break;
                default:
                    throw new ArgumentException("unknown scroll mode!", nameof(mode));
            }
        }

        private void ScrollSelectedTrackIntoView()
        {
            int i = sequencer.SelectedTrack.GetIndex();
            if (i * globalParams.TrackDisplayHeight < trackBlocksScroller.VerticalOffset)
                trackBlocksScroller.ScrollToVerticalOffset(i * globalParams.TrackDisplayHeight);
            else if ((i + 1) * globalParams.TrackDisplayHeight > trackBlocksScroller.VerticalOffset + trackBlocksScroller.ActualHeight)
                trackBlocksScroller.ScrollToVerticalOffset((i + 1) * globalParams.TrackDisplayHeight - trackBlocksScroller.ActualHeight);
        }


        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (main.IsDirty && !ConfirmUnchanged())
                e.Cancel = true;
        }

        private void HandlePipetteQueryCursor(BlockViewModel block, QueryCursorEventArgs e)
        {
            if (block is ColorBlockViewModel || block is RampBlockViewModel)
            {
                e.Cursor = Cursors.Cross;
            }
            else
            {
                e.Cursor = Cursors.No;
            }
        }

        // Pipette feature.
        private void HandlePipetteClick(BlockViewModel block, FrameworkElement controlBlock, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (e.ChangedButton != MouseButton.Left) return;

            Color color;
            if (block is ColorBlockViewModel colorBlock)
            {
                color = colorBlock.Color;
            }
            else if (block is RampBlockViewModel rampBlock)
            {
                Point p = e.GetPosition(controlBlock);
                color = (p.X < controlBlock.ActualWidth / 2 ? rampBlock.StartColor : rampBlock.EndColor);
            }
            else
            {
                return; // Unsupported block type
            }

            sequencer.ApplyPipetteColor(color);
            sequencer.PipetteTarget = null;
        }

        // support for dropping in files

        private void Window_Drag(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (IsSingleProjectFile(files) || IsSingleMusicFile(files))
                    e.Effects = DragDropEffects.Move;
                else
                    e.Effects = DragDropEffects.None;

                e.Handled = true;
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

            if (IsSingleProjectFile(files))
            {
                if (main.IsDirty && !ConfirmUnchanged())
                    return;
                main.OpenDocument(files[0]);
            }
            else if (IsSingleMusicFile(files))
            {
                DoMusicLoadFile(files[0]);
            }
        }

        private static bool IsSingleProjectFile(string[] files)
        {
            return files.Length == 1 && files[0].EndsWith(FileSerializer.EXTENSION_PROJECT, StringComparison.InvariantCultureIgnoreCase);
        }

        private static bool IsSingleMusicFile(string[] files)
        {
            return files.Length == 1 && MUSIC_EXTENSIONS.Any(ext => files[0].EndsWith(ext, StringComparison.InvariantCultureIgnoreCase));
        }


        private GridLength colWidthWhilePoppingOut = GridLength.Auto;
        private GridLength colWidthWhilePoppingOut2 = GridLength.Auto;
        private GridLength lowerAreaHeightWhileCollapsing = GridLength.Auto;

        private void ButtonPopOut_Click(object sender, RoutedEventArgs e)
        {
            Mastermind.OpenPoppedOutSelectionPropertiesWindow(
                selectionDataContent.ActualWidth,
                selectionDataContent.ActualHeight,
                ReintegratePoppedOut);

            selectionDataContent.Visibility = Visibility.Collapsed;
            colWidthWhilePoppingOut = selectionDataColumn.Width;
            selectionDataColumn.Width = new GridLength(0);
            selectionDataColumn.MaxWidth = 0;
            UpdateLowerAreaCollapse();
        }

        private void ReintegratePoppedOut(Window win)
        {
            selectionDataContent.Visibility = Visibility.Visible;
            selectionDataColumn.Width = colWidthWhilePoppingOut;
            selectionDataColumn.MaxWidth = double.PositiveInfinity;
            UpdateLowerAreaCollapse();
        }

        private void ButtonPopOut2_Click(object sender, RoutedEventArgs e)
        {
            Mastermind.OpenPoppedOutVisualizationWindow(
                visualizationContent.ActualWidth,
                visualizationContent.ActualHeight,
                ReintegratePoppedOut2);

            visualizationContent.Visibility = Visibility.Collapsed;
            colWidthWhilePoppingOut2 = visualizationColumn.Width;
            visualizationColumn.Width = new GridLength(0);
            visualizationColumn.MaxWidth = 0;
            UpdateLowerAreaCollapse();
        }

        private void ReintegratePoppedOut2(Window win)
        {
            visualizationContent.Visibility = Visibility.Visible;
            visualizationColumn.Width = colWidthWhilePoppingOut2;
            visualizationColumn.MaxWidth = double.PositiveInfinity;
            UpdateLowerAreaCollapse();
        }

        private void UpdateLowerAreaCollapse()
        {
            if (selectionDataContent.Visibility == Visibility.Collapsed && visualizationContent.Visibility == Visibility.Collapsed)
            {
                lowerAreaHeightWhileCollapsing = lowerAreaRow.Height;
                lowerAreaRow.Height = new GridLength(0);
                lowerAreaRow.MaxHeight = 0;
            }
            else
            {
                lowerAreaRow.Height = lowerAreaHeightWhileCollapsing;
                lowerAreaRow.MaxHeight = double.PositiveInfinity;
            }
        }

        private void selectionCursor_TargetUpdated(object sender, DataTransferEventArgs e)
        {
            if (e.Property == Canvas.LeftProperty && sequencer.Playback.IsPlaying)
            {
                ScrollCursorIntoView(ScrollIntoViewMode.ForPlayback);
            }
        }
    }
}
