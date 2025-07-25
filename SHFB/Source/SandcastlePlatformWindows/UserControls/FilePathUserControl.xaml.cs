﻿//===============================================================================================================
// System  : Sandcastle Tools - Windows platform specific code
// File    : FilePathUserControl.cs
// Author  : Eric Woodruff
// Updated : 06/19/2025
// Note    : Copyright 2011-2025, Eric Woodruff, All rights reserved
//
// This file contains a user control used to select a file
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://GitHub.com/EWSoftware/SHFB.  This
// notice, the author's name, and all copyright notices must remain intact in all applications, documentation,
// and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 04/15/2011  EFW  Created the code
// 10/31/2017  EFW  Converted the control to WPF for better high DPI scaling support on 4K displays
// 05/10/2021  EFW  Moved the code to the Windows platform assembly from SandcastleBuilder.WPF
//===============================================================================================================

using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

using Microsoft.Win32;

using Sandcastle.Core;

namespace Sandcastle.Platform.Windows.UserControls
{
    /// <summary>
    /// This user control is used to select a file
    /// </summary>
    [Description("This control is used to select a file"), DefaultProperty("PersistablePath"),
      DefaultEvent("PersistablePathChanged")]
    public partial class FilePathUserControl : UserControl
    {
        #region Private data members
        //=====================================================================

        private bool showFixedPathOption, changingFixedState;

        #endregion

        #region Properties
        //=====================================================================

        /// <summary>
        /// This is used to get or set the whether to use a File Open dialog
        /// </summary>
        /// <value>If false, a Save As dialog is used instead</value>
        [Category("File Browser"), Description("Specify whether or not to use the File Open dialog.  If false, " +
          "a Save As dialog is used instead"), DefaultValue(true)]
        public bool UseFileOpenDialog { get; set; }

        /// <summary>
        /// This is used to get or set the title of the file dialog
        /// </summary>
        [Category("File Browser"), Description("A title for the file dialog"), DefaultValue("Select a file")]
        public string Title { get; set; }

        /// <summary>
        /// This is used to get or set the filter for the file dialog
        /// </summary>
        [Category("File Browser"), Description("A filter for the file dialog"), DefaultValue("All Files (*.*)|*.*")]
        public string Filter { get; set; }

        /// <summary>
        /// This is used to get or set whether or not to show the fixed path checkbox and expanded path
        /// </summary>
        [Category("File Browser"), Description("Show or hide the Fixed Path option"), DefaultValue(true)]
        public bool ShowFixedPathOption
        {
            get => showFixedPathOption;
            set
            {
                showFixedPathOption = value;
                grdFixPathInfo.Visibility = showFixedPathOption ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// This is used to get or set the default folder from which to start browsing
        /// </summary>
        [Category("File Browser"), Description("The default folder from which to start browsing"),
            DefaultValue(Environment.SpecialFolder.MyDocuments)]
        public Environment.SpecialFolder DefaultFolder { get; set; }

        /// <summary>
        /// This is used to get or set whether or not to use a fixed path
        /// </summary>
        /// <remarks>Changes are applied directly to the data context</remarks>
        [Browsable(false)]
        public bool IsFixedPath
        {
            get
            {
                if(this.DataContext is FilePath fp)
                    return fp.IsFixedPath;

                return false;
            }
            set
            {
                if(this.DataContext is FilePath fp)
                    fp.IsFixedPath = value;
            }
        }

        /// <summary>
        /// This is used to get or set the path in string form
        /// </summary>
        /// <remarks>Changes are applied directly to the data context</remarks>
        [Browsable(false)]
        public string PersistablePath
        {
            get
            {
                if(this.DataContext is FilePath fp)
                    return fp.PersistablePath;

                return null;
            }
            set
            {
                if(this.DataContext is FilePath fp)
                {
                    fp.PersistablePath = value;

                    // If set to a rooted path, set the Fixed Path option
                    bool isFixed = !String.IsNullOrWhiteSpace(value) && Path.IsPathRooted(value);

                    if(isFixed != fp.IsFixedPath)
                        fp.IsFixedPath = isFixed;
                }
            }
        }
        #endregion

        #region Events
        //=====================================================================

        /// <summary>
        /// This event is raised when the file path changes
        /// </summary>
        [Category("Action"), Description("Raised when the file path changes")]
        public event EventHandler PersistablePathChanged;

        #endregion

        #region Constructor
        //=====================================================================

        /// <summary>
        /// Constructor
        /// </summary>
        public FilePathUserControl()
        {
            InitializeComponent();

            this.Title = "Select a file";
            this.Filter = "All Files (*.*)|*.*";
            this.UseFileOpenDialog = this.ShowFixedPathOption = true;
            this.DefaultFolder = Environment.SpecialFolder.MyDocuments;
        }
        #endregion

        #region Event handlers
        //=====================================================================

        /// <summary>
        /// Connect the persistable path changed event when the data context changes
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The event arguments</param>
        private void FilePathUserControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if(e.OldValue is FilePath op)
                op.PersistablePathChanged -= this.filePath_PersistablePathChanged;

            if(e.NewValue is FilePath np)
            {
                np.PersistablePathChanged += this.filePath_PersistablePathChanged;

                if(String.IsNullOrWhiteSpace(np.PersistablePath))
                    lblExpandedPath.Text = "(Not specified)";
                else
                    lblExpandedPath.Text = $"{np.PersistablePath} ({(np.Exists ? "Exists" : "Does not exist")})";

                changingFixedState = true;
                chkFixedPath.IsChecked = np.IsFixedPath;
                changingFixedState = false;
            }
        }

        /// <summary>
        /// Set the focused control when the user control gains the focus
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The event arguments</param>
        private void FilePathUserControl_PreviewGotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
        {
            if(e.NewFocus == this)
            {
                txtFile.Focus();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Handle changes to the path object
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The event arguments</param>
        private void filePath_PersistablePathChanged(object sender, EventArgs e)
        {
            FilePath fp = (FilePath)sender;

            if(String.IsNullOrWhiteSpace(fp.PersistablePath))
                lblExpandedPath.Text = "(Not specified)";
            else
                lblExpandedPath.Text = $"{fp.PersistablePath} ({(fp.Exists ? "Exists" : "Does not exist")})";

            // Keep the fixed state in synch.  This isn't bound as I wasn't able to keep it's state consistent
            // when using a binding.
            changingFixedState = true;
            chkFixedPath.IsChecked = fp.IsFixedPath;
            changingFixedState = false;

            this.PersistablePathChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Prompt the user to select a file
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The event arguments</param>
        private void btnSelectFile_Click(object sender, RoutedEventArgs e)
        {
            FileDialog dlg;
            string filename, path;

            if(!String.IsNullOrWhiteSpace(this.PersistablePath))
            {
                filename = Path.GetFileName(this.PersistablePath);
                path = Path.GetDirectoryName(this.PersistablePath);
            }
            else
                filename = path = String.Empty;

            if(String.IsNullOrWhiteSpace(path))
                path = Environment.GetFolderPath(this.DefaultFolder);
            else
            {
                if(!Path.IsPathRooted(path))
                    path = Path.GetFullPath(path);
            }

            if(this.UseFileOpenDialog)
                dlg = new OpenFileDialog();
            else
                dlg = new SaveFileDialog();

            dlg.RestoreDirectory = true;
            dlg.FileName = filename;
            dlg.InitialDirectory = path;
            dlg.Title = !String.IsNullOrWhiteSpace(this.Title) ? this.Title : "Select a file";
            dlg.Filter = !String.IsNullOrWhiteSpace(this.Filter) ? this.Filter : "All files (*.*)|*.*";

            if(dlg.ShowDialog() ?? false)
            {
                if(this.DataContext is FilePath fp)
                    fp.PersistablePath = dlg.FileName;
            }
        }

        /// <summary>
        /// Keep the fixed path setting in sync with the checkbox
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The event arguments</param>
        private void chkFixedPath_Click(object sender, RoutedEventArgs e)
        {
            // I couldn't get it to work consistently with a binding so it's handled manually
            if(!changingFixedState)
                this.IsFixedPath = chkFixedPath.IsChecked ?? false;
        }
        #endregion
    }
}
