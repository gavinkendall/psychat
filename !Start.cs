using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace ChatClient
{
	public class Start
	{
		[STAThread]
		static void Main()
		{
			try
			{
				ChatForm form = new ChatForm();
				System.Windows.Forms.Application.Run(form);
			}
			catch (Exception ex)
			{
				User.ErrorLog(ex.ToString());
			}
		}
	}
}
