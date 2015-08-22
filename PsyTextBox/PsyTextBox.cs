using System;
using System.Windows.Forms;

namespace PsyTextBox
{
	public class PsyTextBox : System.Windows.Forms.TextBox
	{
		protected override void OnKeyPress(KeyPressEventArgs e)
		{
			if (e.KeyChar == (char)13 || e.KeyChar == (char)27)
			{
				e.Handled = true;
			}

			base.OnKeyPress(e);
		}
	}
}
