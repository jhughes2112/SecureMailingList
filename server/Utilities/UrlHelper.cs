using System;
using System.Text;
using System.Security.Cryptography;
using System.Net;
using System.Net.Sockets;

namespace Shared
{
	static public class UrlHelper
	{
		// This lets you decode a URL that is base64 encoded.  Typically this happens in a JWT, but maybe useful elsewhere?
		static public string Base64UrlDecode(string input)
		{
			string output = input.Replace('-', '+').Replace('_', '/');
			switch (output.Length % 4) // Pad with trailing '='s
			{
				case 0: break; // No pad chars in this case
				case 2: output += "=="; break; // Two pad chars
				case 3: output += "="; break; // One pad char
				default: throw new System.Exception("Illegal base64url string!");
			}
			byte[] converted = Convert.FromBase64String(output); // Standard base64 decoder
			return Encoding.UTF8.GetString(converted);
		}

		// Base64url no-padding encodes the given input buffer.
		static public string Base64UrlEncodeNoPadding(byte[] buffer)
		{
			string base64 = Convert.ToBase64String(buffer);

			// Converts base64 to base64url.
			base64 = base64.Replace("+", "-");
			base64 = base64.Replace("/", "_");
			// Strips padding.
			base64 = base64.Replace("=", "");

			return base64;
		}

		// Returns URI-safe data with a given input length.
		static public string GenerateRandomDataBase64url(uint length)
		{
			byte[] bytes = new byte[length];
			RandomNumberGenerator.Fill(new Span<byte>(bytes));
			return Base64UrlEncodeNoPadding(bytes);
		}

		static public byte[] Base64UrlDecodeBytes(string input)
		{
			string output = input.Replace('-', '+').Replace('_', '/');
			switch (output.Length % 4)
			{
				case 0: break;
				case 2: output += "=="; break;
				case 3: output += "="; break;
				default: throw new Exception($"Illegal base64url string! {input}");
			}
			return Convert.FromBase64String(output);
		}

		// Finds the desired cookie and returns the value it was set to.
		static public string? ExtractCookie(string? cookieHeader, string cookieName)
		{
			if (!string.IsNullOrWhiteSpace(cookieHeader))
			{
				string search = cookieName + "=";
				string[] parts = cookieHeader.Split(';');
				for (int i = 0; i < parts.Length; i++)
				{
					string p = parts[i].Trim();
					if (p.StartsWith(search, StringComparison.Ordinal))
					{
						return p.Substring(search.Length);
					}
				}
			}
			return null;
		}

		// This reconstructs where the client requested from on their end, from headers and X-Forwarded-* stuff.
		static public Uri GetPublicUrl(HttpListenerRequest req)
		{
			// RFC 7239: Forwarded: by=...,for=...,host=example.com,proto=https
			string? forwarded = req.Headers["Forwarded"];
			string? proto = null;
			string? hostPort = null;

			if (!string.IsNullOrEmpty(forwarded))
			{
				foreach (var part in forwarded.Split(','))
				{
					foreach (var kv in part.Split(';'))
					{
						var eq = kv.IndexOf('=');
						if (eq < 0) continue;
						var k = kv[..eq].Trim().ToLowerInvariant();
						var v = kv[(eq + 1)..].Trim().Trim('"');
						if (k == "proto" && proto is null) proto = v;
						else if (k == "host" && hostPort is null) hostPort = v; // may include port
					}
				}
			}

			// De-facto headers from common proxies/load balancers
			proto ??= req.Headers["X-Forwarded-Proto"];
			if (hostPort is null)
			{
				var xfh = req.Headers["X-Forwarded-Host"];
				if (!string.IsNullOrEmpty(xfh))
					hostPort = xfh.Split(',')[0].Trim(); // first hop
			}
			var xfPort = req.Headers["X-Forwarded-Port"]; // only used if host doesn't have a port
			var xfPrefix = req.Headers["X-Forwarded-Prefix"];
			var xfUri    = req.Headers["X-Forwarded-Uri"];
			var xOrigUrl = req.Headers["X-Original-URL"];

			// HTTP/1.1 Host header from the client (Docker NAT preserves this)
			hostPort ??= req.Headers["Host"];

			// Determine external scheme
			var scheme = proto ?? (req.IsSecureConnection ? "https" : "http");

			// Determine path+query (proxies don't standardize this; prefer explicit headers)
			string pathAndQuery =
				!string.IsNullOrEmpty(xOrigUrl) ? xOrigUrl :
				!string.IsNullOrEmpty(xfUri)    ? xfUri    :
				req.RawUrl ?? "/";

			if (!string.IsNullOrEmpty(xfPrefix))
			{
				// Ensure prefix starts with '/' and avoid double-slash joins
				if (!xfPrefix.StartsWith("/")) xfPrefix = "/" + xfPrefix;
				if (!pathAndQuery.StartsWith(xfPrefix, StringComparison.Ordinal))
					pathAndQuery = xfPrefix.TrimEnd('/') + (pathAndQuery.StartsWith("/") ? "" : "/") + pathAndQuery;
			}
			if (!pathAndQuery.StartsWith("/")) pathAndQuery = "/" + pathAndQuery;

			// If hostPort lacks an explicit port, honor X-Forwarded-Port when it's non-default
			string authority = hostPort ?? "";
			if (!string.IsNullOrEmpty(authority) && authority.IndexOf(':') < 0 && !string.IsNullOrEmpty(xfPort))
			{
				if (int.TryParse(xfPort, out var p) && p > 0)
				{
					bool isDefault = (scheme == "https" && p == 443) || (scheme == "http" && p == 80);
					if (!isDefault) authority = $"{authority}:{p}";
				}
			}

			// Fast path when we have enough to build the public URL
			if (!string.IsNullOrEmpty(authority))
			{
				if (Uri.TryCreate($"{scheme}://{authority}{pathAndQuery}", UriKind.Absolute, out var u))
					return u;
			}

			// Last resort: use internal listener endpoint but keep external scheme and original path
			var local = req.LocalEndPoint;
			var host = local?.AddressFamily == AddressFamily.InterNetworkV6
				? $"[{local!.Address}]"
				: (local?.Address.ToString() ?? "localhost");
			var port = local?.Port ?? (scheme == "https" ? 443 : 80);

			var fallback = new UriBuilder(scheme, host, port);
			// Prefer RawUrl for path+query if available, otherwise req.Url
			if (!string.IsNullOrEmpty(req.RawUrl))
			{
				// RawUrl contains path+query. UriBuilder wants path and query split.
				var qIdx = req.RawUrl.IndexOf('?');
				fallback.Path  = qIdx >= 0 ? req.RawUrl[..qIdx] : req.RawUrl;
				fallback.Query = qIdx >= 0 ? req.RawUrl[(qIdx + 1)..] : "";
			}
			else if (req.Url != null)
			{
				fallback.Path  = req.Url.AbsolutePath;
				fallback.Query = req.Url.Query.TrimStart('?');
			}
			return fallback.Uri;
		}
	}
}
