﻿/*
 * Greenshot - a free and open source screenshot tool
 * Copyright (C) 2007-2011  Thomas Braun, Jens Klingen, Robin Krom
 * 
 * For more information see: http://getgreenshot.org/
 * The Greenshot project is hosted on Sourceforge: http://sourceforge.net/projects/greenshot/
 * 
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 1 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */
using System;
using System.Collections.Generic;
using System.IO;

using GreenshotPlugin.Core;
using IniFile;

namespace ExternalCommand {
	/// <summary>
	/// Description of FlickrConfiguration.
	/// </summary>
	[IniSection("ExternalCommand", Description="Greenshot ExternalCommand Plugin configuration")]
	public class ExternalCommandConfiguration : IniSection {
		[IniProperty("Commands", Description="The commands that are available.")]
		public List<string> commands;

		[IniProperty("Commandline", Description="The commandline for the output command.")]
		public Dictionary<string, string> commandlines;

		[IniProperty("Argument", Description="The arguments for the output command.")]
		public Dictionary<string, string> arguments;
		
		private const string MSPAINT = "MS Paint";
		private static string paintPath;
		private static bool hasPaint = false;

		private const string PAINTDOTNET = "Paint.NET";
		private static string paintDotNetPath;
		private static bool hasPaintDotNet = false;
		static ExternalCommandConfiguration() {
			try {
				paintPath = AbstractDestination.GetExePath("pbrush.exe");
				hasPaint = !string.IsNullOrEmpty(paintPath) && File.Exists(paintPath);
				paintDotNetPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Paint.NET\PaintDotNet.exe");
				hasPaintDotNet = !string.IsNullOrEmpty(paintDotNetPath) && File.Exists(paintDotNetPath);
			} catch {
			}
		}

		/// <summary>
		/// Supply values we can't put as defaults
		/// </summary>
		/// <param name="property">The property to return a default for</param>
		/// <returns>object with the default value for the supplied property</returns>
		public override object GetDefault(string property) {
			switch(property) {
				case "Commands":
					List<string> commandDefaults = new List<string>();
					if (hasPaintDotNet) {
						commandDefaults.Add(PAINTDOTNET);
					}
					if (hasPaint) {
						commandDefaults.Add(MSPAINT);
					}
					return commandDefaults; 
				case "Commandline":
					Dictionary<string, string> commandlineDefaults = new Dictionary<string, string>();
					if (hasPaintDotNet) {
						commandlineDefaults.Add(PAINTDOTNET, paintDotNetPath);
					}
					if (hasPaint) {
						commandlineDefaults.Add(MSPAINT, paintPath);
					}
					return commandlineDefaults; 
				case "Argument":
					Dictionary<string, string> argumentDefaults = new Dictionary<string, string>();
					argumentDefaults.Add(PAINTDOTNET, "\"{0}\"");
					if (hasPaint) {
						argumentDefaults.Add(MSPAINT, "\"{0}\"");
					}
					return argumentDefaults; 
			}
			return null;
		}
	}
}