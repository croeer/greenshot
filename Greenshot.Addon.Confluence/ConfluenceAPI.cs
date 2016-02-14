﻿/*
 * Greenshot - a free and open source screenshot tool
 * Copyright (C) 2007-2016 Thomas Braun, Jens Klingen, Robin Krom
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

using Dapplo.Config;
using Dapplo.Config.Ini;
using Dapplo.Config.Support;
using Dapplo.HttpExtensions;
using GreenshotConfluencePlugin.Model;
using GreenshotPlugin.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Dapplo.HttpExtensions.Factory;

namespace GreenshotConfluencePlugin
{
	/// <summary>
	/// Confluence API, using the FlurlClient
	/// </summary>
	public class ConfluenceApi : IDisposable
	{
		private static readonly INetworkConfiguration NetworkConfig = IniConfig.Current.Get<INetworkConfiguration>();
		private const string RestPath = "/rest/api";
		private readonly HttpClient _client;

		public string ConfluenceVersion
		{
			get;
			set;
		}

		public string ServerTitle
		{
			get;
			set;
		}

		public Uri ConfluenceBaseUri
		{
			get;
			set;
		}

		public IConfluenceModel Model
		{
			get;
		}

		/// <summary>
		/// Create the ConfluenceAPI object, here the HttpClient is configured
		/// </summary>
		/// <param name="baseUri">Base URL</param>
		public ConfluenceApi(Uri baseUri)
		{
			ConfluenceBaseUri = baseUri;

			_client = HttpClientFactory.Create();
			_client.AddDefaultRequestHeader("X-Atlassian-Token", "nocheck");
			Model = ProxyBuilder.CreateProxy<IConfluenceModel>().PropertyObject;
			Model.ContentCachedById = new ConcurrentDictionary<long, Content>();
		}

		/// <summary>
		/// Set Basic Authentication for the current client
		/// </summary>
		/// <param name="user">username</param>
		/// <param name="password">password</param>
		public void SetBasicAuthentication(string user, string password)
		{
			_client.SetBasicAuthorization(user, password);
		}

		#region Dispose

		/// <summary>
		/// Dispose
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		/// <summary>
		/// Dispose all managed resources
		/// </summary>
		/// <param name="disposing">when true is passed all managed resources are disposed.</param>
		protected virtual void Dispose(bool disposing)
		{
			if (disposing)
			{
				// free managed resources
				if (_client != null)
				{
					_client.Dispose();
				}
			}
			// free native resources if there are any.
		}

		#endregion

		/// <summary>
		/// Load the spaces into the Model
		/// </summary>
		/// <param name="start">the start of the spaces collection, can be used for paging together with the limit</param>
		/// <param name="limit">the maximum number of spaces to read</param>
		/// <param name="token"></param>
		/// <returns>Spaces</returns>
		public async Task LoadSpacesAsync(int start = 0, int limit = 500, CancellationToken token = default(CancellationToken))
		{
			bool finished;
			// Loop until we have all we need
			int loaded = 0;
			do
			{
				var spacesUri = ConfluenceBaseUri.AppendSegments(RestPath, "space").ExtendQuery(new Dictionary<string, object>
				{
					{
						"start", start
					},
					{
						"limit", limit
					}
				});
				dynamic jsonResponse = await _client.GetAsAsync<dynamic>(spacesUri, token: token).ConfigureAwait(false);
				foreach (var spaceJson in jsonResponse.results)
				{
					if (spaceJson._expandable.ContainsKey("homepage"))
					{
						var space = (Space) Space.CreateFrom(spaceJson);
						Model.Spaces.SafelyAddOrOverwrite(space.SpaceKey, space);
					}
					loaded++;
				}
				int returned = Convert.ToInt32(jsonResponse.size);
				int returnedLimit = Convert.ToInt32(jsonResponse.limit);
				start = start + returned;
				finished = loaded >= limit || returned < returnedLimit;
			}
			while (!finished);
		}

		/// <summary>
		/// Get the content by id
		/// </summary>
		/// <param name="id"></param>
		/// <param name="useCache"></param>
		/// <param name="token"></param>
		/// <returns>Content</returns>
		public async Task<Content> GetContentAsync(long id, bool useCache = true, CancellationToken token = default(CancellationToken))
		{
			Content resultContent;
			if (useCache && Model.ContentCachedById.TryGetValue(id, out resultContent))
			{
				return resultContent;
			}
			using (var responseMessage = await _client.GetAsync(ConfluenceBaseUri.AppendSegments(RestPath, "content", id), token).ConfigureAwait(false))
			{
				// Special case
				if (responseMessage.StatusCode == HttpStatusCode.NotFound)
				{
					return null;
				}
				var jsonResponse = await responseMessage.GetAsAsync<dynamic>(token: token).ConfigureAwait(false);
				resultContent = Content.CreateFromContent(jsonResponse);
			}
			if (useCache)
			{
				Model.ContentCachedById.SafelyAddOrOverwrite(resultContent.Id, resultContent);
			}
			return resultContent;
		}

		/// <summary>
		/// Get the children of the content
		/// </summary>
		/// <param name="contentId"></param>
		/// <param name="useCache"></param>
		/// <param name="token"></param>
		/// <returns>list of content</returns>
		public async Task<IList<Content>> GetChildrenAsync(long contentId, bool useCache = true, CancellationToken token = default(CancellationToken))
		{
			IList<Content> children = new List<Content>();
			Uri childUri = ConfluenceBaseUri.AppendSegments(RestPath, "content", contentId, "child").ExtendQuery("expand", "page");
			var jsonResponse = await _client.GetAsAsync<dynamic>(childUri, null, token).ConfigureAwait(false);
			foreach (var pageContent in jsonResponse.page.results)
			{
				Content child = Content.CreateFromContent(pageContent);
				children.Add(child);
				if (useCache)
				{
					Model.ContentCachedById.SafelyAddOrOverwrite(child.Id, child);
				}
			}
			return children;
		}

		/// <summary>
		/// Search content
		/// </summary>
		/// <param name="cql">Confluence Query Language</param>
		/// <param name="token"></param>
		/// <returns>list of content</returns>
		public async Task<dynamic> SearchAsync(string cql, CancellationToken token = default(CancellationToken))
		{
			Uri searchdUri = ConfluenceBaseUri.AppendSegments(RestPath, "content", "search").ExtendQuery("cql", cql);
			return await _client.GetAsAsync<dynamic>(searchdUri, null, token).ConfigureAwait(false);
		}

		/// <summary>
		/// Search content
		/// </summary>
		/// <param name="spaceKey">Key of the space to search with</param>
		/// <param name="title">Title of the page to search for</param>
		/// <param name="token"></param>
		/// <returns>list of content</returns>
		public async Task<Content> SearchPageAsync(string spaceKey, string title, CancellationToken token = default(CancellationToken))
		{
			Content foundPage;
			if (Model.ContentCachedBySpaceAndTitle.TryGetValue(spaceKey + "." + title, out foundPage))
			{
				return foundPage;
			}
			int loaded = 0;
			int start = 0;
			int limit = 100;
			bool finished;
			do
			{
				Uri searchUri = ConfluenceBaseUri.AppendSegments(RestPath, "content").ExtendQuery(new Dictionary<string, object>
				{
					{
						"start", start
					},
					{
						"limit", limit
					},
					{
						"type", "page"
					},
					{
						"spaceKey", spaceKey
					},
					{
						"title", title
					},
				});
				dynamic jsonResponse = await _client.GetAsAsync<dynamic>(searchUri, null, token).ConfigureAwait(false);
				foreach (var pageContent in jsonResponse.results)
				{
					foundPage = Content.CreateFromContent(pageContent);
					if (foundPage.Title == title)
					{
						Model.ContentCachedBySpaceAndTitle.SafelyAddOrOverwrite(spaceKey + "." + title, foundPage);
						return foundPage;
					}
					loaded++;
				}
				int returned = Convert.ToInt32(jsonResponse.size);
				int returnedLimit = Convert.ToInt32(jsonResponse.limit);
				start = start + returned;
				finished = loaded >= limit || returned < returnedLimit;
			}
			while (!finished);

			return null;
		}

		/// <summary>
		/// Attacht the stream as filename to the confluence content with the supplied id
		/// </summary>
		/// <param name="id">content id</param>
		/// <param name="content">HttpContent like StreamContent or ByteArrayContent</param>
		/// <param name="token"></param>
		/// <returns>attachment id</returns>
		public async Task<string> AttachToContentAsync(long id, HttpContent content, CancellationToken token = default(CancellationToken))
		{
			var attachUri = ConfluenceBaseUri.AppendSegments(RestPath, "content", id, "child", "attachment");

			var jsonResponse = await _client.PostAsync<dynamic, HttpContent>(attachUri, content, null, token).ConfigureAwait(false);
			return jsonResponse.results[0].id;
		}
	}
}