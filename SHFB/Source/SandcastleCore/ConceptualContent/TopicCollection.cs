//===============================================================================================================
// System  : Sandcastle Tools - Sandcastle Tools Core Class Library
// File    : TopicCollection.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 07/08/2025
// Note    : Copyright 2008-2025, Eric Woodruff, All rights reserved
//
// This file contains a collection class used to hold the conceptual content topics for a project.
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://GitHub.com/EWSoftware/SHFB.  This
// notice, the author's name, and all copyright notices must remain intact in all applications, documentation,
// and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 04/24/2008  EFW  Created the code
// 08/07/2008  EFW  Modified for use with the new project format
// 06/06/2010  EFW  Added support for multi-format build output
// 07/09/2010  EFW  Updated for use with .NET 4.0 and MSBuild 4.0.
// 12/15/2011  EFW  Updated for use with the new content layout editor
// 01/01/2013  EFW  Added support for getting referenced namespaces from the topics
//===============================================================================================================

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Xml;

using Sandcastle.Core.Project;

namespace Sandcastle.Core.ConceptualContent
{
    /// <summary>
    /// This collection class is used to hold the conceptual content topics for a project
    /// </summary>
    public class TopicCollection : BindingList<Topic>, ITableOfContents
    {
        #region Private data members
        //=====================================================================

        private readonly ContentFile contentLayoutFile;

        #endregion

        #region Properties
        //=====================================================================

        /// <summary>
        /// This is used to get the default topic
        /// </summary>
        /// <value>It returns the default topic or null if one is not set</value>
        public Topic DefaultTopic
        {
            get
            {
                foreach(Topic t in this)
                {
                    if(t.IsDefaultTopic)
                        return t;

                    var defaultTopic = t.Subtopics.DefaultTopic;

                    if(defaultTopic != null)
                        return defaultTopic;
                }

                return null;
            }
        }

        /// <summary>
        /// This is used to get the topic at which the API table of contents is to be inserted or parented
        /// </summary>
        /// <value>This will return null if no parent location has been set</value>
        public Topic ApiContentInsertionPoint
        {
            get
            {
                foreach(Topic t in this)
                {
                    if(t.ApiParentMode != ApiParentMode.None)
                        return t;

                    var childInsertionPoint = t.Subtopics.ApiContentInsertionPoint;

                    if(childInsertionPoint != null)
                        return childInsertionPoint;
                }

                return null;
            }
        }

        /// <summary>
        /// This is used to get the topic that will serve as the root content container in MS Help Viewer output
        /// </summary>
        /// <value>This will return null if one is not defined</value>
        public Topic MSHVRootContentContainer
        {
            get
            {
                foreach(Topic t in this)
                {
                    if(t.IsMSHVRootContentContainer)
                        return t;

                    var childRoot = t.Subtopics.MSHVRootContentContainer;

                    if(childRoot != null)
                        return childRoot;
                }

                return null;
            }
        }

        /// <summary>
        /// This can be used to get a topic by its unique ID (case-insensitive)
        /// </summary>
        /// <param name="id">The ID of the item to get</param>
        /// <value>Returns the topic with the specified <see cref="Topic.Id" /> or null if not found</value>
        public Topic this[string id]
        {
            get
            {
                Topic found = null;

                foreach(Topic t in this)
                {
                    if(String.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        found = t;
                        break;
                    }
                    else
                    {
                        if(t.Subtopics.Count != 0)
                        {
                            found = t.Subtopics[id];

                            if(found != null)
                                break;
                        }
                    }
                }

                return found;
            }
        }
        #endregion

        #region Constructor
        //=====================================================================

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="contentLayoutFile">The content layout file associated with the collection</param>
        /// <remarks>Topics are not loaded until the <see cref="Load" /> method is called.  If the <c>file</c>
        /// parameter is null, this is assumed to be a child topic collection.</remarks>
        public TopicCollection(ContentFile contentLayoutFile)
        {
            this.contentLayoutFile = contentLayoutFile;
        }
        #endregion

        #region Sort and find methods
        //=====================================================================

        /// <summary>
        /// This is used to sort the collection
        /// </summary>
        /// <remarks>Values are sorted by display title.  Comparisons are case-sensitive.</remarks>
        public void Sort()
        {
            ((List<Topic>)this.Items).Sort((x, y) =>
            {
                return String.Compare(x.DisplayTitle, y.DisplayTitle, StringComparison.Ordinal);
            });

            this.OnListChanged(new ListChangedEventArgs(ListChangedType.Reset, -1));
        }

        /// <summary>
        /// This is used to enumerate all topics recursively
        /// </summary>
        /// <returns>An enumerable list of all topics and sub-topics</returns>
        public IEnumerable<Topic> All()
        {
            foreach(var t in this)
            {
                yield return t;

                if(t.Subtopics.Count != 0)
                    foreach(var st in t.Subtopics.All())
                        yield return st;
            }
        }

        /// <summary>
        /// This is used to find all topics and sub-topics that match the specified predicate recursively
        /// </summary>
        /// <param name="match">The match predicate</param>
        /// <param name="expandParentIfFound">True to expand the parent if a child node matches or false to leave
        /// it as is.  Expanding the node ensures it is visible in the bound tree view.</param>
        /// <returns>An enumerable list of all matches</returns>
        public IEnumerable<Topic> Find(Predicate<Topic> match, bool expandParentIfFound)
        {
            if(match == null)
                throw new ArgumentNullException(nameof(match));

            foreach(var t in this)
            {
                if(match(t))
                    yield return t;

                var matches = t.Subtopics.Find(match, expandParentIfFound);

                if(matches.Any())
                {
                    // If requested, make sure the topic is expanded so that we can move to it in the tree view
                    if(expandParentIfFound)
                        t.IsExpanded = true;

                    foreach(var m in matches)
                        yield return m;
                }
            }
        }
        #endregion

        #region Read/write the content layout file
        //=====================================================================

        /// <summary>
        /// Load the collection from the related file
        /// </summary>
        /// <remarks>This will be done automatically at constructor.  This can be called to reload the collection
        /// if needed.</remarks>
        public void Load()
        {
            string defTopicId, splitTopicId;

            this.Clear();

            using(var xr = XmlReader.Create(contentLayoutFile.FullPath, new XmlReaderSettings { CloseInput = true }))
            {
                xr.MoveToContent();

                // These are old options from versions prior to 1.9.0.0
                defTopicId = xr.GetAttribute("defaultTopic");
                splitTopicId = xr.GetAttribute("splitTOCTopic");

                while(!xr.EOF && xr.NodeType != XmlNodeType.EndElement)
                {
                    if(xr.NodeType == XmlNodeType.Element && xr.Name == "Topic")
                    {
                        Topic t = new();
                        
                        t.ReadXml(xr);
                        
                        this.Add(t);
                    }

                    xr.Read();
                }
            }

            // Set the default topic if defined from a prior version
            if(!String.IsNullOrEmpty(defTopicId))
                this[defTopicId]?.IsDefaultTopic = true;

            // Set the "split TOC" option if defined from a prior version
            if(!String.IsNullOrEmpty(splitTopicId))
                this[splitTopicId]?.ApiParentMode = ApiParentMode.InsertAfter;

            this.MatchProjectFilesToTopics();
        }

        /// <summary>
        /// This gets all possible content files from the project and attempts to match them to the topics in the
        /// collection by ID.
        /// </summary>
        public void MatchProjectFilesToTopics()
        {
            TopicFile topicFile;
            string ext;

            foreach(var contentFile in contentLayoutFile.ContentFileProvider.ContentFiles(BuildAction.None))
            {
                ext = Path.GetExtension(contentFile.Filename).ToLowerInvariant();

                if(ext == ".aml")
                {
                    topicFile = new TopicFile(contentFile);

                    if(topicFile.Id != null)
                        this.SetTopic(topicFile);
                }
            }
        }

        /// <summary>
        /// Save the topic collection to the related content layout file
        /// </summary>
        public void Save()
        {
            using var writer = XmlWriter.Create(contentLayoutFile.FullPath, new XmlWriterSettings
            {
                Indent = true,
                CloseOutput = true
            });
            
            writer.WriteStartDocument();
            writer.WriteStartElement("Topics");

            foreach(Topic t in this)
                t.WriteXml(writer);

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }
        #endregion

        #region Private helper methods
        //=====================================================================

        /// <summary>
        /// This is used by contained items to notify the parent that a child list changed and thus the
        /// collection should be marked as dirty.
        /// </summary>
        /// <param name="t">The topic that changed</param>
        /// <param name="e">The list change event arguments from the child collection</param>
        internal void ChildListChanged(Topic t, ListChangedEventArgs e)
        {
            int idx = this.IndexOf(t);

            if(idx != -1)
                this.OnListChanged(new ListChangedEventArgs(ListChangedType.ItemChanged, idx, e.PropertyDescriptor));
        }

        /// <summary>
        /// Set the topic file in any entry that has a matching ID
        /// </summary>
        /// <param name="topicFile">The topic file</param>
        /// <remarks>The IDs should be unique across all entries but, if a duplicate exists, this will help find
        /// it as we'll get a more descriptive error later in the build.</remarks>
        private void SetTopic(TopicFile topicFile)
        {
            foreach(Topic t in this)
            {
                if(t.Id == topicFile.Id)
                    t.TopicFile = topicFile;

                if(t.Subtopics.Count != 0)
                    t.Subtopics.SetTopic(topicFile);
            }
        }
        #endregion

        #region Add all topic files from a folder
        //=====================================================================

        /// <summary>
        /// Add all topics from the specified folder recursively to the collection and to the given project file
        /// </summary>
        /// <param name="folder">The folder from which to get the files</param>
        /// <param name="basePath">The base path to remove from files copied from another folder into the project
        /// folder.  On the first call, this should match the <paramref name="folder"/> value.</param>
        /// <param name="project">The project to which the files are added</param>
        /// <remarks>Only actual conceptual content topic files are added.  They must have a ".aml" extension and
        /// must be one of the valid <see cref="DocumentType">document types</see>.  Folders will be added as
        /// sub-topics recursively.  If a file with the same name as the folder exists, it will be associated
        /// with the container node.  If no such file exists, an empty container node is created.</remarks>
        public void AddTopicsFromFolder(string folder, string basePath, ISandcastleProject project)
        {
            if(basePath == null)
                throw new ArgumentNullException(nameof(basePath));

            if(project == null)
                throw new ArgumentNullException(nameof(project));

            IFileItem newItem;
            Topic topic, removeTopic;
            string name, newPath, projectPath = Path.GetDirectoryName(project.Filename);

            if(basePath.Length !=0 && basePath[basePath.Length - 1] != Path.DirectorySeparatorChar)
                basePath += Path.DirectorySeparatorChar;

            // Add files
            foreach(string file in Directory.EnumerateFiles(folder, "*.aml"))
            {
                try
                {
                    // The file must reside under the project path
                    if(Path.GetDirectoryName(file).StartsWith(projectPath, StringComparison.OrdinalIgnoreCase))
                        newPath = file;
                    else
                        newPath = Path.Combine(projectPath, file.Substring(basePath.Length));

                    // Add the file to the project
                    newItem = project.AddFileToProject(file, newPath);
                    topic = new Topic { TopicFile = new TopicFile(newItem.ToContentFile()) };

                    if(topic.DocumentType > DocumentType.Invalid)
                        this.Add(topic);
                }
                catch
                {
                    // Ignore invalid files
                }
            }

            // Add folders recursively
            foreach(string folderName in Directory.EnumerateDirectories(folder))
            {
                topic = new Topic { Title = name = Path.GetFileName(folderName) };
                topic.Subtopics.AddTopicsFromFolder(folderName, basePath, project);

                // Ignore empty folders
                if(topic.Subtopics.Count == 0)
                    continue;

                this.Add(topic);

                // Look for a file with the same name as the folder
                removeTopic = null;

                foreach(Topic t in topic.Subtopics)
                {
                    if(t.TopicFile != null && Path.GetFileNameWithoutExtension(t.TopicFile.FullPath) == name)
                    {
                        // If found, remove it as it represents the container node
                        topic.Title = null;
                        topic.TopicFile = t.TopicFile;
                        removeTopic = t;
                        break;
                    }
                }

                if(removeTopic != null)
                    topic.Subtopics.Remove(removeTopic);
            }
        }
        #endregion

        #region Overrides
        //=====================================================================

        /// <summary>
        /// This is overridden to set the inserted item's parent to this collection
        /// </summary>
        /// <inheritdoc />
        protected override void InsertItem(int index, Topic item)
        {
            if(item == null)
                throw new ArgumentNullException(nameof(item));

            base.InsertItem(index, item);
            item.Parent = this;
        }

        /// <summary>
        /// This is overridden to set the inserted item's parent to this collection
        /// </summary>
        /// <inheritdoc />
        protected override void SetItem(int index, Topic item)
        {
            if(item == null)
                throw new ArgumentNullException(nameof(item));

            base.SetItem(index, item);
            item.Parent = this;
        }

        /// <summary>
        /// This is overridden to clear the parent on the removed item
        /// </summary>
        /// <param name="index">The index of the item to remove</param>
        protected override void RemoveItem(int index)
        {
            this[index].Parent = null;
            base.RemoveItem(index);
        }
        #endregion

        #region ITableOfContents implementation
        //=====================================================================

        /// <inheritdoc />
        public ContentFile ContentLayoutFile => contentLayoutFile;

        /// <inheritdoc />
        public void GenerateTableOfContents(TocEntryCollection toc, bool includeInvisibleItems)
        {
            if(toc == null)
                throw new ArgumentNullException(nameof(toc));

            foreach(Topic t in this)
            {
                if(t.Visible || includeInvisibleItems)
                {
                    TocEntry entry = new(t.TopicFile?.ContentFile.BasePathProvider);

                    if(t.TopicFile != null)
                    {
                        entry.SourceFile = new FilePath(t.TopicFile.FullPath, t.TopicFile.ContentFile.BasePathProvider);
                        entry.DestinationFile = Path.Combine("html", t.Id + ".htm");
                    }

                    entry.Id = t.Id;
                    entry.PreviewerTitle = !String.IsNullOrEmpty(t.Title) ? t.Title :
                        Path.GetFileNameWithoutExtension(t.TopicFilename);
                    entry.LinkText = String.IsNullOrEmpty(t.LinkText) ? t.DisplayTitle : t.LinkText;
                    entry.Title = t.DisplayTitle;
                    entry.IsDefaultTopic = t.IsDefaultTopic;
                    entry.ApiParentMode = t.ApiParentMode;
                    entry.IsExpanded = t.IsExpanded;
                    entry.IsSelected = t.IsSelected;

                    if(t.Subtopics.Count != 0)
                        t.Subtopics.GenerateTableOfContents(entry.Children, includeInvisibleItems);

                    toc.Add(entry);
                }
            }
        }
        #endregion
    }
}
