using NLog;

namespace Qognify.Logging
{
    public static class LoggerFactory
    {
        public static Logger GetLogger<T>()
        {
            return LogManager.GetLogger(typeof(T).FullName);
        }
    }
}
