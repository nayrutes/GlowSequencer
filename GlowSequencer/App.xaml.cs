﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace GlowSequencer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        //private void TrackCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        //{
        //    CheckBox cb = (CheckBox)sender;
        //    return;
        //    var data = cb.DataContext as Tuple<ViewModel.TrackViewModel, bool, ViewModel.BlockViewModel>;

        //    if (cb.IsChecked.Value)
        //        data.Item3.AddToTrack(data.Item1);
        //    else if (data.Item3.GetModel().Tracks.Count > 1)
        //        data.Item3.RemoveFromTrack(data.Item1);
        //    else
        //        cb.IsChecked = true;
        //}

        private void AnyContextMenu_Opening(object sender, ContextMenuEventArgs e)
        {
            //UIElement placementTarget = ((ContextMenu)sender).PlacementTarget;
            //FocusManager.SetFocusedElement(placementTarget, placementTarget);
        }
    }
}