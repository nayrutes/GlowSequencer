﻿using GlowSequencer.Util;
using GlowSequencer.ViewModel;
using System;
using System.Collections.Generic;
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

        private const int DRAG_START_END_PIXEl_WINDOW = 6;
        private const int DRAG_START_END_PIXEl_WINDOW_TOUCH = 12;
        private const double TIMELINE_CURSOR_PADDING_LEFT_PX = 1;
        private const double TIMELINE_CURSOR_PADDING_RIGHT_PX = 3;

        private readonly GlobalViewParameters globalParams = (GlobalViewParameters)Application.Current.FindResource("vm_Global");
        private MainViewModel main;
        private SequencerViewModel sequencer { get { return main.CurrentDocument; } }

        private Point? selectionDragStart = null;
        private bool selectionIsDragging = false;
        private CompositionMode selectionCompositionMode = CompositionMode.None;

        private BlockDragMode dragMode = BlockDragMode.None;
        private Point dragStart = new Point(); // start of the drag, relative to the timeline
        private int dragTrackBaseline = -1;
        private List<DraggedBlockData> draggedBlocks = null;


        public MainWindow()
        {
            InitializeComponent();
            main = (MainViewModel)DataContext;

            sequencer.SetViewportState(trackBlocksScroller.HorizontalOffset, trackBlocksScroller.ActualWidth);
            timeline.Focus();
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
                    FrameworkElement control = (FrameworkElement)sender;
                    BlockViewModel block = (BlockViewModel)control.DataContext;

                    FrameworkElement controlBlock = (FrameworkElement)VisualTreeHelper.GetChild(control, 0);
                    var localMouse = e.GetPosition(controlBlock);


                    // if the block start/end is already being dragged or if the mouse is at the left or right edge
                    if (localMouse.X < DRAG_START_END_PIXEl_WINDOW ||
                        (localMouse.X > controlBlock.ActualWidth - DRAG_START_END_PIXEl_WINDOW && localMouse.X < controlBlock.ActualWidth + DRAG_START_END_PIXEl_WINDOW))
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
            FrameworkElement controlBlock = (FrameworkElement)VisualTreeHelper.GetChild(control, 0);
            BlockViewModel block = (control.DataContext as BlockViewModel);

            var localMouse = e.GetPosition(controlBlock);

            BlockDragMode mode;
            if (e.RightButton == MouseButtonState.Pressed)
                mode = BlockDragMode.Block;
            else if (localMouse.X > controlBlock.ActualWidth - DRAG_START_END_PIXEl_WINDOW && localMouse.X < controlBlock.ActualWidth + DRAG_START_END_PIXEl_WINDOW)
                mode = BlockDragMode.End;
            else if (localMouse.X < DRAG_START_END_PIXEl_WINDOW)
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
                if (!block.IsSelected)
                    sequencer.SelectBlock(block, CompositionModeFromKeyboard());

                // record initial information
                dragMode = mode;
                dragStart = e.GetPosition(timeline); // always relative to timeline
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

                // suppress context menu
                if (!dragStart.Equals(e.GetPosition(timeline)))
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
            FrameworkElement control = (FrameworkElement)sender;
            FrameworkElement controlBlock = (FrameworkElement)VisualTreeHelper.GetChild(control, 0);
            BlockViewModel block = (control.DataContext as BlockViewModel);

            if (!block.IsSelected)
                sequencer.SelectBlock(block, CompositionModeFromKeyboard());

            var localPos = e.Manipulators.First().GetPosition(controlBlock);

            BlockDragMode mode;
            if (localPos.X > controlBlock.ActualWidth - DRAG_START_END_PIXEl_WINDOW_TOUCH && localPos.X < controlBlock.ActualWidth + DRAG_START_END_PIXEl_WINDOW_TOUCH)
                mode = BlockDragMode.End;
            else if (localPos.X < DRAG_START_END_PIXEl_WINDOW_TOUCH)
                mode = BlockDragMode.Start;
            else
                mode = BlockDragMode.Block;

            // record initial information
            dragMode = mode;
            dragStart = e.Manipulators.First().GetPosition(timeline); // always relative to timeline
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
            // - repect min duration on all blocks
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

            sequencer.CursorPosition = SnapValue((float)e.GetPosition(timeline).X / sequencer.TimePixelScale);

            var originalDC = ((FrameworkElement)e.OriginalSource).DataContext;
            // if the click happened on an empty part of the timeline and we are not delta selecting, select nothing
            if (CompositionModeFromKeyboard() == CompositionMode.None
                && (originalDC == null || !(originalDC is BlockViewModel)))
            {
                sequencer.SelectBlock(null, CompositionMode.None);
            }

            selectionDragStart = e.GetPosition(timeline);
            timeline.CaptureMouse();
            //Debug.WriteLine("d: set start point");
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
                if (delta.LengthSquared > 10 * 10)
                {
                    selectionIsDragging = true;
                    selectionCompositionMode = CompositionModeFromKeyboard();
                    e.Handled = true;
                }
            }
        }

        private void timeline_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // for some reason this has to be above the if statement below
            // ReleaseMouseCapture() apparently instantly calls the MouseMove handler or sth
            if (selectionDragStart != null)
            {
                selectionDragStart = null;
                timeline.ReleaseMouseCapture();
            }
            if (selectionIsDragging)
            {
                selectionIsDragging = false;
                dragSelectionRect.Visibility = Visibility.Hidden;

                if (selectionCompositionMode != CompositionMode.None)
                {
                    sequencer.ConfirmSelectionDelta();
                    selectionCompositionMode = CompositionMode.None;
                }
            }
        }

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
                        ScrollCursorIntoView();
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
                        ScrollCursorIntoView();
                    }
                    e.Handled = true;
                    break;
                case Key.Left:
                    sequencer.CursorPosition =
                        GetIsSnapping()
                            ? SnapValue(sequencer.CursorPosition - sequencer.GridInterval * 0.51f)
                            : sequencer.CursorPosition - 2 / sequencer.TimePixelScale;

                    ScrollCursorIntoView();
                    e.Handled = true;
                    break;
                case Key.Right:
                    sequencer.CursorPosition =
                        GetIsSnapping()
                            ? SnapValue(sequencer.CursorPosition + sequencer.GridInterval * 0.51f)
                            : sequencer.CursorPosition + 2 / sequencer.TimePixelScale;

                    if (sequencer.CursorPixelPosition > timeline.ActualWidth - TIMELINE_CURSOR_PADDING_RIGHT_PX)
                        sequencer.CursorPixelPosition = timeline.ActualWidth - TIMELINE_CURSOR_PADDING_RIGHT_PX;
                    ScrollCursorIntoView();
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

        private void ScrollCursorIntoView()
        {
            if (sequencer.CursorPixelPosition < trackBlocksScroller.HorizontalOffset + TIMELINE_CURSOR_PADDING_LEFT_PX)
                trackBlocksScroller.ScrollToHorizontalOffset(sequencer.CursorPixelPosition - TIMELINE_CURSOR_PADDING_LEFT_PX);
            else if (sequencer.CursorPixelPosition > trackBlocksScroller.HorizontalOffset + trackBlocksScroller.ActualWidth - TIMELINE_CURSOR_PADDING_RIGHT_PX)
                trackBlocksScroller.ScrollToHorizontalOffset(sequencer.CursorPixelPosition - trackBlocksScroller.ActualWidth + TIMELINE_CURSOR_PADDING_RIGHT_PX);
        }

        // same as above, but jumps further ahead when scrolling to the right
        private void ScrollCursorIntoViewForPlayback()
        {
            if (sequencer.CursorPixelPosition > trackBlocksScroller.HorizontalOffset + trackBlocksScroller.ActualWidth - TIMELINE_CURSOR_PADDING_RIGHT_PX)
                trackBlocksScroller.ScrollToHorizontalOffset(sequencer.CursorPixelPosition - 0.3 * trackBlocksScroller.ActualWidth + TIMELINE_CURSOR_PADDING_RIGHT_PX);
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

        private void statusBarTimeValue_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // at some point, double clicking the readonly values could convert them into a text field to adjust the view/cursor position by entering values;
            // the feature was postponed because it was deemed low priority; this is the start of its implementation
#if DEBUG
            // find first binding of a descendant
            DependencyObject dep = (DependencyObject)e.Source;
            BindingExpression binding = null;
            do
            {
                if (VisualTreeHelper.GetChildrenCount(dep) < 1)
                {
                    MessageBox.Show("Internal error: Could not locate property to modify!");
                    return;
                }
                dep = VisualTreeHelper.GetChild(dep, 0);
                if (dep is FrameworkElement)
                {
                    var fe = (FrameworkElement)dep;
                    binding = fe.GetBindingExpression(TextBlock.TextProperty) ?? fe.GetBindingExpression(Run.TextProperty);
                }
            } while (binding == null);

            MessageBox.Show(binding.ResolvedSourcePropertyName + " = " + binding.ResolvedSource);
#endif
        }


        private void WaveFormControl_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            CommandBinding_ExecuteMusicLoadFile(sender, null);
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
                ScrollCursorIntoViewForPlayback();
            }
        }
    }
}
