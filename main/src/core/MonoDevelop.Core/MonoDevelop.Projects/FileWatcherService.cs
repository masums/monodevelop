﻿//
// FileWatcherService.cs
//
// Author:
//       Matt Ward <matt.ward@microsoft.com>
//
// Copyright (c) 2018 Microsoft
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using MonoDevelop.Core;

namespace MonoDevelop.Projects
{
	static class FileWatcherService
	{
		static readonly Dictionary<FilePath, FileWatcherWrapper> watchers = new Dictionary<FilePath, FileWatcherWrapper> ();
		static ImmutableList<WorkspaceItem> workspaceItems = ImmutableList<WorkspaceItem>.Empty;

		public static void Add (WorkspaceItem item)
		{
			lock (watchers) {
				workspaceItems = workspaceItems.Add (item);
				UpdateWatchers ();
			}
		}

		public static void Remove (WorkspaceItem item)
		{
			lock (watchers) {
				workspaceItems = workspaceItems.Remove (item);
				UpdateWatchers ();
			}
		}

		static void UpdateWatchers ()
		{
			HashSet<FilePath> watchedDirectories = GetWatchedDirectories ();
			foreach (FilePath directory in GetRootDirectories ()) {
				watchedDirectories.Remove (directory);
				if (!watchers.TryGetValue (directory, out FileWatcherWrapper existingWatcher)) {
					var watcher = new FileWatcherWrapper (directory);
					watchers.Add (directory, watcher);
					watcher.EnableRaisingEvents = true;
				}
			}

			// Remove file watchers no longer needed.
			foreach (FilePath directory in watchedDirectories) {
				Remove (directory);
			}
		}

		static HashSet<FilePath> GetWatchedDirectories ()
		{
			var directories = new HashSet<FilePath> ();
			foreach (FilePath directory in watchers.Keys) {
				directories.Add (directory);
			}
			return directories;
		}

		static IEnumerable<FilePath> GetRootDirectories ()
		{
			var directories = new HashSet<FilePath> ();

			foreach (WorkspaceItem item in workspaceItems) {
				foreach (FilePath directory in item.GetRootDirectories ()) {
					directories.Add (directory);
				}
			}

			return Normalize (directories);
		}

		static void Remove (FilePath directory)
		{
			if (watchers.TryGetValue (directory, out FileWatcherWrapper watcher)) {
				watcher.EnableRaisingEvents = false;
				watchers.Remove (directory);
			}
		}

		public static IEnumerable<FilePath> Normalize (IEnumerable<FilePath> directories)
		{
			var directorySet = new HashSet<FilePath> (directories);

			return directorySet.Where (d => {
				return directorySet.All (other => !d.IsChildPathOf (other));
			});
		}
	}

	class FileWatcherWrapper
	{
		FSW.FileSystemWatcher watcher;

		public FileWatcherWrapper (FilePath path)
		{
			watcher = new FSW.FileSystemWatcher (path);

			// Need LastWrite otherwise no file change events are generated by the native file watcher.
			watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName;
			watcher.IncludeSubdirectories = true;
			watcher.InternalBufferSize = 32768;

			watcher.Changed += OnFileChanged;
			watcher.Deleted += OnFileDeleted;
			watcher.Renamed += OnFileRenamed;
			watcher.Created += OnFileCreated;
			watcher.Error += OnFileWatcherError;
		}

		public bool EnableRaisingEvents {
			get { return watcher.EnableRaisingEvents; }
			set { watcher.EnableRaisingEvents = value; }
		}

		void OnFileChanged (object sender, FileSystemEventArgs e)
		{
			System.Console.WriteLine ("OnFileChanged: {0}", e.FullPath);
			FileService.NotifyFileChanged (e.FullPath);
		}

		void OnFileDeleted (object sender, FileSystemEventArgs e)
		{
			System.Console.WriteLine ("OnFileDeleted: {0}", e.FullPath);
			FileService.NotifyFileRemoved (e.FullPath);
		}

		void OnFileRenamed (object sender, RenamedEventArgs e)
		{
			System.Console.WriteLine ("OnFileRenamed: {0} -> {1}", e.OldFullPath, e.FullPath);

			if (IsTempFileRename (e)) {
				FileService.NotifyFileChanged (e.FullPath);
			}
		}

		void OnFileCreated (object sender, FileSystemEventArgs e)
		{
			System.Console.WriteLine ("OnFileCreated: {0}", e.FullPath);
		}

		void OnFileWatcherError (object sender, ErrorEventArgs e)
		{
			LoggingService.LogError ("FileService.FileWatcher error", e.GetException ());
		}

		static bool IsTempFileRename (RenamedEventArgs e)
		{
			var path = new FilePath (e.FullPath);
			var backupFileName = path.ParentDirectory.Combine (".#" + path.FileName);
			return backupFileName == e.OldFullPath;
		}
	}
}
