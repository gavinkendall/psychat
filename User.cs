using System;
using System.IO;
using System.Drawing;
using System.Collections;

using PsyRC;
using PsyRC.Delegates;

namespace ChatClient
{
	public class User
	{
		public static string username;
		public static string password;
		public static string extra;
		public static string server;
		public static string ip;
		public static int port;
		public static string channel;
		public static bool allowInput = false;

		public static string defaultFontFamily = "Verdana";
		public static float defaultFontSize = 9;
		public static FontStyle defaultFontStyle = FontStyle.Regular;

		public static Color background;
		public static Color text;
		public static Color yourself;
		public static Color person;
		public static Color action;
		public static Color time;
		public static Color notice;
		public static Color tag;

        public static void Log(string filename, string text)
        {
            try
            {
                text = text.TrimEnd('\n');

                using (StreamWriter sw = new StreamWriter("log_" + filename + "_" + DateTime.Now.ToShortDateString().ToString().Replace("/", "-") + ".txt", true))
                {
                    sw.WriteLine(text);
                    sw.Flush();
                    sw.Close();
                }
            }
            catch (Exception ex)
            {
                ErrorLog(ex.ToString());
            }
        }

		public static void ErrorLog(string text)
		{
            text = text.TrimEnd('\n');
            using (StreamWriter sw = new StreamWriter("error.txt", true))
            {
                sw.WriteLine(text);
                sw.Flush();
                sw.Close();
            }
		}
	}
}
