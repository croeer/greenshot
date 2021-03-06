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
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Dapplo.Config.Ini;
using Greenshot.Addon.Controls;
using Greenshot.Addon.Core;
using Greenshot.Addon.Windows;

namespace Greenshot.Addon.Imgur.Forms
{
	/// <summary>
	/// Description of ImgurHistory.
	/// </summary>
	public partial class ImgurHistory : ImgurForm
	{
		private static readonly Serilog.ILogger Log = Serilog.Log.Logger.ForContext<ImgurHistory>();
		private GreenshotColumnSorter columnSorter;
		private static readonly IImgurConfiguration config = IniConfig.Current.Get<IImgurConfiguration>();

		private static readonly string[] _columns =
		{
			"hash", "title", "deleteHash", "Date"
		};

		private static ImgurHistory instance = new ImgurHistory();

		public static void ShowHistory()
		{
			instance.ShowDialog();
		}

		private ImgurHistory()
		{
			ManualLanguageApply = true;
			//
			// The InitializeComponent() call is required for Windows Forms designer support.
			//
			InitializeComponent();
			AcceptButton = finishedButton;
			CancelButton = finishedButton;
			// Init sorting
			columnSorter = new GreenshotColumnSorter();
			listview_imgur_uploads.ListViewItemSorter = columnSorter;
			columnSorter.SortColumn = 3;
			columnSorter.Order = SortOrder.Descending;
			ApplyLanguage();
			ClearWindow();
			Shown += async (sender, eventArgs) =>
			{
				Text = imgurLanguage.History + " Loading...";
				BeginRedrawWindow();
				ClearWindow();
				EndRedrawWindow();
				await LoadHistory();
				await ImgurUtils.RetrieveImgurCredits();
				if (config.Credits > 0)
				{
					Text = imgurLanguage.History + " (" + config.Credits + " credits)";
				}
			};
		}

		/// <summary>
		/// Redraw all
		/// </summary>
		private void Redraw()
		{
			BeginRedrawWindow();
			ClearWindow();
			foreach (var imgurInfo in config.RuntimeImgurHistory.Values)
			{
				AddImgurItem(imgurInfo);
			}
			EndRedrawWindow();
		}

		private void ClearWindow()
		{
			BeginRedrawWindow();
			listview_imgur_uploads.Items.Clear();
			listview_imgur_uploads.Columns.Clear();
			foreach (var column in _columns)
			{
				listview_imgur_uploads.Columns.Add(column);
			}
			EndRedrawWindow();
		}

		private void BeginRedrawWindow()
		{
			// Should fix Bug #3378699 
			pictureBox1.Image = null;
			deleteButton.Enabled = false;
			openButton.Enabled = false;
			clipboardButton.Enabled = false;

			listview_imgur_uploads.BeginUpdate();
		}

		private void EndRedrawWindow()
		{
			listview_imgur_uploads.EndUpdate();
			listview_imgur_uploads.Refresh();
		}

		private void AddImgurItem(ImageInfo imgurInfo)
		{
			listview_imgur_uploads.BeginUpdate();
			var item = new ListViewItem(imgurInfo.Id);
			item.Tag = imgurInfo;
			item.SubItems.Add(imgurInfo.Title);
			item.SubItems.Add(imgurInfo.DeleteHash);
			item.SubItems.Add(imgurInfo.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", DateTimeFormatInfo.InvariantInfo));
			listview_imgur_uploads.Items.Add(item);
			for (int i = 0; i < _columns.Length; i++)
			{
				listview_imgur_uploads.Columns[i].Width = -2;
			}
			listview_imgur_uploads.EndUpdate();
			listview_imgur_uploads.Refresh();
		}

		private void Listview_imgur_uploadsSelectedIndexChanged(object sender, EventArgs e)
		{
			pictureBox1.Image = null;
			if (listview_imgur_uploads.SelectedItems != null && listview_imgur_uploads.SelectedItems.Count > 0)
			{
				deleteButton.Enabled = true;
				openButton.Enabled = true;
				clipboardButton.Enabled = true;
				if (listview_imgur_uploads.SelectedItems.Count == 1)
				{
					var imgurInfo = (ImageInfo) listview_imgur_uploads.SelectedItems[0].Tag;
					pictureBox1.Image = imgurInfo.Image;
				}
			}
			else
			{
				pictureBox1.Image = null;
				deleteButton.Enabled = false;
				openButton.Enabled = false;
				clipboardButton.Enabled = false;
			}
		}

		private async void DeleteButtonClick(object sender, EventArgs e)
		{
			if (listview_imgur_uploads.SelectedItems != null && listview_imgur_uploads.SelectedItems.Count > 0)
			{
				for (int i = 0; i < listview_imgur_uploads.SelectedItems.Count; i++)
				{
					var imgurInfo = (ImageInfo) listview_imgur_uploads.SelectedItems[i].Tag;
					var result = MessageBox.Show(string.Format(imgurLanguage.DeleteQuestion, imgurInfo.Title), string.Format(imgurLanguage.DeleteTitle, imgurInfo.Id), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
					if (result == DialogResult.Yes)
					{
						// Should fix Bug #3378699 
						pictureBox1.Image = null;

						await PleaseWaitWindow.CreateAndShowAsync("Imgur plug-in", imgurLanguage.CommunicationWait, async (progress, pleaseWaitToken) =>
						{
							return await ImgurUtils.DeleteImgurImageAsync(imgurInfo, pleaseWaitToken);
						});

						imgurInfo.Dispose();
					}
				}
			}

			Redraw();
		}

		private void ClipboardButtonClick(object sender, EventArgs e)
		{
			var links = new StringBuilder();
			if (listview_imgur_uploads.SelectedItems != null && listview_imgur_uploads.SelectedItems.Count > 0)
			{
				for (int i = 0; i < listview_imgur_uploads.SelectedItems.Count; i++)
				{
					var imgurInfo = (ImageInfo) listview_imgur_uploads.SelectedItems[i].Tag;
					if (config.UsePageLink)
					{
						links.AppendLine(imgurInfo.Page.AbsoluteUri);
					}
					else
					{
						links.AppendLine(imgurInfo.Original.AbsoluteUri);
					}
				}
			}
			ClipboardHelper.SetClipboardData(links.ToString());
		}

		private void ClearHistoryButtonClick(object sender, EventArgs e)
		{
			var result = MessageBox.Show(imgurLanguage.ClearQuestion, "Imgur", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
			if (result == DialogResult.Yes)
			{
				config.RuntimeImgurHistory.Clear();
				config.ImgurUploadHistory.Clear();
				Redraw();
			}
		}

		private void OpenButtonClick(object sender, EventArgs e)
		{
			if (listview_imgur_uploads.SelectedItems != null && listview_imgur_uploads.SelectedItems.Count > 0)
			{
				for (int i = 0; i < listview_imgur_uploads.SelectedItems.Count; i++)
				{
					var imgurInfo = (ImageInfo) listview_imgur_uploads.SelectedItems[i].Tag;
					Process.Start(imgurInfo.Page.AbsoluteUri);
				}
			}
		}

		private void listview_imgur_uploads_ColumnClick(object sender, ColumnClickEventArgs e)
		{
			// Determine if clicked column is already the column that is being sorted.
			if (e.Column == columnSorter.SortColumn)
			{
				// Reverse the current sort direction for this column.
				if (columnSorter.Order == SortOrder.Ascending)
				{
					columnSorter.Order = SortOrder.Descending;
				}
				else
				{
					columnSorter.Order = SortOrder.Ascending;
				}
			}
			else
			{
				// Set the column number that is to be sorted; default to ascending.
				columnSorter.SortColumn = e.Column;
				columnSorter.Order = SortOrder.Ascending;
			}

			// Perform the sort with these new sort options.
			listview_imgur_uploads.Sort();
		}

		/// <summary>
		/// Load the complete history of the imgur uploads, with the corresponding information
		/// </summary>
		private async Task LoadHistory(CancellationToken token = default(CancellationToken))
		{
			bool saveNeeded = false;

			// Load the ImUr history
			foreach (string hash in config.ImgurUploadHistory.Keys)
			{
				if (config.RuntimeImgurHistory.ContainsKey(hash))
				{
					// Already loaded, only add it to the view
					AddImgurItem(config.RuntimeImgurHistory[hash]);
					continue;
				}
				try
				{
					var imgurInfo = await ImgurUtils.RetrieveImgurInfoAsync(hash, config.ImgurUploadHistory[hash]);
					if (imgurInfo != null)
					{
						await ImgurUtils.RetrieveImgurThumbnailAsync(imgurInfo);
						config.RuntimeImgurHistory.Add(hash, imgurInfo);
						// Already loaded, only add it to the view
						AddImgurItem(imgurInfo);
					}
					else
					{
						Log.Debug("Deleting not found ImgUr {Hash} from config.", hash);
						config.ImgurUploadHistory.Remove(hash);
						saveNeeded = true;
					}
				}
				catch (Exception e)
				{
					Log.Error("Problem loading ImgUr history for hash " + hash, e);
				}
			}
			if (saveNeeded)
			{
				// Save needed changes
				// IniConfig.Save();
			}
		}
	}
}