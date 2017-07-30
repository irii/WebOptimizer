﻿using System.Threading.Tasks;
using Bundler.Utilities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace Bundler
{
    /// <summary>
    /// Middleware for setting up bundles
    /// </summary>
    internal class AssetMiddleware
    {
        private readonly FileCache _fileCache;

        /// <summary>
        /// Initializes a new instance of the <see cref="AssetMiddleware"/> class.
        /// </summary>
        public AssetMiddleware(IHostingEnvironment env, IMemoryCache cache)
        {
            _fileCache = new FileCache(env.WebRootFileProvider, cache);
        }

        /// <summary>
        /// Invokes the middleware
        /// </summary>
        public async Task InvokeAsync(HttpContext context, IAsset asset)
        {
            string cacheKey = asset.GenerateCacheKey(context);

            if (IsConditionalGet(context, cacheKey))
            {
                context.Response.StatusCode = 304;
                await WriteOutputAsync(context, asset, string.Empty, cacheKey).ConfigureAwait(false);
            }
            else if (AssetManager.Pipeline.EnableCaching && _fileCache.TryGetValue(cacheKey, out string value))
            {
                await WriteOutputAsync(context, asset, value, cacheKey).ConfigureAwait(false);
            }
            else
            {
                string result = await asset.ExecuteAsync(context).ConfigureAwait(false);

                if (string.IsNullOrEmpty(result))
                {
                    // TODO: Do some clever error handling
                    return;
                }

                _fileCache.Add(cacheKey, result, asset.SourceFiles);

                await WriteOutputAsync(context, asset, result, cacheKey).ConfigureAwait(false);
            }
        }

        private bool IsConditionalGet(HttpContext context, string cacheKey)
        {
            if (context.Request.Headers.TryGetValue("If-None-Match", out var inm))
            {
                return cacheKey == inm.ToString().Trim('"');
            }

            return false;
        }

        private async Task WriteOutputAsync(HttpContext context, IAsset asset, string content, string cacheKey)
        {
            context.Response.ContentType = asset.ContentType;

            if (AssetManager.Pipeline.EnableCaching && !string.IsNullOrEmpty(cacheKey))
            {
                context.Response.Headers["Cache-Control"] = $"max-age=31536000"; // 1 year
                context.Response.Headers["ETag"] = $"\"{cacheKey}\"";
            }

            if (!string.IsNullOrEmpty(content))
            {
                await context.Response.WriteAsync(content).ConfigureAwait(false);
            }
        }
    }
}