using System;
using System.Collections.Generic;
using System.Configuration;
using BVNetwork.NotFound.Configuration;

namespace BVNetwork.NotFound.Core.Configuration
{
    public enum FileNotFoundMode
    {
        /// <summary>
        ///
        /// </summary>
        On,
        /// <summary>
        ///
        /// </summary>
        Off,
        /// <summary>
        ///
        /// </summary>
        RemoteOnly
    };

    public enum LoggerMode
    {
        On, Off
    };


    /// <summary>
    /// Configuration utility class for the custom 404 handler
    /// </summary>
    public class Configuration
    {
        private const string DefRedirectsXmlFile = "~/CustomRedirects.config";
        private const string DefNotfoundPage = "~/bvn/filenotfound/notfound.aspx";
        private const LoggerMode DefLogging = LoggerMode.On;
        private static LoggerMode _logging = DefLogging;
        private const int DefBufferSize = 30;
        private const int DefThreshhold = 5;
        private const string KeyErrorFallback = "EPfBVN404UseStdErrorHandlerAsFallback";
        private const FileNotFoundMode DefNotfoundMode = FileNotFoundMode.On;
        private static FileNotFoundMode _handlerMode = DefNotfoundMode;
        private static bool _handlerModeIsRead;
        private static bool _fallbackToEPiServerError;
        private static bool _fallbackToEPiServerErrorIsRead;
        private const string DefIgnoredExtensions = "jpg,gif,png,css,js,ico,swf,woff";
        private static List<string> _ignoredResourceExtensions;

        public const int CurrentVersion = 3;


        // Only contains static methods for reading configuration values
        // Should not be instanciable
        private Configuration()
        {
        }


        /// <summary>
        /// Tells the errorhandler to use EPiServer Exception Manager
        /// to render unhandled errors. Defaults to False.
        /// </summary>
        public static bool FallbackToEPiServerErrorExceptionManager
        {
            get
            {
                if (_fallbackToEPiServerErrorIsRead == false)
                {
                    _fallbackToEPiServerErrorIsRead = true;
                    if (ConfigurationManager.AppSettings[KeyErrorFallback] != null)
                        bool.TryParse(ConfigurationManager.AppSettings[KeyErrorFallback], out _fallbackToEPiServerError);
                }
                return _fallbackToEPiServerError;
            }
        }

        /// <summary>
        /// Resource extensions to be ignored.
        /// </summary>
        public static List<string> IgnoredResourceExtensions
        {
            get
            {
                if (_ignoredResourceExtensions == null)
                {
                    var ignoredExtensions =
                        string.IsNullOrEmpty(Bvn404HandlerConfiguration.Instance.IgnoredResourceExtensions)
                            ? DefIgnoredExtensions.Split(',')
                            : Bvn404HandlerConfiguration.Instance.IgnoredResourceExtensions.Split(',');
                    _ignoredResourceExtensions = new List<string>(ignoredExtensions);
                }
                return _ignoredResourceExtensions;
            }
        }

        /// <summary>
        /// The mode to use for the 404 handler
        /// </summary>
        public static FileNotFoundMode FileNotFoundHandlerMode
        {
            get
            {
                if (_handlerModeIsRead == false)
                {
                    var mode = Bvn404HandlerConfiguration.Instance.HandlerMode ?? DefNotfoundMode.ToString();

                    try
                    {
                        _handlerMode = (FileNotFoundMode)Enum.Parse(typeof(FileNotFoundMode), mode, true /* Ignores case */);
                    }
                    catch
                    {
                        _handlerMode = DefNotfoundMode;
                    }
                    _handlerModeIsRead = true;
                }

                return _handlerMode;
            }
        }

        /// <summary>
        /// The mode to use for the 404 handler
        /// </summary>
        public static LoggerMode Logging
        {
            get
            {
                var mode = Bvn404HandlerConfiguration.Instance.Logging ?? DefLogging.ToString();

                try
                {
                    _logging = (LoggerMode)Enum.Parse(typeof(LoggerMode), mode, true /* Ignores case */);
                }
                catch
                {
                    _logging = DefLogging;
                }

                return _logging;
            }
        }


        /// <summary>
        /// The virtual path to the 404 handler aspx file.
        /// </summary>
        public static string FileNotFoundHandlerPage => string.IsNullOrEmpty(Bvn404HandlerConfiguration.Instance.FileNotFoundPage)
                                                            ? DefNotfoundPage
                                                            : Bvn404HandlerConfiguration.Instance.FileNotFoundPage;

        /// <summary>
        /// The relative path to the custom redirects xml file, including the name of the
        /// xml file. The 404 handler will map the result to a server path.
        /// </summary>
        public static string CustomRedirectsXmlFile
        {
            get
            {
                if (Bvn404HandlerConfiguration.Instance != null &&
                    string.IsNullOrEmpty(Bvn404HandlerConfiguration.Instance.RedirectsXmlFile) == false)
                {
                    return Bvn404HandlerConfiguration.Instance.RedirectsXmlFile;
                }

                return DefRedirectsXmlFile;
            }
        }


        /// <summary>
        /// BufferSize for logging of redirects.
        /// </summary>
        public static int BufferSize
        {
            get
            {
                if (Bvn404HandlerConfiguration.Instance != null && Bvn404HandlerConfiguration.Instance.BufferSize != -1)
                {
                    return Bvn404HandlerConfiguration.Instance.BufferSize;
                }

                return DefBufferSize;
            }
        }

        /// <summary>
        /// ThreshHold value for redirect logging.
        /// </summary>
        public static int ThreshHold
        {
            get
            {
                if (Bvn404HandlerConfiguration.Instance != null && Bvn404HandlerConfiguration.Instance.Threshold != -1)
                {
                    return Bvn404HandlerConfiguration.Instance.Threshold;
                }

                return DefThreshhold;
            }
        }

    }
}
