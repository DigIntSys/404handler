using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Web;
using EPiServer.Logging;
using System.Web.Mvc;
using BVNetwork.NotFound.Core.Configuration;
using BVNetwork.NotFound.Core.CustomRedirects;
using BVNetwork.NotFound.Core.Data;
using BVNetwork.NotFound.Core.Logging;
using EPiServer.Core;
using EPiServer.Web;
using IPAddress = System.Net.IPAddress;
using log4net;

namespace BVNetwork.NotFound.Core
{
    public class Custom404Handler
    {
        public const string NotFoundParam = "404;notfound";

        private static readonly List<string> _ignoredResourceExtensions = new List<string> { "jpg", "gif", "png", "css", "js", "ico", "swf", "woff" };

        private static readonly ILog Logger = LogManager.GetLogger(typeof(Custom404Handler));

        public static bool HandleRequest(string referer, Uri urlNotFound, out string newUrl)
        {
            // Try to match the requested url my matching it
            // to the static list of custom redirects
            CustomRedirectHandler fnfHandler = CustomRedirectHandler.Current;
            CustomRedirect redirect = fnfHandler.CustomRedirects.Find(urlNotFound);
            string pathAndQuery = urlNotFound.PathAndQuery;
            newUrl = null;
            if (redirect == null)
            {
                redirect = fnfHandler.CustomRedirects.FindInProviders(urlNotFound.AbsoluteUri);
            }

            if (redirect != null)
            {
                if (redirect.State.Equals((int)DataStoreHandler.State.Saved))
                {
                    // Found it, however, we need to make sure we're not running in an
                    // infinite loop. The new url must not be the referrer to this page
                    if (string.Compare(redirect.NewUrl, pathAndQuery, StringComparison.InvariantCultureIgnoreCase) != 0)
                    {
                        newUrl = redirect.NewUrl;
                        return true;
                    }
                }
            }
            else
            {
                // log request to database - if logging is turned on.
                if (Configuration.Configuration.Logging == LoggerMode.On)
                {
                    // Safe logging
                    RequestLogger.Instance.LogRequest(pathAndQuery, referer);
                }
            }
            return false;
        }

        public static void FileNotFoundHandler(object sender, EventArgs evt)
        {
            // Check if this should be enabled
            if (Configuration.Configuration.FileNotFoundHandlerMode == FileNotFoundMode.Off)
                return;

            // If we're only doing this for remote users, we need to test for local host
            if (Configuration.Configuration.FileNotFoundHandlerMode == FileNotFoundMode.RemoteOnly)
            {
                // Determine if we're on localhost
                bool localHost = IsLocalhost();
                if (localHost)
                {
                    Logger.Debug("Determined to be localhost, returning");
                    return;
                }
                Logger.Debug("Not localhost, handling error");
            }

            Logger.Debug("FileNotFoundHandler called");

            HttpContext context = HttpContext.Current;
            if (context == null)
            {
                Logger.Debug("No HTTPContext, returning");
                return;
            }

            Uri notFoundUri = context.Request.Url;

            // Skip resource files
            if (IsResourceFile(notFoundUri))
                return;

            // not a server exception. Handle standard 404 error
            if (context.Response.StatusCode != 404 && !CheckForException(context, notFoundUri))
                return;
               
            string query = context.Request.ServerVariables["QUERY_STRING"];

            // avoid duplicate log entries
            if ((query != null) && query.StartsWith("404;"))
            {
                return;
            }

            // Avoid looping forever
            if (IsInfiniteLoop(context))
                return;

            string newUrl;
            if (HandleRequest(GetReferer(context.Request.UrlReferrer), notFoundUri, out newUrl))
            {
                context.Response.RedirectPermanent(newUrl);
            }
            else
            {
                string url = Get404Url();

                context.Response.Clear();
                context.Response.TrySkipIisCustomErrors = true;
                context.Server.ClearError();

                // do the redirect to the 404 page here
                if (HttpRuntime.UsingIntegratedPipeline)
                {
                    context.Server.TransferRequest(url, true);
                }
                else
                {
                    context.RewritePath(url, false);
                    IHttpHandler httpHandler = new MvcHttpHandler();
                    httpHandler.ProcessRequest(context);
                }
                // return the original status code to the client
                // (this won't work in integrated pipleline mode)
                context.Response.StatusCode = 404;
            }
        }

        private static bool CheckForException(HttpContext context, Uri notFoundUri)
        {
            try
            {
                var exception = context.Server.GetLastError();
                Exception innerEx = exception?.GetBaseException();
                if (innerEx != null)
                {
                    if (innerEx is PageNotFoundException)
                    {
                        // Should be a normal 404 handler
                        Logger.InfoFormat("404 PageNotFoundException - Url: {0}", notFoundUri.ToString());
                        Logger.DebugFormat("404 PageNotFoundException - Exception: {0}", innerEx.ToString());

                        // Redirect to page, handling this as a normal 404 error
                        return true;
                    }

                    // IO File not Found exceptions means the .aspx file cannot
                    // be found. We'll handle this as a standard 404 error
                    if (innerEx is FileNotFoundException)
                    {
                        Logger.InfoFormat("404 FileNotFoundException - Url: {0}", notFoundUri.ToString());
                        Logger.DebugFormat("404 FileNotFoundException - Exception: {0}", innerEx.ToString());
                        // Redirect to page, handling this as a normal 404 error
                        return true;
                    }

                    // Not all exceptions we need to handle are specific exception types.
                    // We need to handle file not founds, for .aspx pages in directories
                    // that does not exists. However, an 404 error will be returned by the
                    // HttpException class.
                    HttpException httpEx = innerEx as HttpException;
                    if (httpEx != null)
                    {
                        if (httpEx.GetHttpCode() == 404)
                        {
                            Logger.InfoFormat("404 HttpException - Url: {0}", notFoundUri.ToString());
                            Logger.DebugFormat("404 HttpException - Exception: {0}", httpEx.ToString());
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Debug("Unable to fetch 404 exception.", ex);
            }
            return false;
        }

        /// <summary>
        /// Determines whether the specified not found URI is a resource file
        /// </summary>
        /// <param name="notFoundUri">The not found URI.</param>
        /// <returns>
        /// 	<c>true</c> if it is a resource file; otherwise, <c>false</c>.
        /// </returns>
        private static bool IsResourceFile(Uri notFoundUri)
        {
            string extension = notFoundUri.AbsolutePath;
            int extPos = extension.LastIndexOf('.');
            if (extPos > 0)
            {
                extension = extension.Substring(extPos + 1);
                if (_ignoredResourceExtensions.Contains(extension))
                {
                    // Ignoring 404 rewrite of known resource extension
                    Logger.DebugFormat("Ignoring rewrite of '{0}'. '{1}' is a known resource extension", notFoundUri.ToString(),extension);
                    return true;
                }
            }
            return false;
        }

        private static bool IsInfiniteLoop(HttpContext ctx)
        {
            string requestUrl = ctx.Request.Url.AbsolutePath;
            string fnfPageUrl = Get404Url();
            if (fnfPageUrl.StartsWith("~"))
                fnfPageUrl = fnfPageUrl.Substring(1);
            int posQuery = fnfPageUrl.IndexOf("?");
            if (posQuery > 0)
                fnfPageUrl = fnfPageUrl.Substring(0, posQuery);

            if (string.Compare(requestUrl, fnfPageUrl, StringComparison.InvariantCultureIgnoreCase) == 0)
            {
                Logger.Info("404 Handler detected an infinite loop to 404 page. Exiting");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Determines whether the current request is on localhost.
        /// </summary>
        /// <returns>
        /// 	<c>true</c> if current request is localhost; otherwise, <c>false</c>.
        /// </returns>
        private static bool IsLocalhost()
        {
            bool localHost = false;
            try
            {
                System.Net.IPAddress address = System.Net.IPAddress.Parse(HttpContext.Current.Request.UserHostAddress);
                Debug.WriteLine("IP Address of user: " + address, "404Handler");

                IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());
                Debug.WriteLine("Host Entry of local computer: " + host.HostName, "404Handler");
                localHost = address.Equals(IPAddress.Loopback) || (Array.IndexOf(host.AddressList, address) >= 0);
            }
            catch
            {
                // localhost is false
            }
            return localHost;
        }

        public static string GetReferer(Uri referer)
        {
            string refererUrl = "";
            if (referer != null)
            {
                refererUrl = referer.AbsolutePath;
                if (!string.IsNullOrEmpty(refererUrl))
                {
                    // Strip away host name in front, if local redirect

                    string hostUrl = SiteDefinition.Current.SiteUrl.ToString();
                    if (refererUrl.StartsWith(hostUrl))
                        refererUrl = refererUrl.Remove(0, hostUrl.Length);
                }
            }
            return refererUrl;
        }

        /// <summary>
        /// Creates a url to the 404 page, containing the aspxerrorpath query
        /// variable with information about the current request url
        /// </summary>
        /// <returns></returns>
        private static string Get404Url()
        {
            string baseUrl = Configuration.Configuration.FileNotFoundHandlerPage;
            string currentUrl = HttpContext.Current.Request.Url.PathAndQuery;
            return String.Format("{0}?{1}={2}", baseUrl, NotFoundParam, HttpContext.Current.Server.UrlEncode(currentUrl));
        }
    }
}