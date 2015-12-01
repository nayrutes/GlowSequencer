﻿using GlowSequencer.ViewModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GlowSequencer.View
{
    public class MusicSegmentCommands
    {
        public static readonly RoutedUICommand AddSegment = new RoutedUICommand("", "AddSegment", typeof(SequencerCommands));
        public static readonly RoutedUICommand DeleteSegment = new RoutedUICommand("", "DeleteSegment", typeof(SequencerCommands), new InputGestureCollection { new KeyGesture(Key.Delete) });
        public static readonly RoutedUICommand SetAsDefault = new RoutedUICommand("", "SetAsDefault", typeof(SequencerCommands), new InputGestureCollection { new KeyGesture(Key.Enter, ModifierKeys.Alt) });
    }

    public partial class MusicSegmentsWindow
    {
        private void CommandBinding_CanExecuteAlways(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = true;
        }

        private void CommandBinding_CanExecuteIfNotReadOnly(object sender, CanExecuteRoutedEventArgs e)
        {
            if (e.Parameter == null)
                return;

            e.CanExecute = !((MusicSegmentViewModel)e.Parameter).IsReadOnly;
        }

        private void CommandBinding_CanExecuteIfNotDefault(object sender, CanExecuteRoutedEventArgs e)
        {
            if (e.Parameter == null)
                return;

            e.CanExecute = !((MusicSegmentViewModel)e.Parameter).IsDefault;
        }


        private void CommandBinding_ExecuteAddSegment(object sender, ExecutedRoutedEventArgs e)
        {
            MusicSegmentViewModel newSegment = sequencer.AddMusicSegment();
            segmentsListBox.SelectedItem = newSegment;
        }

        private void CommandBinding_ExecuteDeleteSegment(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Parameter == null)
                return;

            MusicSegmentViewModel segment = (MusicSegmentViewModel)e.Parameter;

            if (segment.ReferringBlocksDummies.Count > 0)
            {
                MessageBoxResult result = MessageBox.Show(this, segment.ReferringBlocksDummies.Count + " blocks are referring to '" + segment.Label + "' and will have their music segment reset." + Environment.NewLine + "Are you sure?", "Confirmation", MessageBoxButton.YesNo);
                if (result != MessageBoxResult.Yes)
                    return;
            }

            sequencer.DeleteMusicSegment(segment);
        }


        private void CommandBinding_ExecuteSetAsDefault(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Parameter == null)
                return;

            sequencer.SetMusicSegmentAsDefault((MusicSegmentViewModel)e.Parameter);
        }
    }

}