﻿/*
 * Greenshot - a free and open source screenshot tool
 * Copyright (C) 2007-2016 Thomas Braun, Jens Klingen, Robin Krom
 * 
 * For more information see: http://getgreenshot.org/
 * The Greenshot project is hosted on GitHub: https://github.com/greenshot
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
using System.Drawing;
using System.Runtime.InteropServices;
using Dapplo.Config.Ini;
using Microsoft.Office.Core;
using PowerPoint = Microsoft.Office.Interop.PowerPoint;

namespace Greenshot.Addon.Office.OfficeExport
{
	public class PowerpointExporter
	{
		private static readonly Serilog.ILogger Log = Serilog.Log.Logger.ForContext(typeof(PowerpointExporter));
		private static readonly IOfficeConfiguration OfficeConfig = IniConfig.Current.Get<IOfficeConfiguration>();
		private static Version _powerpointVersion;

		private static bool IsAfter2003()
		{
			return _powerpointVersion.Major > (int) OfficeVersion.OFFICE_2003;
		}

		/// <summary>
		/// Get the captions of all the open powerpoint presentations
		/// </summary>
		/// <returns></returns>
		public static IEnumerable<string> GetPowerpointPresentations()
		{
			using (var powerpointApplication = GetPowerPointApplication())
			{
				if (powerpointApplication == null)
				{
					yield break;
				}

				using (var presentations = DisposableCom.Create(powerpointApplication.ComObject.Presentations))
				{
					Log.Debug("Open Presentations: {0}", presentations.ComObject.Count);
					for (int i = 1; i <= presentations.ComObject.Count; i++)
					{
						using (var presentation = DisposableCom.Create(presentations.ComObject[i]))
						{
							if (presentation == null)
							{
								continue;
							}
							if (presentation.ComObject.ReadOnly == MsoTriState.msoTrue)
							{
								continue;
							}
							if (IsAfter2003())
							{
								if (presentation.ComObject.Final)
								{
									continue;
								}
							}
							yield return presentation.ComObject.Name;
						}
					}
				}
			}
		}

		/// <summary>
		/// Export the image from the tmpfile to the presentation with the supplied name
		/// </summary>
		/// <param name="presentationName">Name of the presentation to insert to</param>
		/// <param name="tmpFile">Filename of the image file to insert</param>
		/// <param name="imageSize">Size of the image</param>
		/// <param name="title">A string with the image title</param>
		/// <returns></returns>
		public static bool ExportToPresentation(string presentationName, string tmpFile, Size imageSize, string title)
		{
			using (var powerpointApplication = GetPowerPointApplication())
			{
				if (powerpointApplication == null)
				{
					return false;
				}
				using (var presentations = DisposableCom.Create(powerpointApplication.ComObject.Presentations))
				{
					Log.Debug("Open Presentations: {0}", presentations.ComObject.Count);
					for (int i = 1; i <= presentations.ComObject.Count; i++)
					{
						using (var presentation = DisposableCom.Create(presentations.ComObject[i]))
						{
							if (presentation == null)
							{
								continue;
							}
							if (!presentation.ComObject.Name.StartsWith(presentationName))
							{
								continue;
							}
							try
							{
								AddPictureToPresentation(presentation, tmpFile, imageSize, title);
								return true;
							}
							catch (Exception e)
							{
								Log.Error(e, "Adding picture to powerpoint failed");
							}
						}
					}
				}
			}
			return false;
		}

		/// <summary>
		/// Internal method to add a picture to a presentation
		/// </summary>
		/// <param name="presentation"></param>
		/// <param name="tmpFile"></param>
		/// <param name="imageSize"></param>
		/// <param name="title"></param>
		private static void AddPictureToPresentation(IDisposableCom<PowerPoint.Presentation> presentation, string tmpFile, Size imageSize, string title)
		{
			if (presentation != null)
			{
				//ISlide slide = presentation.Slides.AddSlide( presentation.Slides.Count + 1, PPSlideLayout.ppLayoutPictureWithCaption);
				IDisposableCom<PowerPoint.Slide> slide = null;
				try
				{
					float left, top;
					using (var pageSetup = DisposableCom.Create(presentation.ComObject.PageSetup))
					{
						left = (pageSetup.ComObject.SlideWidth/2) - (imageSize.Width/2f);
						top = (pageSetup.ComObject.SlideHeight/2) - (imageSize.Height/2f);
					}
					float width = imageSize.Width;
					float height = imageSize.Height;
					IDisposableCom<PowerPoint.Shape> shapeForCaption = null;
					bool hasScaledWidth = false;
					bool hasScaledHeight = false;
					try
					{
						using (var slides = DisposableCom.Create(presentation.ComObject.Slides))
						{
							slide = DisposableCom.Create(slides.ComObject.Add(slides.ComObject.Count + 1, OfficeConfig.PowerpointSlideLayout));
						}

						using (var shapes = DisposableCom.Create(slide.ComObject.Shapes))
						{
							using (var shapeForLocation = DisposableCom.Create(shapes.ComObject[2]))
							{
								// Shapes[2] is the image shape on this layout.
								shapeForCaption = DisposableCom.Create(shapes.ComObject[1]);
								if (width > shapeForLocation.ComObject.Width)
								{
									width = shapeForLocation.ComObject.Width;
									left = shapeForLocation.ComObject.Left;
									hasScaledWidth = true;
								}
								else
								{
									shapeForLocation.ComObject.Left = left;
								}
								shapeForLocation.ComObject.Width = imageSize.Width;

								if (height > shapeForLocation.ComObject.Height)
								{
									height = shapeForLocation.ComObject.Height;
									top = shapeForLocation.ComObject.Top;
									hasScaledHeight = true;
								}
								else
								{
									top = (shapeForLocation.ComObject.Top + (shapeForLocation.ComObject.Height/2)) - (imageSize.Height/2f);
								}
								shapeForLocation.ComObject.Height = imageSize.Height;
							}
						}
					}
					catch (Exception e)
					{
						Log.Error(e, "Powerpoint shape creating failed");
						using (var slides = DisposableCom.Create(presentation.ComObject.Slides))
						{
							slide = DisposableCom.Create(slides.ComObject.Add(slides.ComObject.Count + 1, PowerPoint.PpSlideLayout.ppLayoutBlank));
						}
					}
					using (var shapes = DisposableCom.Create(slide.ComObject.Shapes))
					{
						using (var shape = DisposableCom.Create(shapes.ComObject.AddPicture(tmpFile, MsoTriState.msoFalse, MsoTriState.msoTrue, 0, 0, width, height)))
						{
							if (OfficeConfig.PowerpointLockAspectRatio)
							{
								shape.ComObject.LockAspectRatio = MsoTriState.msoTrue;
							}
							else
							{
								shape.ComObject.LockAspectRatio = MsoTriState.msoFalse;
							}
							shape.ComObject.ScaleHeight(1, MsoTriState.msoTrue, MsoScaleFrom.msoScaleFromMiddle);
							shape.ComObject.ScaleWidth(1, MsoTriState.msoTrue, MsoScaleFrom.msoScaleFromMiddle);
							if (hasScaledWidth)
							{
								shape.ComObject.Width = width;
							}
							if (hasScaledHeight)
							{
								shape.ComObject.Height = height;
							}
							shape.ComObject.Left = left;
							shape.ComObject.Top = top;
							shape.ComObject.AlternativeText = title;
						}
					}
					if (shapeForCaption != null)
					{
						try
						{
							using (shapeForCaption)
							{
								// Using try/catch to make sure problems with the text range don't give an exception.
								using (var textFrame = DisposableCom.Create(shapeForCaption.ComObject.TextFrame))
								{
									using (var textRange = DisposableCom.Create(textFrame.ComObject.TextRange))
									{
										textRange.ComObject.Text = title;
									}
								}
							}
						}
						catch (Exception ex)
						{
							Log.Warning("Problem setting the title to a text-range", ex);
						}
					}
					// Activate/Goto the slide
					try
					{
						using (var application = DisposableCom.Create(presentation.ComObject.Application))
						{
							using (var activeWindow = DisposableCom.Create(application.ComObject.ActiveWindow))
							{
								using (var view = DisposableCom.Create(activeWindow.ComObject.View))
								{
									view.ComObject.GotoSlide(slide.ComObject.SlideNumber);
								}
							}
						}
					}
					catch (Exception ex)
					{
						Log.Warning("Problem going to the slide", ex);
					}
				}
				finally
				{
					if (slide != null)
					{
						slide.Dispose();
					}
				}
			}
		}

		/// <summary>
		/// Insert a capture into a new presentation
		/// </summary>
		/// <param name="tmpFile"></param>
		/// <param name="imageSize"></param>
		/// <param name="title"></param>
		/// <returns></returns>
		public static bool InsertIntoNewPresentation(string tmpFile, Size imageSize, string title)
		{
			bool isPictureAdded = false;
			using (var powerpointApplication = GetOrCreatePowerPointApplication())
			{
				if (powerpointApplication != null)
				{
					powerpointApplication.ComObject.Activate();
					powerpointApplication.ComObject.Visible = MsoTriState.msoTrue;
					using (var presentations = DisposableCom.Create(powerpointApplication.ComObject.Presentations))
					{
						using (var presentation = DisposableCom.Create(presentations.ComObject.Add()))
						{
							try
							{
								AddPictureToPresentation(presentation, tmpFile, imageSize, title);
								isPictureAdded = true;
							}
							catch (Exception e)
							{
								Log.Error(e, "Powerpoint add picture to presentation failed");
							}
						}
					}
				}
			}
			return isPictureAdded;
		}

		/// <summary>
		/// Initialize static powerpoint variables like version
		/// </summary>
		/// <param name="powerpointApplication">IPowerpointApplication</param>
		private static void InitializeVariables(IDisposableCom<PowerPoint.Application> powerpointApplication)
		{
			if (powerpointApplication == null || powerpointApplication.ComObject == null || _powerpointVersion != null)
			{
				return;
			}
			if (!Version.TryParse(powerpointApplication.ComObject.Version, out _powerpointVersion))
			{
				Log.Warning("Assuming Powerpoint version 1997.");
				_powerpointVersion = new Version((int) OfficeVersion.OFFICE_97, 0, 0, 0);
			}
		}

		/// <summary>
		/// Call this to get the running PowerPoint application, returns null if there isn't any.
		/// </summary>
		/// <returns>ComDisposable for PowerPoint.Application or null</returns>
		private static IDisposableCom<PowerPoint.Application> GetPowerPointApplication()
		{
			IDisposableCom<PowerPoint.Application> powerPointApplication;
			try
			{
				powerPointApplication = DisposableCom.Create((PowerPoint.Application) Marshal.GetActiveObject("PowerPoint.Application"));
			}
			catch (Exception)
			{
				// Ignore, probably no PowerPoint running
				return null;
			}
			if (powerPointApplication.ComObject != null)
			{
				InitializeVariables(powerPointApplication);
			}
			return powerPointApplication;
		}

		/// <summary>
		/// Call this to get the running PowerPoint application, or create a new instance
		/// </summary>
		/// <returns>ComDisposable for PowerPoint.Application</returns>
		private static IDisposableCom<PowerPoint.Application> GetOrCreatePowerPointApplication()
		{
			IDisposableCom<PowerPoint.Application> powerPointApplication = GetPowerPointApplication();
			if (powerPointApplication == null)
			{
				powerPointApplication = DisposableCom.Create(new PowerPoint.Application());
			}
			InitializeVariables(powerPointApplication);
			return powerPointApplication;
		}
	}
}