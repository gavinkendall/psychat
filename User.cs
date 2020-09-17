using System;
using System.IO;
using System.Drawing;

namespace psychat
{
    public static class User
    {
        public static string Username { get; set; }

        public static string Extra { get; set; }

        public static string Server { get; set; }

        public static int Port { get; set; }

        public static string Channel { get; set; }

        public static string DefaultFontFamily { get; set; } = "Verdana";

        public static float DefaultFontSize { get; set; } = 9;

        public static FontStyle DefaultFontStyle { get; set; } = FontStyle.Regular;

        public static Color Background { get; set; }

        public static Color Text { get; set; }

        public static Color Yourself { get; set; }

        public static Color Person { get; set; }

        public static Color Action1 { get; set; }

        public static Color Time { get; set; }

        public static Color Notice { get; set; }

        public static Color Tag { get; set; }

        /// <summary>
        /// Writes a message to the log file.
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="message"></param>
        public static void Log(string filename, string message)
        {
            try
            {
                message = message.TrimEnd('\n');

                using (var sw = new StreamWriter("log_" + filename + "_" + DateTime.Now.ToString("yyyy-MM-dd") + ".txt", true))
                {
                    sw.WriteLine(message);
                    sw.Flush();
                    sw.Close();
                }
            }
            catch (Exception ex)
            {
                ErrorLog(ex.ToString());
            }
        }

        /// <summary>
        /// Writes a message to the error log.
        /// </summary>
        /// <param name="message">The message to write to the error log.</param>
        public static void ErrorLog(string message)
        {
            message = message.TrimEnd('\n');

            using (var sw = new StreamWriter("error.txt", true))
            {
                sw.WriteLine(message);
                sw.Flush();
                sw.Close();
            }
        }
    }
}
