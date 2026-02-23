using System.IO;
using Newtonsoft.Json;

namespace Qognify.Config
{
    public class QognifySettings
    {
        public bool DebugApp { get; set; }
        public TcpServerSettings TcpServer { get; set; }
        public TcpClientSettings TcpClient { get; set; }
        public FileSettings Files { get; set; }
        public LoggingSettings Logging { get; set; }
    }

    public class TcpServerSettings
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public int ProcessIntervalSeconds { get; set; }
        public int ServerTimeoutSeconds { get; set; }
    }

    public class TcpClientSettings
    {
        public string ServerSendHost { get; set; }
    }

    public class FileSettings
    {
        public string BaseDir { get; set; }
        public string AppLogFile { get; set; }
        public string CsvListKeynameAction { get; set; }
    }

    public class LoggingSettings
    {
        public string ConsoleLevel { get; set; }
        public string FileLevel { get; set; }
        public string LoggerName { get; set; }
    }
    
    public static class AppSettingsLoader
    {
        public static QognifySettings Load(string path)
        {
            var json = File.ReadAllText(path);

            var root = JsonConvert.DeserializeObject<RootConfig>(json);

            if (root == null || root.Qognify == null)
                throw new InvalidDataException("Invalid appsettings.json structure: missing 'Qognify' root node.");

            return root.Qognify;
        }
    }
}
