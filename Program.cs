using System;

namespace psychat
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            try
            {
                var form = new ChatForm();
                System.Windows.Forms.Application.Run(form);
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }
    }
}
