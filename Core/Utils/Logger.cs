using System.Text;
namespace OpenCrossoutProtocol;
public static class Logger
{
    public enum LogType : byte
    {
        Base = 1,
        Warning = 2,
        Error = 3,
        Request = 4,
        Response = 5,
        DebugInfo = 6
    }

    public delegate void LogEventHandler(string message, byte logType);

    public static event LogEventHandler OnLogMessage;

    /// <summary>
    /// Внутривенное подписочное логгирование. Для ядра сервера. CabinServerCore.Logger.OnLogMessage += (message, logType) => log(message, logType); для подписки
    /// </summary>
    public static void logl(string logString, byte logtype = (byte)LogType.Base)
    {
        OnLogMessage?.Invoke(logString + "\n", logtype);
    }

    /// <summary>
    /// Внутривенное подписочное логгирование. Для ядра сервера, лёгкий вывод hex данных. CabinServerCore.Logger.OnLogMessage += (message, logType) => log(message, logType); message для подписки
    /// </summary>
    public static void loghexl(byte[] byteArray, byte logtype = (byte)LogType.Base)
    {
        string output;

        if (byteArray == null || byteArray.Length == 0)
        {
            Console.WriteLine("LOGGER.ERROR: The byte array is empty or null.");
            return;
        }

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < byteArray.Length; i++)
        {
            sb.Append(byteArray[i].ToString("X2"));
            if (i < byteArray.Length - 1)
            {
                sb.Append(" ");
            }
        }

        output = sb.ToString() + "\n";
        OnLogMessage?.Invoke(output, logtype);
    }
}