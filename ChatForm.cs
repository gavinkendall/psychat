using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Threading;
using System.Collections;
using System.Windows.Forms;
using System.ComponentModel;
using System.Xml.Serialization;
using System.Runtime.Serialization;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Formatters.Soap;
using System.Runtime.Serialization.Formatters.Binary;

using PsyRC;
using PsyRC.Delegates;

namespace ChatClient
{
    public class ChatForm : System.Windows.Forms.Form
    {
        private const int CINT_DEFAULT_IRC_PORT = 6667;
        private const string CSTR_DEFAULT_IRC_CHANNEL = "##csharp";
        private const string CSTR_DEFAULT_IRC_SERVER = "irc.freenode.net";

        private IrcClient irc;
        private string mstrUrl;
        private string mstrQuitMessage;
        private bool mblnShowWelcomeMessage = true;

        private delegate void RichTextBoxUpdate_Delegate(string target, string type, string text);
        private delegate void UpdateUserList_Delegate(bool clear);
        private delegate void ScrollChatWindow_Delegate();
        private delegate void OnTopic_Delegate(string str1, string str2, Data ircdata);
        private delegate void Exit_Delegate();
        private delegate void OnChannelMessage_Delegate(Data ircdata);

        private delegate void AddTabPage_Delegate(TabPage tab);
        private AddTabPage_Delegate addTabPageDelegate;

        private delegate void RemoveTabPage_Delegate(TabPage tab);
        private RemoveTabPage_Delegate removeTabPageDelegate;

        private delegate void OnQueryAction_Delegate(string str1, Data ircdata);
        private OnQueryAction_Delegate onQueryActionDelegate;

        private delegate void OnQueryMessage_Delegate(Data ircdata);
        private OnQueryMessage_Delegate onQueryMessageDelegate;

        private bool allowInput = false;
        private System.Windows.Forms.TabControl tabControlChatTabs;
        private System.Windows.Forms.TabPage tabPageChatOutput;
        private System.Windows.Forms.Panel panelUserList;
        private System.Windows.Forms.Panel panelChatInput;
        private System.Windows.Forms.Panel panelChatTabs;
        private System.Windows.Forms.ListBox listBoxUserList;
        private PsyTextBox.PsyTextBox textBoxChatInput;

        private TabPage tab;
        private ArrayList alprivMsgs = new ArrayList();
        private ArrayList alPrivMsgAlert = new ArrayList();
        private ArrayList alIgnoredHosts = new ArrayList();
        private ArrayList alPrivMsgWindows = new ArrayList();
        private ArrayList alPrivMsgWindowList = new ArrayList();

        private Thread threadIrcConnection;
        private Khendys.Controls.ExRichTextBox textBoxChatWindow;

        private System.Timers.Timer timerAutoScroll;
        private Khendys.Controls.ExRichTextBox exRichTextBoxChatOutput;
        private System.ComponentModel.IContainer components;

        private MainMenu mainMenu;
        private MenuItem menuItemFile;
        private MenuItem menuItemConnect;
        private MenuItem menuItemDisconnect;
        private MenuItem menuItemSeperator;
        private MenuItem menuItemExit;
        private MenuItem menuItemOptions;
        private MenuItem menuItemAutoConnect;
        private MenuItem menuItemAutoScroll;

        private const int EM_LINESCROLL = 0x00B6;

        [DllImport("user32.dll")]
        static extern int SetScrollPos(IntPtr hWnd, int nBar, int nPos, bool bRedraw);

        [DllImport("user32.dll")]
        static extern int SendMessage(IntPtr hWnd, int wMsg, int wParam, int lParam);

        public ChatForm()
        {
            try
            {
                Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser;
                key = key.CreateSubKey("SOFTWARE\\" + Application.Name);

                // This works in Windows.
                //User.username = (string)key.GetValue("username", System.Security.Principal.WindowsIdentity.GetCurrent().Name.Split('\\')[1].ToString());

                // This works in Linux.
                User.username = (string)key.GetValue("username", System.Security.Principal.WindowsIdentity.GetCurrent().Name);

                User.defaultFontSize = (int)key.GetValue("fontsize", 9);
                string fontstyle = (string)key.GetValue("fontstyle", "regular");

                if (fontstyle.Equals("bold"))
                {
                    User.defaultFontStyle = FontStyle.Bold;
                }
                if (fontstyle.Equals("regular"))
                {
                    User.defaultFontStyle = FontStyle.Regular;
                }

                InitializeComponent();

                User.defaultFontFamily = (string)key.GetValue("fontfamily", "Verdana");

                User.server = (string)key.GetValue("server", CSTR_DEFAULT_IRC_SERVER);
                User.channel = (string)key.GetValue("channel", CSTR_DEFAULT_IRC_CHANNEL);
                User.port = (int)key.GetValue("port", CINT_DEFAULT_IRC_PORT);

                int autoconnect = (int)key.GetValue("autoconnect", 0);
                int autoscroll = (int)key.GetValue("autoscroll", 1);
                int swearfilter = (int)key.GetValue("swearfilter", 1);
                int securetext = (int)key.GetValue("securetext", 0);

                mstrQuitMessage = (string)key.GetValue("quitmsg", "bye");

                if (autoconnect == 1)
                {
                    menuItemAutoConnect.Checked = true;
                }
                else
                {
                    menuItemAutoConnect.Checked = false;
                }

                if (autoscroll == 1)
                {
                    menuItemAutoScroll.Checked = true;
                }
                else
                {
                    menuItemAutoScroll.Checked = false;
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void ChatForm_Load(object sender, System.EventArgs e)
        {
            try
            {
                User.background = Color.FromArgb(255, 255, 255);
                User.text = Color.FromArgb(0, 0, 0);
                User.yourself = Color.FromArgb(0, 0, 0);
                User.person = Color.FromArgb(85, 85, 85);
                User.action = Color.FromArgb(0, 0, 255);
                User.notice = Color.FromArgb(0, 85, 0);
                User.tag = Color.FromArgb(255, 0, 0);
                User.time = Color.FromArgb(140, 137, 137);

                tabPageChatOutput.Text = User.channel;
                textBoxChatInput.Focus();

                this.Text = Application.Name + " [" + User.server + ":" + User.port.ToString() + ", " + User.username + "]";
                this.panelUserList.BackColor = SystemColors.Control;

                this.exRichTextBoxChatOutput.BackColor = User.background;
                this.exRichTextBoxChatOutput.ForeColor = User.text;

                AppendText(User.channel, "notice", Application.Name + " " + Application.Version + " by " + Application.Author + "\n\n");
                AppendText(User.channel, "notice", "User: " + User.username + "\n");
                AppendText(User.channel, "notice", "Server: " + User.server + "\n");
                AppendText(User.channel, "notice", "Port: " + User.port.ToString() + "\n");
                AppendText(User.channel, "notice", "Channel: " + User.channel + "\n\n");
                AppendText(User.channel, "notice", "Enter /connect to initiate a new connection with the above user, server, port, and channel.\n\n");
                AppendText(User.channel, "notice", "Use /nick to change your username/nickname.\n");
                AppendText(User.channel, "notice", "Use /server to change the server.\n");
                AppendText(User.channel, "notice", "Use /port to change the port number.\n");
                AppendText(User.channel, "notice", "Use /channel to change the channel.\n\n");
                AppendText(User.channel, "notice", "Enter /help for a list of available commands.\n");
                AppendText(User.channel, "notice", "-------------------------------------" + "\n");

                if (menuItemAutoConnect.Checked)
                {
                    menuItemConnect_Click(sender, e);
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    if (components != null)
                    {
                        components.Dispose();
                    }
                }

                base.Dispose(disposing);
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        #region Windows Form Designer generated code
        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ChatForm));
            this.tabControlChatTabs = new System.Windows.Forms.TabControl();
            this.tabPageChatOutput = new System.Windows.Forms.TabPage();
            this.exRichTextBoxChatOutput = new Khendys.Controls.ExRichTextBox();
            this.panelUserList = new System.Windows.Forms.Panel();
            this.listBoxUserList = new System.Windows.Forms.ListBox();
            this.panelChatInput = new System.Windows.Forms.Panel();
            this.textBoxChatInput = new PsyTextBox.PsyTextBox();
            this.panelChatTabs = new System.Windows.Forms.Panel();
            this.timerAutoScroll = new System.Timers.Timer();
            this.menuItemConnect = new System.Windows.Forms.MenuItem();
            this.menuItemDisconnect = new System.Windows.Forms.MenuItem();
            this.menuItemSeperator = new System.Windows.Forms.MenuItem();
            this.menuItemExit = new System.Windows.Forms.MenuItem();
            this.menuItemFile = new System.Windows.Forms.MenuItem();
            this.menuItemAutoConnect = new System.Windows.Forms.MenuItem();
            this.menuItemAutoScroll = new System.Windows.Forms.MenuItem();
            this.menuItemOptions = new System.Windows.Forms.MenuItem();
            this.mainMenu = new System.Windows.Forms.MainMenu(this.components);
            this.tabControlChatTabs.SuspendLayout();
            this.tabPageChatOutput.SuspendLayout();
            this.panelUserList.SuspendLayout();
            this.panelChatInput.SuspendLayout();
            this.panelChatTabs.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.timerAutoScroll)).BeginInit();
            this.SuspendLayout();
            // 
            // tabControlChatTabs
            // 
            this.tabControlChatTabs.Appearance = System.Windows.Forms.TabAppearance.FlatButtons;
            this.tabControlChatTabs.Controls.Add(this.tabPageChatOutput);
            this.tabControlChatTabs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControlChatTabs.DrawMode = System.Windows.Forms.TabDrawMode.OwnerDrawFixed;
            this.tabControlChatTabs.Font = new System.Drawing.Font("Verdana", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.tabControlChatTabs.HotTrack = true;
            this.tabControlChatTabs.Location = new System.Drawing.Point(0, 0);
            this.tabControlChatTabs.Name = "tabControlChatTabs";
            this.tabControlChatTabs.SelectedIndex = 0;
            this.tabControlChatTabs.Size = new System.Drawing.Size(611, 341);
            this.tabControlChatTabs.TabIndex = 4;
            this.tabControlChatTabs.DrawItem += new System.Windows.Forms.DrawItemEventHandler(this.tabControlChatTabs_DrawItem);
            this.tabControlChatTabs.SelectedIndexChanged += new System.EventHandler(this.tabControlChatTabs_SelectedIndexChanged);
            // 
            // tabPageChatOutput
            // 
            this.tabPageChatOutput.BackColor = System.Drawing.SystemColors.Control;
            this.tabPageChatOutput.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.tabPageChatOutput.Controls.Add(this.exRichTextBoxChatOutput);
            this.tabPageChatOutput.Location = new System.Drawing.Point(4, 26);
            this.tabPageChatOutput.Name = "tabPageChatOutput";
            this.tabPageChatOutput.Size = new System.Drawing.Size(603, 311);
            this.tabPageChatOutput.TabIndex = 0;
            this.tabPageChatOutput.Text = "tabPage1";
            // 
            // exRichTextBoxChatOutput
            // 
            this.exRichTextBoxChatOutput.AutoSize = true;
            this.exRichTextBoxChatOutput.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.exRichTextBoxChatOutput.Cursor = System.Windows.Forms.Cursors.IBeam;
            this.exRichTextBoxChatOutput.Dock = System.Windows.Forms.DockStyle.Fill;
            this.exRichTextBoxChatOutput.Font = new System.Drawing.Font("Verdana", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.exRichTextBoxChatOutput.HiglightColor = Khendys.Controls.RtfColor.White;
            this.exRichTextBoxChatOutput.Location = new System.Drawing.Point(0, 0);
            this.exRichTextBoxChatOutput.Name = "exRichTextBoxChatOutput";
            this.exRichTextBoxChatOutput.ReadOnly = true;
            this.exRichTextBoxChatOutput.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical;
            this.exRichTextBoxChatOutput.Size = new System.Drawing.Size(601, 309);
            this.exRichTextBoxChatOutput.TabIndex = 4;
            this.exRichTextBoxChatOutput.Text = "";
            this.exRichTextBoxChatOutput.TextColor = Khendys.Controls.RtfColor.Black;
            this.exRichTextBoxChatOutput.LinkClicked += new LinkClickedEventHandler(this.exRichTextBoxChatOutput_LinkClicked);
            // 
            // panelUserList
            // 
            this.panelUserList.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            this.panelUserList.Controls.Add(this.listBoxUserList);
            this.panelUserList.Dock = System.Windows.Forms.DockStyle.Right;
            this.panelUserList.Location = new System.Drawing.Point(611, 0);
            this.panelUserList.Name = "panelUserList";
            this.panelUserList.Size = new System.Drawing.Size(173, 341);
            this.panelUserList.TabIndex = 3432;
            // 
            // listBoxUserList
            // 
            this.listBoxUserList.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.listBoxUserList.Dock = System.Windows.Forms.DockStyle.Fill;
            this.listBoxUserList.Font = new System.Drawing.Font("Verdana", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.listBoxUserList.ForeColor = System.Drawing.Color.Black;
            this.listBoxUserList.HorizontalScrollbar = true;
            this.listBoxUserList.IntegralHeight = false;
            this.listBoxUserList.ItemHeight = 14;
            this.listBoxUserList.Location = new System.Drawing.Point(0, 0);
            this.listBoxUserList.Name = "listBoxUserList";
            this.listBoxUserList.ScrollAlwaysVisible = true;
            this.listBoxUserList.Size = new System.Drawing.Size(169, 337);
            this.listBoxUserList.Sorted = true;
            this.listBoxUserList.TabIndex = 34;
            this.listBoxUserList.TabStop = false;
            this.listBoxUserList.DoubleClick += new System.EventHandler(this.listBoxUsers_DoubleClick);
            // 
            // panelChatInput
            // 
            this.panelChatInput.Controls.Add(this.textBoxChatInput);
            this.panelChatInput.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelChatInput.Location = new System.Drawing.Point(0, 341);
            this.panelChatInput.Name = "panelChatInput";
            this.panelChatInput.Size = new System.Drawing.Size(784, 24);
            this.panelChatInput.TabIndex = 3555;
            // 
            // textBoxChatInput
            // 
            this.textBoxChatInput.Dock = System.Windows.Forms.DockStyle.Fill;
            this.textBoxChatInput.Font = new System.Drawing.Font("Verdana", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.textBoxChatInput.Location = new System.Drawing.Point(0, 0);
            this.textBoxChatInput.MaxLength = 255;
            this.textBoxChatInput.Name = "textBoxChatInput";
            this.textBoxChatInput.Size = new System.Drawing.Size(784, 22);
            this.textBoxChatInput.TabIndex = 1;
            this.textBoxChatInput.KeyPress += new System.Windows.Forms.KeyPressEventHandler(this.textBoxChatInput_KeyPress);
            // 
            // panelChatTabs
            // 
            this.panelChatTabs.Controls.Add(this.tabControlChatTabs);
            this.panelChatTabs.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelChatTabs.Location = new System.Drawing.Point(0, 0);
            this.panelChatTabs.Name = "panelChatTabs";
            this.panelChatTabs.Size = new System.Drawing.Size(611, 341);
            this.panelChatTabs.TabIndex = 4444;
            // 
            // timerAutoScroll
            // 
            this.timerAutoScroll.Enabled = true;
            this.timerAutoScroll.Interval = 1000D;
            this.timerAutoScroll.SynchronizingObject = this;
            // 
            // menuItemConnect
            // 
            this.menuItemConnect.Index = 0;
            this.menuItemConnect.Text = "Connect";
            this.menuItemConnect.Click += new System.EventHandler(this.menuItemConnect_Click);
            // 
            // menuItemDisconnect
            // 
            this.menuItemDisconnect.Enabled = false;
            this.menuItemDisconnect.Index = 1;
            this.menuItemDisconnect.Text = "Disconnect";
            this.menuItemDisconnect.Click += new System.EventHandler(this.menuItemDisconnect_Click);
            // 
            // menuItemSeperator
            // 
            this.menuItemSeperator.Index = 2;
            this.menuItemSeperator.Text = "-";
            // 
            // menuItemExit
            // 
            this.menuItemExit.Index = 3;
            this.menuItemExit.Text = "Exit";
            this.menuItemExit.Click += new System.EventHandler(this.menuItemExit_Click);
            // 
            // menuItemFile
            // 
            this.menuItemFile.Index = 0;
            this.menuItemFile.MenuItems.AddRange(new System.Windows.Forms.MenuItem[] {
            this.menuItemConnect,
            this.menuItemDisconnect,
            this.menuItemSeperator,
            this.menuItemExit});
            this.menuItemFile.Text = "File";
            // 
            // menuItemAutoConnect
            // 
            this.menuItemAutoConnect.Index = 0;
            this.menuItemAutoConnect.Text = "Auto Connect";
            this.menuItemAutoConnect.Click += new System.EventHandler(this.menuItemAutoConnect_Click);
            // 
            // menuItemAutoScroll
            // 
            this.menuItemAutoScroll.Checked = true;
            this.menuItemAutoScroll.Index = 1;
            this.menuItemAutoScroll.Text = "Auto Scroll";
            this.menuItemAutoScroll.Click += new System.EventHandler(this.menuItemAutoScroll_Click);
            // 
            // menuItemOptions
            // 
            this.menuItemOptions.Index = 1;
            this.menuItemOptions.MenuItems.AddRange(new System.Windows.Forms.MenuItem[] {
            this.menuItemAutoConnect,
            this.menuItemAutoScroll});
            this.menuItemOptions.Text = "Options";
            // 
            // mainMenu
            // 
            this.mainMenu.MenuItems.AddRange(new System.Windows.Forms.MenuItem[] {
            this.menuItemFile,
            this.menuItemOptions});
            // 
            // ChatForm
            // 
            this.AutoScaleBaseSize = new System.Drawing.Size(5, 13);
            this.ClientSize = new System.Drawing.Size(784, 365);
            this.Controls.Add(this.panelChatTabs);
            this.Controls.Add(this.panelUserList);
            this.Controls.Add(this.panelChatInput);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Menu = this.mainMenu;
            this.MinimumSize = new System.Drawing.Size(800, 404);
            this.Name = "ChatForm";
            this.Closing += new System.ComponentModel.CancelEventHandler(this.ChatForm_Closing);
            this.Load += new System.EventHandler(this.ChatForm_Load);
            this.Resize += new System.EventHandler(this.ChatForm_Resize);
            this.tabControlChatTabs.ResumeLayout(false);
            this.tabPageChatOutput.ResumeLayout(false);
            this.tabPageChatOutput.PerformLayout();
            this.panelUserList.ResumeLayout(false);
            this.panelChatInput.ResumeLayout(false);
            this.panelChatInput.PerformLayout();
            this.panelChatTabs.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.timerAutoScroll)).EndInit();
            this.ResumeLayout(false);
        }
        #endregion

        private string Nickname(string nickname)
        {
            try
            {
                if (!string.IsNullOrEmpty(nickname))
                {
                    ChannelUser user = irc.GetChannelUser(User.channel, nickname);

                    if (user.IsOp)
                    {
                        nickname = "@" + nickname;
                    }
                    if (user.IsVoice)
                    {
                        nickname = "+" + nickname;
                    }
                    if (user.IsOwner)
                    {
                        nickname = "~" + nickname;
                    }
                    if (user.IsHalfOp)
                    {
                        nickname = "%" + nickname;
                    }
                    if (user.IsProtected)
                    {
                        nickname = "&" + nickname;
                    }

                    return nickname;
                }
                else
                {
                    return User.username;
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
                return null;
            }
        }

        private void RemoveTabPage_DelegateFunction(TabPage tab)
        {
            try
            {
                alPrivMsgWindowList.RemoveAt(alprivMsgs.IndexOf(tab.Text));
                alprivMsgs.Remove(tab.Text);
                tabControlChatTabs.TabPages.Remove(tab);
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void AddTabPage_DelegateFunction(TabPage tab)
        {
            try
            {
                tabControlChatTabs.TabPages.Add(tab);
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void OnQueryAction_DelegateFunction(string text, Data ircdata)
        {
            try
            {
                if (!alIgnoredHosts.Contains(irc.GetChannelUser(User.channel, ircdata.Nick).Host))
                {
                    if (listBoxUserList.Items.Contains(Nickname(ircdata.Nick.ToString())))
                    {
                        tab = new TabPage(ircdata.Nick);

                        if (!alprivMsgs.Contains(ircdata.Nick.ToString()))
                        {
                            textBoxChatWindow = new Khendys.Controls.ExRichTextBox();

                            textBoxChatWindow.BackColor = User.background;
                            textBoxChatWindow.ForeColor = User.text;
                            textBoxChatWindow.ReadOnly = true;
                            textBoxChatWindow.Font = new Font(User.defaultFontFamily, User.defaultFontSize, User.defaultFontStyle, System.Drawing.GraphicsUnit.Point, ((System.Byte)(0)));

                            textBoxChatWindow.Dock = System.Windows.Forms.DockStyle.Fill;
                            textBoxChatWindow.Visible = true;
                            
                            tab.Controls.Add(textBoxChatWindow);

                            alprivMsgs.Add(ircdata.Nick.ToString());

                            alPrivMsgWindows = new ArrayList();
                            alPrivMsgWindows.Add(textBoxChatWindow);

                            alPrivMsgWindowList.Add(alPrivMsgWindows);

                            string nickname = ircdata.Nick;
                            AppendText(tab.Text, "tag", "[");
                            AppendText(tab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                            AppendText(tab.Text, "tag", "] ");
                            AppendText(tab.Text, "notice", " -!- " + nickname + " is " +
                                irc.GetChannelUser(User.channel, nickname).Ident.ToString() + "@" +
                                irc.GetChannelUser(User.channel, nickname).Host.ToString() + " (" +
                                irc.GetChannelUser(User.channel, nickname).Realname.ToString() + ")\n");

                            addTabPageDelegate = new AddTabPage_Delegate(AddTabPage_DelegateFunction);
                            IAsyncResult r = BeginInvoke(addTabPageDelegate, new object[] { tab });
                            EndInvoke(r);

                            OnQueryMessage_DelegateFunction(ircdata);
                        }
                        else
                        {
                            for (int i = 0; i < tabControlChatTabs.TabPages.Count; i++)
                            {
                                if (tabControlChatTabs.TabPages[i].Text.ToString().Equals(ircdata.Nick.ToString()))
                                {
                                    tab = tabControlChatTabs.TabPages[i];
                                    AppendText(tab.Text, "tag", "[");
                                    AppendText(tab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                                    AppendText(tab.Text, "tag", "] ");
                                    AppendText(tab.Text, "action", "* " + ircdata.Nick.ToString() + " ");
                                    AppendText(tab.Text, "action", text);
                                    AppendText(tab.Text, "text", "\n");
                                    User.Log(tab.Text, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + "* " + ircdata.Nick.ToString() + " \n");

                                    alPrivMsgAlert.Add(i);
                                }
                            }

                            tabControlChatTabs.Refresh();

                            for (int i = 0; i < tab.Controls.Count; i++)
                            {
                                if (tab.Controls[i].GetType().Name == "ExRichTextBox")
                                {
                                    AppendText(tab.Controls[i].Text, "tag", "[");
                                    AppendText(tab.Controls[i].Text, "time", DateTime.Now.ToShortTimeString().ToString());
                                    AppendText(tab.Controls[i].Text, "tag", "] ");
                                    AppendText(tab.Controls[i].Text, "action", "* " + ircdata.Nick.ToString() + " ");
                                    AppendText(tab.Text, "action", text);
                                    AppendText(tab.Controls[i].Text, "text", "\n");
                                    User.Log(tab.Controls[i].Text, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + "* " + ircdata.Nick.ToString() + " \n");
                                }
                            }
                        }

                        ShowChatWindow();
                    }
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void OnQueryMessage_DelegateFunction(Data ircdata)
        {
            try
            {
                if (!alIgnoredHosts.Contains(irc.GetChannelUser(User.channel, ircdata.Nick).Host))
                {
                    if (listBoxUserList.Items.Contains(Nickname(ircdata.Nick.ToString())))
                    {
                        tab = new TabPage(ircdata.Nick);

                        if (!alprivMsgs.Contains(ircdata.Nick.ToString()))
                        {
                            textBoxChatWindow = new Khendys.Controls.ExRichTextBox();
                            textBoxChatWindow.BackColor = User.background;
                            textBoxChatWindow.ForeColor = User.text;
                            textBoxChatWindow.ReadOnly = true;
                            textBoxChatWindow.Font = new Font(User.defaultFontFamily, User.defaultFontSize, User.defaultFontStyle, System.Drawing.GraphicsUnit.Point, ((System.Byte)(0)));
                            textBoxChatWindow.Dock = System.Windows.Forms.DockStyle.Fill;
                            textBoxChatWindow.Visible = true;
                            
                            tab.Controls.Add(textBoxChatWindow);


                            alprivMsgs.Add(ircdata.Nick.ToString());

                            alPrivMsgWindows = new ArrayList();
                            alPrivMsgWindows.Add(textBoxChatWindow);

                            alPrivMsgWindowList.Add(alPrivMsgWindows);

                            string nickname = ircdata.Nick;
                            AppendText(tab.Text, "tag", "[");
                            AppendText(tab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                            AppendText(tab.Text, "tag", "] ");
                            AppendText(tab.Text, "notice", " -!- " + nickname + " is " +
                                irc.GetChannelUser(User.channel, nickname).Ident.ToString() + "@" +
                                irc.GetChannelUser(User.channel, nickname).Host.ToString() + " (" +
                                irc.GetChannelUser(User.channel, nickname).Realname.ToString() + ")\n");

                            addTabPageDelegate = new AddTabPage_Delegate(AddTabPage_DelegateFunction);
                            IAsyncResult r = BeginInvoke(addTabPageDelegate, new object[] { tab });
                            EndInvoke(r);

                            OnQueryMessage_DelegateFunction(ircdata);
                        }
                        else
                        {
                            for (int i = 0; i < tabControlChatTabs.TabPages.Count; i++)
                            {
                                if (tabControlChatTabs.TabPages[i].Text.ToString().Equals(ircdata.Nick.ToString()))
                                {
                                    tab = tabControlChatTabs.TabPages[i];
                                    AppendText(tab.Text, "tag", "[");
                                    AppendText(tab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                                    AppendText(tab.Text, "tag", "] <");
                                    AppendText(tab.Text, "person", Nickname(ircdata.Nick.ToString()));
                                    AppendText(tab.Text, "tag", "> ");
                                    AppendText(tab.Text, "person", ircdata.Message.ToString());
                                    AppendText(tab.Text, "text", "\n");
                                    User.Log(tab.Text, "[" + DateTime.Now.ToShortTimeString().ToString() + "] <" + ircdata.Nick.ToString() + "> " + ircdata.Message.ToString() + "\n");

                                    alPrivMsgAlert.Add(i);
                                }
                            }

                            tabControlChatTabs.Refresh();

                            for (int i = 0; i < tab.Controls.Count; i++)
                            {
                                if (tab.Controls[i].GetType().Name == "ExRichTextBox")
                                {
                                    AppendText(tab.Controls[i].Text, "tag", "[");
                                    AppendText(tab.Controls[i].Text, "time", DateTime.Now.ToShortTimeString().ToString());
                                    AppendText(tab.Controls[i].Text, "tag", "] <");
                                    AppendText(tab.Controls[i].Text, "person", ircdata.Nick.ToString());
                                    AppendText(tab.Controls[i].Text, "tag", "> ");
                                    AppendText(tab.Controls[i].Text, "person", ircdata.Message.ToString());
                                    AppendText(tab.Controls[i].Text, "text", "\n");
                                    User.Log(tab.Controls[i].Text, "[" + DateTime.Now.ToShortTimeString().ToString() + "] <" + ircdata.Nick.ToString() + "> " + ircdata.Message.ToString() + "\n");
                                }
                            }
                        }

                        ShowChatWindow();
                    }
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnPart(string str1, string str2, string str3, Data ircdata)
        {
            try
            {
                if (str3 != null)
                {
                    AppendText(User.channel, "tag", "[");
                    AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                    AppendText(User.channel, "tag", "] ");
                    AppendText(User.channel, "notice", " -!- " + ircdata.Nick.ToString() + " [" + ircdata.Ident + "@" + ircdata.Host + "] has left " + str1.ToString() + " (" + str3.ToString() + ")" + "\n");
                    User.Log(User.channel, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + " -!- " + ircdata.Nick.ToString() + " [" + ircdata.Ident + "@" + ircdata.Host + "] has left " + str1.ToString() + " (" + str3.ToString() + ")" + "\n");
                }
                else
                {
                    AppendText(User.channel, "tag", "[");
                    AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                    AppendText(User.channel, "tag", "] ");
                    AppendText(User.channel, "notice", " -!- " + ircdata.Nick.ToString() + " [" + ircdata.Ident + "@" + ircdata.Host + "] has left " + str1.ToString() + "\n");
                    User.Log(User.channel, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + " -!- " + ircdata.Nick.ToString() + " [" + ircdata.Ident + "@" + ircdata.Host + "] has left " + str1.ToString() + "\n");
                }

                if (alprivMsgs.Contains(ircdata.Nick.ToString()))
                {
                    for (int i = 0; i < tabControlChatTabs.TabPages.Count; i++)
                    {
                        if (tabControlChatTabs.TabPages[i].Text.ToString().Equals(ircdata.Nick.ToString()))
                        {
                            removeTabPageDelegate = new RemoveTabPage_Delegate(RemoveTabPage_DelegateFunction);
                            IAsyncResult r = BeginInvoke(removeTabPageDelegate, new object[] { tabControlChatTabs.TabPages[i] });
                            EndInvoke(r);
                        }
                    }
                }

                RemoveUserFromUserList(ircdata.Nick.ToString());
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnQuit(string str1, string str2, Data ircdata)
        {
            try
            {
                if (str2 != null)
                {
                    AppendText(User.channel, "tag", "[");
                    AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                    AppendText(User.channel, "tag", "] ");
                    AppendText(User.channel, "notice", " -!- " + ircdata.Nick.ToString() + " [" + ircdata.Ident + "@" + ircdata.Host + "] has quit" + " (" + str2.ToString() + ")" + "\n");
                    User.Log(User.channel, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + " -!- " + ircdata.Nick.ToString() + " [" + ircdata.Ident + "@" + ircdata.Host + "] has quit" + " (" + str2.ToString() + ")" + "\n");
                }
                else
                {
                    AppendText(User.channel, "tag", "[");
                    AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                    AppendText(User.channel, "tag", "] ");
                    AppendText(User.channel, "notice", " -!- " + ircdata.Nick.ToString() + " [" + ircdata.Ident + "@" + ircdata.Host + "] has quit\n");
                    User.Log(User.channel, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + " -!- " + ircdata.Nick.ToString() + " [" + ircdata.Ident + "@" + ircdata.Host + "] has quit\n");
                }

                if (alprivMsgs.Contains(ircdata.Nick.ToString()))
                {
                    for (int i = 0; i < tabControlChatTabs.TabPages.Count; i++)
                    {
                        if (tabControlChatTabs.TabPages[i].Text.ToString().Equals(ircdata.Nick.ToString()))
                        {
                            removeTabPageDelegate = new RemoveTabPage_Delegate(RemoveTabPage_DelegateFunction);
                            IAsyncResult r = BeginInvoke(removeTabPageDelegate, new object[] { tabControlChatTabs.TabPages[i] });
                            EndInvoke(r);
                        }
                    }
                }

                RemoveUserFromUserList(ircdata.Nick.ToString());
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnJoin(string str1, string str2, Data ircdata)
        {
            try
            {
                if (!ircdata.Nick.ToString().Equals(irc.Nickname.ToString()))
                {
                    AppendText(User.channel, "tag", "[");
                    AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                    AppendText(User.channel, "tag", "] ");
                    AppendText(User.channel, "notice", " -!- " + ircdata.Nick.ToString() + " [" + ircdata.Ident.ToString() + "@" + ircdata.Host.ToString() + "] has joined " + str1.ToString() + "\n");
                    User.Log(User.channel, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + " -!- " + ircdata.Nick.ToString() + " [" + ircdata.Ident.ToString() + "@" + ircdata.Host.ToString() + "] has joined " + str1.ToString() + "\n");
                }
                else
                {
                    if (mblnShowWelcomeMessage)
                    {
                        AppendText(User.channel, "notice", "-------------------------------------" + "\n");
                        AppendText(User.channel, "notice", " -!- You're in! You can start chatting now. ");
                        AppendText(User.channel, "text", "\n");

                        AppendText(User.channel, "notice", " -!- You may want to change your default nickname.");
                        AppendText(User.channel, "text", "\n");

                        AppendText(User.channel, "notice", " -!- Enter \"/nick random12345\" (without quotes) to change your nickname to random12345.");
                        AppendText(User.channel, "text", "\n");

                        mblnShowWelcomeMessage = false;
                    }

                    AppendText(User.channel, "tag", "[");
                    AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                    AppendText(User.channel, "tag", "] ");
                    AppendText(User.channel, "notice", " -!- " + irc.Nickname.ToString() + " [" + ircdata.Ident.ToString() + "@" + ircdata.Host.ToString() + "] has joined " + str1.ToString() + "\n");

                    User.Log(User.channel, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + " -!- " + irc.Nickname.ToString() + " [" + ircdata.Ident.ToString() + "@" + ircdata.Host.ToString() + "] has joined " + str1.ToString() + "\n");
                    allowInput = true;
                }

                if (!ircdata.Nick.ToString().Equals(irc.Nickname.ToString()))
                {
                    AddUserToUserList(ircdata.Nick.ToString());
                }
                else
                {
                    AddUserToUserList(irc.Nickname.ToString());
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnChannelAction(string text, Data ircdata)
        {
            try
            {
                if (!alIgnoredHosts.Contains(irc.GetChannelUser(User.channel, ircdata.Nick).Host))
                {
                    for (int i = 0; i < tabControlChatTabs.TabPages.Count; i++)
                    {
                        if (tabControlChatTabs.TabPages[i].Text.ToString().Equals(User.channel))
                        {
                            AppendText(User.channel, "tag", "[");
                            AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                            AppendText(User.channel, "tag", "] ");
                            AppendText(User.channel, "action", "* " + ircdata.Nick.ToString() + " ");
                            AppendText(User.channel, "action", text);
                            AppendText(User.channel, "text", "\n");
                            User.Log(User.channel, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + "* " + ircdata.Nick.ToString() + " " + text + "\n");

                            alPrivMsgAlert.Add(i);
                            break;
                        }
                    }

                    tabControlChatTabs.Refresh();
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnNickChange(string str1, string str2, Data ircdata)
        {
            try
            {
                this.Text = Application.Name + " [" + User.server + ":" + User.port.ToString() + ", " + irc.Nickname + "] - " + irc.GetChannel(User.channel).Topic.ToString();

                AppendText(User.channel, "tag", "[");
                AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(User.channel, "tag", "] ");
                AppendText(User.channel, "notice", " -!- " + str1.ToString() + " is now known as " + str2.ToString() + "\n");

                User.Log(User.channel, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + " -!- " + str1.ToString() + " is now known as " + str2.ToString() + "\n");

                for (int i = 0; i < alprivMsgs.Count; i++)
                {
                    if (alprivMsgs[i].ToString().Equals(str1))
                    {
                        alprivMsgs[i] = str2;
                    }
                }

                for (int i = 0; i < tabControlChatTabs.TabPages.Count; i++)
                {
                    if (tabControlChatTabs.TabPages[i].Text.ToString().Equals(str1))
                    {
                        tabControlChatTabs.TabPages[i].Text = str2;
                    }
                }

                UpdateUserList(true);
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }


        private void AddUserToUserList(string nickname)
        {
            try
            {
                Channel chan = irc.GetChannel(User.channel);
                IDictionaryEnumerator it = chan.Users.GetEnumerator();

                while (it.MoveNext())
                {
                    ChannelUser chanUser = (ChannelUser)it.Value;

                    if (chanUser.Nick.Equals(nickname))
                    {
                        if (chanUser.IsOwner)
                        {
                            if (!listBoxUserList.Items.Contains("~" + chanUser.Nick.ToString()))
                            {
                                listBoxUserList.Items.Add("~" + chanUser.Nick.ToString());
                            }
                        }
                        else if (chanUser.IsOp)
                        {
                            if (!listBoxUserList.Items.Contains("@" + chanUser.Nick.ToString()))
                            {
                                listBoxUserList.Items.Add("@" + chanUser.Nick.ToString());
                            }
                        }
                        else if (chanUser.IsHalfOp)
                        {
                            if (!listBoxUserList.Items.Contains("%" + chanUser.Nick.ToString()))
                            {
                                listBoxUserList.Items.Add("%" + chanUser.Nick.ToString());
                            }
                        }
                        else if (chanUser.IsVoice)
                        {
                            if (!listBoxUserList.Items.Contains("+" + chanUser.Nick.ToString()))
                            {
                                listBoxUserList.Items.Add("+" + chanUser.Nick.ToString());
                            }
                        }
                        else if (chanUser.IsProtected)
                        {
                            if (!listBoxUserList.Items.Contains("&" + chanUser.Nick.ToString()))
                            {
                                listBoxUserList.Items.Add("&" + chanUser.Nick.ToString());
                            }
                        }
                        else if (!chanUser.IsOp && !chanUser.IsVoice && !chanUser.IsHalfOp && !chanUser.IsOwner && !chanUser.IsProtected)
                        {
                            if (!listBoxUserList.Items.Contains(chanUser.Nick.ToString()))
                            {
                                listBoxUserList.Items.Add(chanUser.Nick.ToString());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void RemoveUserFromUserList(string nickname)
        {
            try
            {
                if (listBoxUserList.Items.Contains("~" + nickname.ToString()))
                {
                    listBoxUserList.Items.Remove("~" + nickname.ToString());
                }

                if (listBoxUserList.Items.Contains("@" + nickname.ToString()))
                {
                    listBoxUserList.Items.Remove("@" + nickname.ToString());
                }

                if (listBoxUserList.Items.Contains("%" + nickname.ToString()))
                {
                    listBoxUserList.Items.Remove("%" + nickname.ToString());
                }

                if (listBoxUserList.Items.Contains("+" + nickname.ToString()))
                {
                    listBoxUserList.Items.Remove("+" + nickname.ToString());
                }

                if (listBoxUserList.Items.Contains("&" + nickname.ToString()))
                {
                    listBoxUserList.Items.Remove("&" + nickname.ToString());
                }

                if (listBoxUserList.Items.Contains(nickname.ToString()))
                {
                    listBoxUserList.Items.Remove(nickname.ToString());
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void UpdateUserList(bool clear)
        {
            try
            {
                if (listBoxUserList.InvokeRequired)
                {
                    listBoxUserList.Invoke(new UpdateUserList_Delegate(UpdateUserList), new object[] { clear });
                }
                else
                {
                    if (clear)
                    {
                        listBoxUserList.Items.Clear();
                    }

                    Channel chan = irc.GetChannel(User.channel);

                    if (chan != null)
                    {
                        IDictionaryEnumerator it = chan.Users.GetEnumerator();

                        while (it.MoveNext())
                        {
                            ChannelUser chanUser = (ChannelUser)it.Value;

                            if (chanUser.IsOwner)
                            {
                                if (!listBoxUserList.Items.Contains("~" + chanUser.Nick.ToString()))
                                {
                                    listBoxUserList.Items.Add("~" + chanUser.Nick.ToString());
                                }
                            }
                            else if (chanUser.IsOp)
                            {
                                if (!listBoxUserList.Items.Contains("@" + chanUser.Nick.ToString()))
                                {
                                    listBoxUserList.Items.Add("@" + chanUser.Nick.ToString());
                                }
                            }
                            else if (chanUser.IsHalfOp)
                            {
                                if (!listBoxUserList.Items.Contains("%" + chanUser.Nick.ToString()))
                                {
                                    listBoxUserList.Items.Add("%" + chanUser.Nick.ToString());
                                }
                            }
                            else if (chanUser.IsVoice)
                            {
                                if (!listBoxUserList.Items.Contains("+" + chanUser.Nick.ToString()))
                                {
                                    listBoxUserList.Items.Add("+" + chanUser.Nick.ToString());
                                }
                            }
                            else if (chanUser.IsProtected)
                            {
                                if (!listBoxUserList.Items.Contains("&" + chanUser.Nick.ToString()))
                                {
                                    listBoxUserList.Items.Add("&" + chanUser.Nick.ToString());
                                }
                            }
                            else if (!chanUser.IsOp && !chanUser.IsVoice && !chanUser.IsHalfOp && !chanUser.IsOwner && !chanUser.IsProtected)
                            {
                                if (!listBoxUserList.Items.Contains(chanUser.Nick.ToString()))
                                {
                                    listBoxUserList.Items.Add(chanUser.Nick.ToString());
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnReadLine(string data)
        {
            try
            {
                if (!allowInput)
                {
                    AppendText(User.channel, "notice", data.ToString() + "\n");

                    if (data.EndsWith("WARN SOCKET  - connection lost"))
                    {
                        AppendText(User.channel, "notice", " -!- You've lost your connection to the chat server." + "\n");
                    }

                    if (data.EndsWith("Checking ident...") ||
                        data.EndsWith("Checking Ident") ||
                        data.EndsWith("Checking ident"))
                    {
                        AppendText(User.channel, "notice", " -!- You're still connecting. Be patient." + "\n");
                    }
                }

                UpdateUserList(false);
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnChannelMessage(Data ircdata)
        {
            try
            {
                if (tabControlChatTabs.InvokeRequired)
                {
                    tabControlChatTabs.Invoke(new OnChannelMessage_Delegate(irc_OnChannelMessage), new object[] { ircdata });
                }
                else
                {
                    if (!alIgnoredHosts.Contains(irc.GetChannelUser(User.channel, ircdata.Nick).Host))
                    {
                        for (int i = 0; i < tabControlChatTabs.TabPages.Count; i++)
                        {
                            if (tabControlChatTabs.TabPages[i].Text.ToString().Equals(User.channel))
                            {
                                AppendText(User.channel, "tag", "[");
                                AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                                AppendText(User.channel, "tag", "] <");
                                AppendText(User.channel, "person", Nickname(ircdata.Nick.ToString()));
                                AppendText(User.channel, "tag", "> ");
                                AppendText(User.channel, "text", ircdata.Message.ToString());
                                AppendText(User.channel, "text", "\n");

                                User.Log(User.channel, "[" + DateTime.Now.ToShortTimeString().ToString() + "] <" + ircdata.Nick.ToString() + "> " + ircdata.Message.ToString() + "\n");

                                alPrivMsgAlert.Add(i);

                                break;
                            }
                        }

                        tabControlChatTabs.Refresh();
                    }
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnQueryNotice(Data ircdata)
        {
            try
            {
                if (ircdata.Nick != null)
                {
                    AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                    AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                    AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                    AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- " + ircdata.Nick.ToString() + " - " + ircdata.Message.ToString() + "\n");

                    User.Log(tabControlChatTabs.SelectedTab.Text, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + " -!- " + ircdata.Nick.ToString() + " - " + ircdata.Message.ToString() + "\n");
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnTopic(string str1, string str2, Data ircdata)
        {
            try
            {
                if (this.InvokeRequired)
                {
                    this.Invoke(new OnTopic_Delegate(irc_OnTopic), new object[] { str1, str2, ircdata });
                }
                else
                {
                    this.Text = Application.Name + " [" + User.server + ":" + User.port.ToString() + ", " + irc.Nickname + "] - " + irc.GetChannel(User.channel).Topic.ToString();
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnDisconnected()
        {
            try
            {

            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnDeop(string str1, string str2, string str3, Data ircdata)
        {
            try
            {
                AppendText(User.channel, "tag", "[");
                AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(User.channel, "tag", "] ");
                AppendText(User.channel, "notice", " -!- " + ircdata.Nick.ToString() + " has taken operator status from " + str3.ToString() + " on " + str1.ToString() + "\n");

                User.Log(User.channel, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + " -!- " + ircdata.Nick.ToString() + " has taken operator status from " + str3.ToString() + " on " + str1.ToString() + "\n");
                UpdateUserList(true);
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnOwner(string str1, string str2, string str3, Data ircdata)
        {
            try
            {
                AppendText(User.channel, "tag", "[");
                AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(User.channel, "tag", "] ");
                AppendText(User.channel, "notice", " -!- " + ircdata.Nick.ToString() + " has given owner status to " + str3.ToString() + " on " + str1.ToString() + "\n");

                User.Log(User.channel, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + " -!- " + ircdata.Nick.ToString() + " has given owner status to " + str3.ToString() + " on " + str1.ToString() + "\n");
                UpdateUserList(true);
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnHalfOp(string str1, string str2, string str3, Data ircdata)
        {
            try
            {
                AppendText(User.channel, "tag", "[");
                AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(User.channel, "tag", "] ");
                AppendText(User.channel, "notice", " -!- " + ircdata.Nick.ToString() + " has given halfop status to " + str3.ToString() + " on " + str1.ToString() + "\n");

                User.Log(User.channel, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + " -!- " + ircdata.Nick.ToString() + " has given halfop status to " + str3.ToString() + " on " + str1.ToString() + "\n");
                UpdateUserList(true);
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnProtect(string str1, string str2, string str3, Data ircdata)
        {
            try
            {
                AppendText(User.channel, "tag", "[");
                AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(User.channel, "tag", "] ");
                AppendText(User.channel, "notice", " -!- " + ircdata.Nick.ToString() + " has given protect status to " + str3.ToString() + " on " + str1.ToString() + "\n");

                User.Log(User.channel, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + " -!- " + ircdata.Nick.ToString() + " has given protect status to " + str3.ToString() + " on " + str1.ToString() + "\n");
                UpdateUserList(true);
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnDeOwner(string str1, string str2, string str3, Data ircdata)
        {
            try
            {
                AppendText(User.channel, "tag", "[");
                AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(User.channel, "tag", "] ");
                AppendText(User.channel, "notice", " -!- " + ircdata.Nick.ToString() + " has taken owner status from " + str3.ToString() + " on " + str1.ToString() + "\n");

                User.Log(User.channel, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + " -!- " + ircdata.Nick.ToString() + " has taken owner status from " + str3.ToString() + " on " + str1.ToString() + "\n");
                UpdateUserList(true);
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnDeHalfOp(string str1, string str2, string str3, Data ircdata)
        {
            try
            {
                AppendText(User.channel, "tag", "[");
                AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(User.channel, "tag", "] ");
                AppendText(User.channel, "notice", " -!- " + ircdata.Nick.ToString() + " has taken halfop status from " + str3.ToString() + " on " + str1.ToString() + "\n");

                User.Log(User.channel, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + " -!- " + ircdata.Nick.ToString() + " has taken halfop status from " + str3.ToString() + " on " + str1.ToString() + "\n");
                UpdateUserList(true);
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnDeProtect(string str1, string str2, string str3, Data ircdata)
        {
            try
            {
                AppendText(User.channel, "tag", "[");
                AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(User.channel, "tag", "] ");
                AppendText(User.channel, "notice", " -!- " + ircdata.Nick.ToString() + " has taken protect status from " + str3.ToString() + " on " + str1.ToString() + "\n");

                User.Log(User.channel, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + " -!- " + ircdata.Nick.ToString() + " has taken protect status from " + str3.ToString() + " on " + str1.ToString() + "\n");
                UpdateUserList(true);
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnOp(string str1, string str2, string str3, Data ircdata)
        {
            try
            {
                if (irc.GetChannelUser(User.channel, str3).IsVoice)
                {
                    irc.Devoice(User.channel, str3);
                }

                if (irc.GetChannelUser(User.channel, str3).IsOwner)
                {
                    irc.DeOwner(User.channel, str3);
                }

                if (irc.GetChannelUser(User.channel, str3).IsHalfOp)
                {
                    irc.DeHalfOp(User.channel, str3);
                }

                AppendText(User.channel, "tag", "[");
                AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(User.channel, "tag", "] ");
                AppendText(User.channel, "notice", " -!- " + ircdata.Nick.ToString() + " has given operator status to " + str3.ToString() + " on " + str1.ToString() + "\n");

                User.Log(User.channel, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + " -!- " + ircdata.Nick.ToString() + " has given operator status to " + str3.ToString() + " on " + str1.ToString() + "\n");
                UpdateUserList(true);
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnDevoice(string str1, string str2, string str3, Data ircdata)
        {
            try
            {
                AppendText(User.channel, "tag", "[");
                AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(User.channel, "tag", "] ");
                AppendText(User.channel, "notice", " -!- " + ircdata.Nick.ToString() + " has taken voice status from " + str3.ToString() + " on " + str1.ToString() + "\n");

                User.Log(User.channel, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + " -!- " + ircdata.Nick.ToString() + " has taken voice status from " + str3.ToString() + " on " + str1.ToString() + "\n");
                UpdateUserList(true);
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnVoice(string str1, string str2, string str3, Data ircdata)
        {
            try
            {
                if (irc.GetChannelUser(User.channel, str3).IsOp)
                {
                    irc.Deop(User.channel, str3);
                }

                if (irc.GetChannelUser(User.channel, str3).IsOwner)
                {
                    irc.DeOwner(User.channel, str3);
                }

                if (irc.GetChannelUser(User.channel, str3).IsHalfOp)
                {
                    irc.DeHalfOp(User.channel, str3);
                }

                AppendText(User.channel, "tag", "[");
                AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(User.channel, "tag", "] ");
                AppendText(User.channel, "notice", " -!- " + ircdata.Nick.ToString() + " has given voice status to " + str3.ToString() + " on " + str1.ToString() + "\n");

                User.Log(User.channel, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + " -!- " + ircdata.Nick.ToString() + " has given voice status to " + str3.ToString() + " on " + str1.ToString() + "\n");
                UpdateUserList(true);
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }


        private void irc_OnWho(string str1, string str2, string str3, string str4, string str5, bool bl1, bool bl2, bool bl3, bool bl4, bool bl5, bool bl6, bool bl7, string str6, int i, Data ircdata)
        {

        }

        private void irc_OnModeChange(Data ircdata)
        {

        }

        private void irc_OnUserModeChange(Data ircdata)
        {

        }

        private void irc_OnUnban(string str1, string str2, string str3, Data ircdata)
        {
            try
            {
                AppendText(User.channel, "tag", "[");
                AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(User.channel, "tag", "] ");
                AppendText(User.channel, "notice", " -!- " + ircdata.Nick.ToString() + " has unbanned " + str3.ToString() + "\n");

                User.Log(User.channel, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + " -!- " + ircdata.Nick.ToString() + " has unbanned " + str3.ToString() + "\n");
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnBan(string str1, string str2, string str3, Data ircdata)
        {
            try
            {
                AppendText(User.channel, "tag", "[");
                AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(User.channel, "tag", "] ");
                AppendText(User.channel, "notice", " -!- " + ircdata.Nick.ToString() + " has banned " + str3.ToString() + "\n");

                User.Log(User.channel, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + " -!- " + ircdata.Nick.ToString() + " has banned " + str3.ToString() + "\n");
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnKick(string str1, string str2, string str3, string str4, Data ircdata)
        {
            try
            {
                AppendText(User.channel, "tag", "[");
                AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(User.channel, "tag", "] ");
                AppendText(User.channel, "notice", " -!- " + str2.ToString() + " was kicked from " + str1.ToString() + " by " + ircdata.Nick.ToString() + " (" + str4.ToString() + ")" + "\n");

                User.Log(User.channel, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + " -!- " + str2.ToString() + " was kicked from " + str1.ToString() + " by " + ircdata.Nick.ToString() + " (" + str4.ToString() + ")" + "\n");

                RemoveUserFromUserList(str2.ToString());

                if (str2.Equals(User.username))
                {
                    irc.Join(User.channel);
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnQueryAction(string str1, Data ircdata)
        {
            try
            {
                if (ircdata.Nick != null)
                {

                    if (ircdata.Nick != null)
                    {
                        onQueryActionDelegate = new OnQueryAction_Delegate(OnQueryAction_DelegateFunction);
                        IAsyncResult r = BeginInvoke(onQueryActionDelegate, new object[] { str1, ircdata });
                        EndInvoke(r);

                    }
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void irc_OnQueryMessage(Data ircdata)
        {
            try
            {
                if (ircdata.Nick != null)
                {
                    onQueryMessageDelegate = new OnQueryMessage_Delegate(OnQueryMessage_DelegateFunction);
                    IAsyncResult r = BeginInvoke(onQueryMessageDelegate, new object[] { ircdata });
                    EndInvoke(r);
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void textBoxChatInput_KeyPress(object sender, KeyPressEventArgs e)
        {
            try
            {
                if (allowInput)
                {
                    if (e.KeyChar == (char)27)
                    {
                        if (alprivMsgs.Contains(tabControlChatTabs.SelectedTab.Text))
                        {
                            for (int i = 0; i < tabControlChatTabs.TabPages.Count; i++)
                            {
                                if (tabControlChatTabs.TabPages[i].Text.ToString().Equals(tabControlChatTabs.SelectedTab.Text))
                                {
                                    removeTabPageDelegate = new RemoveTabPage_Delegate(RemoveTabPage_DelegateFunction);
                                    IAsyncResult r = BeginInvoke(removeTabPageDelegate, new object[] { tabControlChatTabs.TabPages[i] });
                                    EndInvoke(r);
                                }
                            }
                        }
                    }

                    if (e.KeyChar == (char)13)
                    {
                        if (User.extra != null)
                        {
                            this.Text = Application.Name + " [" + User.server + ":" + User.port.ToString() + ", " + irc.Nickname + "] - " + User.extra.ToString();
                        }

                        if (textBoxChatInput.Text.Length > 0)
                        {
                            if (textBoxChatInput.Text.StartsWith("/"))
                            {
                                if (textBoxChatInput.Text.StartsWith("/nick ") ||
                                    textBoxChatInput.Text.StartsWith("/me ") ||
                                    textBoxChatInput.Text.StartsWith("/msg ") ||
                                    textBoxChatInput.Text.StartsWith("/query ") ||
                                    textBoxChatInput.Text.StartsWith("/op ") ||
                                    textBoxChatInput.Text.Equals("/basic") ||
                                    textBoxChatInput.Text.Equals("/help") ||
                                    textBoxChatInput.Text.Equals("/admin") ||
                                    textBoxChatInput.Text.Equals("/config") ||
                                    textBoxChatInput.Text.Equals("/close") ||
                                    textBoxChatInput.Text.Equals("/disconnect") ||
                                    textBoxChatInput.Text.StartsWith("/own ") ||
                                    textBoxChatInput.Text.StartsWith("/halfop ") ||
                                    textBoxChatInput.Text.StartsWith("/protect ") ||
                                    textBoxChatInput.Text.StartsWith("/voice ") ||
                                    textBoxChatInput.Text.StartsWith("/ignore ") ||
                                    textBoxChatInput.Text.Equals("/clear") ||
                                    textBoxChatInput.Text.StartsWith("/deop ") ||
                                    textBoxChatInput.Text.StartsWith("/devoice ") ||
                                    textBoxChatInput.Text.StartsWith("/deown ") ||
                                    textBoxChatInput.Text.StartsWith("/dehalfop ") ||
                                    textBoxChatInput.Text.StartsWith("/unprotect ") ||
                                    textBoxChatInput.Text.StartsWith("/ban ") ||
                                    textBoxChatInput.Text.StartsWith("/unban ") ||
                                    textBoxChatInput.Text.StartsWith("/kick ") ||
                                    textBoxChatInput.Text.StartsWith("/topic ") ||
                                    textBoxChatInput.Text.Equals("/topic") ||
                                    textBoxChatInput.Text.StartsWith("/whois ") ||
                                    textBoxChatInput.Text.Equals("/quit") ||
                                    textBoxChatInput.Text.Equals("/exit") ||
                                    textBoxChatInput.Text.StartsWith("/quit ") ||
                                    textBoxChatInput.Text.StartsWith("/autoscroll ") ||
                                    textBoxChatInput.Text.StartsWith("/autoconnect ") ||
                                    textBoxChatInput.Text.StartsWith("/exit "))
                                {
                                    if (textBoxChatInput.Text.Equals("/config"))
                                    {
                                        ShowConfigHelp();
                                    }

                                    if (textBoxChatInput.Text.Equals("/admin"))
                                    {
                                        ShowAdvancedCommandHelp();
                                    }

                                    if (textBoxChatInput.Text.Equals("/help"))
                                    {
                                        ShowMainHelp();
                                    }

                                    if (textBoxChatInput.Text.Equals("/basic"))
                                    {
                                        ShowBasicCommandHelp();
                                    }

                                    if (textBoxChatInput.Text.StartsWith("/autoconnect "))
                                    {
                                        string message = textBoxChatInput.Text.Substring(13);

                                        if (message.Equals("on"))
                                        {
                                            menuItemAutoConnect.Checked = true;
                                            AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                                            AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                                            AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                                            AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Auto Connect has been turned on\n");
                                        }

                                        if (message.Equals("off"))
                                        {
                                            menuItemAutoConnect.Checked = false;
                                            AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                                            AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                                            AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                                            AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Auto Connect has been turned off\n");
                                        }
                                    }

                                    if (textBoxChatInput.Text.StartsWith("/autoscroll "))
                                    {
                                        string message = textBoxChatInput.Text.Substring(12);

                                        if (message.Equals("on"))
                                        {
                                            menuItemAutoScroll.Checked = true;
                                            timerAutoScroll.Enabled = true;

                                            ScrollChatWindow();

                                            AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                                            AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                                            AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                                            AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Auto Scroll has been turned on\n");
                                        }

                                        if (message.Equals("off"))
                                        {
                                            menuItemAutoScroll.Checked = false;
                                            timerAutoScroll.Enabled = false;

                                            AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                                            AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                                            AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                                            AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Auto Scroll has been turned off\n");
                                        }
                                    }

                                    if (textBoxChatInput.Text.Equals("/disconnect"))
                                    {
                                        if (menuItemDisconnect.Enabled)
                                        {
                                            menuItemDisconnect_Click(sender, e);
                                        }
                                    }

                                    if (textBoxChatInput.Text.Equals("/close"))
                                    {
                                        if (alprivMsgs.Contains(tabControlChatTabs.SelectedTab.Text))
                                        {
                                            for (int i = 0; i < tabControlChatTabs.TabPages.Count; i++)
                                            {
                                                if (tabControlChatTabs.TabPages[i].Text.ToString().Equals(tabControlChatTabs.SelectedTab.Text))
                                                {
                                                    removeTabPageDelegate = new RemoveTabPage_Delegate(RemoveTabPage_DelegateFunction);
                                                    IAsyncResult r = BeginInvoke(removeTabPageDelegate, new object[] { tabControlChatTabs.TabPages[i] });
                                                    EndInvoke(r);
                                                }
                                            }
                                        }
                                    }

                                    if (textBoxChatInput.Text.StartsWith("/quit ") || textBoxChatInput.Text.StartsWith("/exit "))
                                    {
                                        mstrQuitMessage = textBoxChatInput.Text.Substring(6);

                                        Exit();
                                    }

                                    if (textBoxChatInput.Text.Equals("/quit") || textBoxChatInput.Text.Equals("/exit"))
                                    {
                                        Exit();
                                    }

                                    if (textBoxChatInput.Text.StartsWith("/msg ") || textBoxChatInput.Text.StartsWith("/query "))
                                    {
                                        string comment = null;
                                        string message = null;

                                        if (textBoxChatInput.Text.StartsWith("/msg "))
                                        {
                                            message = textBoxChatInput.Text.Substring(5);
                                        }

                                        if (textBoxChatInput.Text.StartsWith("/query "))
                                        {
                                            message = textBoxChatInput.Text.Substring(7);
                                        }

                                        string[] split = message.Split(' ');

                                        for (int i = 1; i < split.Length; i++)
                                        {
                                            comment += split[i].ToString() + " ";
                                        }

                                        if (comment != null)
                                        {
                                            comment = comment.TrimStart(' ');
                                        }

                                        if (!split[0].ToString().Equals(irc.Nickname.ToString()))
                                        {
                                            if (split[0].ToString().Equals("NickServ"))
                                            {
                                                split[0] = split[0].ToLower();
                                            }

                                            if (listBoxUserList.Items.Contains(split[0].ToString()) || split[0].ToString().Equals("nickserv"))
                                            {
                                                textBoxChatWindow = new Khendys.Controls.ExRichTextBox();

                                                textBoxChatWindow.BackColor = User.background;
                                                textBoxChatWindow.ForeColor = User.text;
                                                textBoxChatWindow.ReadOnly = true;
                                                textBoxChatWindow.Font = new Font(User.defaultFontFamily, User.defaultFontSize, User.defaultFontStyle, System.Drawing.GraphicsUnit.Point, ((System.Byte)(0)));

                                                tab = new TabPage(split[0].ToString());

                                                if (!alprivMsgs.Contains(split[0].ToString()))
                                                {
                                                    textBoxChatWindow.Dock = System.Windows.Forms.DockStyle.Fill;

                                                    textBoxChatWindow.Visible = true;

                                                    tab.Controls.Add(textBoxChatWindow);

                                                    alprivMsgs.Add(split[0].ToString());

                                                    alPrivMsgWindows = new ArrayList();
                                                    alPrivMsgWindows.Add(textBoxChatWindow);

                                                    alPrivMsgWindowList.Add(alPrivMsgWindows);

                                                    addTabPageDelegate = new AddTabPage_Delegate(AddTabPage_DelegateFunction);
                                                    IAsyncResult r = BeginInvoke(addTabPageDelegate, new object[] { tab });
                                                    EndInvoke(r);

                                                    tabControlChatTabs.SelectedTab = tab;

                                                    if (comment != null && comment.Length > 0)
                                                    {
                                                        if (split[0].Equals("nickserv"))
                                                        {
                                                            irc.Message(SendType.Message, split[0].ToString(), comment.ToString());
                                                        }
                                                        else
                                                        {
                                                            irc.Message(SendType.Message, split[0].ToString(), comment.ToString());
                                                        }

                                                        AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", "[");
                                                        AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", DateTime.Now.ToShortTimeString().ToString());
                                                        AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", "] <");
                                                        AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", Nickname(irc.Nickname.ToString()));
                                                        AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", "> ");
                                                        AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", comment.ToString());
                                                        AppendText(tabControlChatTabs.SelectedTab.Text, "text", "\n");

                                                        User.Log(tabControlChatTabs.SelectedTab.Text, "[" + DateTime.Now.ToShortTimeString().ToString() + "] <" + Nickname(irc.Nickname.ToString()) + "> " + comment.ToString() + "\n");
                                                    }
                                                }
                                                else
                                                {
                                                    for (int i = 0; i < tabControlChatTabs.TabPages.Count; i++)
                                                    {
                                                        if (tabControlChatTabs.TabPages[i].Text.ToString().Equals(split[0]))
                                                        {
                                                            tab = tabControlChatTabs.TabPages[i];
                                                        }
                                                    }

                                                    for (int i = 0; i < tab.Controls.Count; i++)
                                                    {
                                                        if (tab.Controls[i].GetType().Name == "ExRichTextBox")
                                                        {
                                                            tabControlChatTabs.SelectedTab = tab;

                                                            if (comment != null && comment.Length > 0)
                                                            {
                                                                if (split[0].Equals("nickserv"))
                                                                {
                                                                    irc.Message(SendType.Message, split[0].ToString(), comment.ToString());
                                                                }
                                                                else
                                                                {
                                                                    irc.Message(SendType.Message, split[0].ToString(), comment.ToString());
                                                                }

                                                                AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", "[");
                                                                AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", DateTime.Now.ToShortTimeString().ToString());
                                                                AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", "] <");
                                                                AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", Nickname(irc.Nickname.ToString()));
                                                                AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", "> ");
                                                                AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", comment.ToString());
                                                                AppendText(tabControlChatTabs.SelectedTab.Text, "text", "\n");

                                                                User.Log(tabControlChatTabs.SelectedTab.Text, "[" + DateTime.Now.ToShortTimeString().ToString() + "] <" + Nickname(irc.Nickname.ToString()) + "> " + comment.ToString() + "\n");
                                                                break;
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }

                                    if (textBoxChatInput.Text.StartsWith("/whois "))
                                    {
                                        string nickname = textBoxChatInput.Text.Substring(7);

                                        if (listBoxUserList.Items.Contains(Nickname(nickname)))
                                        {
                                            AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                                            AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                                            AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                                            AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- " + nickname + " is " +
                                                irc.GetChannelUser(User.channel, nickname).Ident.ToString() + "@" +
                                                irc.GetChannelUser(User.channel, nickname).Host.ToString() + " (" +
                                                irc.GetChannelUser(User.channel, nickname).Realname.ToString() + ")\n");

                                            User.Log(tabControlChatTabs.SelectedTab.Text, "[" + DateTime.Now.ToShortTimeString().ToString() + "] " + " -!- " + nickname + " is " + irc.GetChannelUser(User.channel, nickname).Ident.ToString() + "@" + irc.GetChannelUser(User.channel, nickname).Host.ToString() + " (" + irc.GetChannelUser(User.channel, nickname).Realname.ToString() + ")\n");
                                        }
                                    }

                                    if (textBoxChatInput.Text.StartsWith("/me "))
                                    {
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "action", "* " + irc.Nickname.ToString() + " ");
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "action", textBoxChatInput.Text.Substring(4));
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "action", "\n");

                                        irc.Message(SendType.Action, tabControlChatTabs.SelectedTab.Text, textBoxChatInput.Text.Substring(4));

                                        User.Log(tabControlChatTabs.SelectedTab.Text, "[" + DateTime.Now.ToShortTimeString().ToString() + "] * " + irc.Nickname.ToString() + " " + textBoxChatInput.Text.Substring(4) + "\n");
                                    }

                                    if (textBoxChatInput.Text.Equals("/topic"))
                                    {
                                        AppendText(User.channel, "notice", " -!- Topic: " + irc.GetChannel(User.channel).Topic.ToString() + "\n");
                                        User.Log(User.channel, " -!- Topic: " + irc.GetChannel(User.channel).Topic.ToString() + "\n");
                                    }

                                    if (textBoxChatInput.Text.Equals("/clear"))
                                    {
                                        ClearText();
                                    }

                                    if (textBoxChatInput.Text.StartsWith("/topic "))
                                    {
                                        if (irc.GetChannelUser(User.channel, irc.Nickname).IsOp ||
                                            irc.GetChannelUser(User.channel, irc.Nickname).IsOwner)
                                        {
                                            string message = textBoxChatInput.Text.Substring(7);
                                            string[] split = message.Split(' ');

                                            if (split.Length > 0)
                                            {
                                                message = message.TrimEnd(' ');
                                                AppendText(User.channel, "notice", " -!- " + irc.Nickname.ToString() + " changed the topic to: " + message + "\n");

                                                irc.Topic(User.channel, message);

                                                User.Log(User.channel, " -!- " + irc.Nickname.ToString() + " changed the topic to: " + message + "\n");
                                                this.Text = Application.Name + " [" + User.server + ":" + User.port.ToString() + ", " + irc.Nickname + "] - " + message.ToString();
                                            }
                                        }
                                        else
                                        {
                                            AppendText(User.channel, "notice", " -!- " + "You need to have operator status or higher in order to set the topic. This is a restriction imposed by " + Application.Name + " in order to protect the topic from being set by non-operators.\n");
                                            User.Log(User.channel, " -!- " + "You need to have operator status or higher in order to set the topic. This is a restriction imposed by " + Application.Name + " in order to protect the topic from being set by non-operators.\n");
                                        }
                                    }

                                    if (textBoxChatInput.Text.StartsWith("/protect "))
                                    {
                                        string message = textBoxChatInput.Text.Substring(4);
                                        string[] split = message.Split(' ');

                                        if (split.Length == 1)
                                        {
                                            if (irc.GetChannelUser(User.channel, split[0].ToString()).IsVoice)
                                            {
                                                irc.Devoice(User.channel, split[0].ToString());
                                            }

                                            if (irc.GetChannelUser(User.channel, split[0].ToString()).IsOwner)
                                            {
                                                irc.DeOwner(User.channel, split[0].ToString());
                                            }

                                            if (irc.GetChannelUser(User.channel, split[0].ToString()).IsHalfOp)
                                            {
                                                irc.DeHalfOp(User.channel, split[0].ToString());
                                            }

                                            if (irc.GetChannelUser(User.channel, split[0].ToString()).IsOp)
                                            {
                                                irc.Deop(User.channel, split[0].ToString());
                                            }

                                            irc.Protect(User.channel, split[0].ToString());
                                        }
                                    }

                                    if (textBoxChatInput.Text.StartsWith("/halfop "))
                                    {
                                        string message = textBoxChatInput.Text.Substring(4);
                                        string[] split = message.Split(' ');

                                        if (split.Length == 1)
                                        {
                                            if (irc.GetChannelUser(User.channel, split[0].ToString()).IsVoice)
                                            {
                                                irc.Devoice(User.channel, split[0].ToString());
                                            }

                                            if (irc.GetChannelUser(User.channel, split[0].ToString()).IsOwner)
                                            {
                                                irc.DeOwner(User.channel, split[0].ToString());
                                            }

                                            if (irc.GetChannelUser(User.channel, split[0].ToString()).IsOp)
                                            {
                                                irc.Deop(User.channel, split[0].ToString());
                                            }

                                            if (irc.GetChannelUser(User.channel, split[0].ToString()).IsProtected)
                                            {
                                                irc.DeProtect(User.channel, split[0].ToString());
                                            }

                                            irc.HalfOp(User.channel, split[0].ToString());
                                        }
                                    }

                                    if (textBoxChatInput.Text.StartsWith("/own "))
                                    {
                                        string message = textBoxChatInput.Text.Substring(4);
                                        string[] split = message.Split(' ');

                                        if (split.Length == 1)
                                        {
                                            if (irc.GetChannelUser(User.channel, split[0].ToString()).IsVoice)
                                            {
                                                irc.Devoice(User.channel, split[0].ToString());
                                            }

                                            if (irc.GetChannelUser(User.channel, split[0].ToString()).IsOp)
                                            {
                                                irc.Deop(User.channel, split[0].ToString());
                                            }

                                            if (irc.GetChannelUser(User.channel, split[0].ToString()).IsHalfOp)
                                            {
                                                irc.DeHalfOp(User.channel, split[0].ToString());
                                            }

                                            if (irc.GetChannelUser(User.channel, split[0].ToString()).IsProtected)
                                            {
                                                irc.DeProtect(User.channel, split[0].ToString());
                                            }

                                            irc.Owner(User.channel, split[0].ToString());
                                        }
                                    }

                                    if (textBoxChatInput.Text.StartsWith("/op "))
                                    {
                                        string message = textBoxChatInput.Text.Substring(4);
                                        string[] split = message.Split(' ');

                                        if (split.Length == 1)
                                        {
                                            if (irc.GetChannelUser(User.channel, split[0].ToString()).IsVoice)
                                            {
                                                irc.Devoice(User.channel, split[0].ToString());
                                            }

                                            if (irc.GetChannelUser(User.channel, split[0].ToString()).IsOwner)
                                            {
                                                irc.DeOwner(User.channel, split[0].ToString());
                                            }

                                            if (irc.GetChannelUser(User.channel, split[0].ToString()).IsHalfOp)
                                            {
                                                irc.DeHalfOp(User.channel, split[0].ToString());
                                            }

                                            if (irc.GetChannelUser(User.channel, split[0].ToString()).IsProtected)
                                            {
                                                irc.DeProtect(User.channel, split[0].ToString());
                                            }

                                            irc.Op(User.channel, split[0].ToString());
                                        }
                                    }

                                    if (textBoxChatInput.Text.StartsWith("/deown "))
                                    {
                                        string message = textBoxChatInput.Text.Substring(6);
                                        string[] split = message.Split(' ');

                                        if (split.Length == 1)
                                        {
                                            irc.DeOwner(User.channel, split[0].ToString());
                                        }
                                    }

                                    if (textBoxChatInput.Text.StartsWith("/dehalfop "))
                                    {
                                        string message = textBoxChatInput.Text.Substring(6);
                                        string[] split = message.Split(' ');

                                        if (split.Length == 1)
                                        {
                                            irc.DeHalfOp(User.channel, split[0].ToString());
                                        }
                                    }

                                    if (textBoxChatInput.Text.StartsWith("/unprotect "))
                                    {
                                        string message = textBoxChatInput.Text.Substring(6);
                                        string[] split = message.Split(' ');

                                        if (split.Length == 1)
                                        {
                                            irc.DeProtect(User.channel, split[0].ToString());
                                        }
                                    }

                                    if (textBoxChatInput.Text.StartsWith("/deop "))
                                    {
                                        string message = textBoxChatInput.Text.Substring(6);
                                        string[] split = message.Split(' ');

                                        if (split.Length == 1)
                                        {
                                            irc.Deop(User.channel, split[0].ToString());
                                        }
                                    }

                                    if (textBoxChatInput.Text.StartsWith("/ignore "))
                                    {
                                        string user = textBoxChatInput.Text.Substring(8);

                                        if (user != User.username)
                                        {
                                            alIgnoredHosts.Add(irc.GetChannelUser(User.channel, user).Host.ToString());

                                            using (StreamWriter sw = new StreamWriter("ignore", true))
                                            {
                                                sw.WriteLine(irc.GetChannelUser(User.channel, user).Host.ToString());
                                            }
                                        }
                                    }

                                    if (textBoxChatInput.Text.StartsWith("/voice "))
                                    {
                                        string message = textBoxChatInput.Text.Substring(7);
                                        string[] split = message.Split(' ');

                                        if (split.Length == 1)
                                        {
                                            irc.Voice(User.channel, split[0].ToString());

                                            if (irc.GetChannelUser(User.channel, split[0].ToString()).IsOp)
                                            {
                                                irc.Deop(User.channel, split[0].ToString());
                                            }

                                            if (irc.GetChannelUser(User.channel, split[0].ToString()).IsOwner)
                                            {
                                                irc.DeOwner(User.channel, split[0].ToString());
                                            }

                                            if (irc.GetChannelUser(User.channel, split[0].ToString()).IsProtected)
                                            {
                                                irc.DeProtect(User.channel, split[0].ToString());
                                            }

                                            if (irc.GetChannelUser(User.channel, split[0].ToString()).IsHalfOp)
                                            {
                                                irc.DeHalfOp(User.channel, split[0].ToString());
                                            }
                                        }
                                    }

                                    if (textBoxChatInput.Text.StartsWith("/devoice "))
                                    {
                                        string message = textBoxChatInput.Text.Substring(9);
                                        string[] split = message.Split(' ');

                                        if (split.Length == 1)
                                        {
                                            irc.Devoice(User.channel, split[0].ToString());
                                        }
                                    }

                                    if (textBoxChatInput.Text.StartsWith("/ban "))
                                    {
                                        string message = textBoxChatInput.Text.Substring(5);
                                        string[] split = message.Split(' ');

                                        if (split.Length == 1)
                                        {
                                            if (!irc.GetChannelUser(User.channel, split[0]).IsOwner &&
                                                !irc.GetChannelUser(User.channel, split[0]).IsOp &&
                                                !irc.GetChannelUser(User.channel, split[0]).IsHalfOp &&
                                                !irc.GetChannelUser(User.channel, split[0]).IsProtected)
                                            {
                                                irc.Ban(User.channel, irc.GetChannelUser(User.channel, split[0].ToString()).Host.ToString());
                                            }
                                            else
                                            {
                                                AppendText(User.channel, "tag", "[");
                                                AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                                                AppendText(User.channel, "tag", "] ");
                                                AppendText(User.channel, "notice", " -!- Ban failed. " + split[0] + " is a protected user\n");
                                            }
                                        }
                                    }

                                    if (textBoxChatInput.Text.StartsWith("/unban "))
                                    {
                                        string message = textBoxChatInput.Text.Substring(7);
                                        string[] split = message.Split(' ');

                                        if (split.Length == 1)
                                        {
                                            irc.Unban(User.channel, irc.GetChannelUser(User.channel, split[0].ToString()).Host.ToString());
                                        }
                                    }

                                    if (textBoxChatInput.Text.StartsWith("/kick "))
                                    {
                                        string message = textBoxChatInput.Text.Substring(6);
                                        string[] split = message.Split(' ');

                                        if (split.Length == 1)
                                        {
                                            if (!irc.GetChannelUser(User.channel, split[0]).IsOwner &&
                                                !irc.GetChannelUser(User.channel, split[0]).IsOp &&
                                                !irc.GetChannelUser(User.channel, split[0]).IsHalfOp &&
                                                !irc.GetChannelUser(User.channel, split[0]).IsProtected)
                                            {
                                                irc.Kick(User.channel, split[0]);
                                            }
                                            else
                                            {
                                                AppendText(User.channel, "tag", "[");
                                                AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                                                AppendText(User.channel, "tag", "] ");
                                                AppendText(User.channel, "notice", " -!- Kick failed. " + split[0] + " is a protected user\n");
                                            }
                                        }

                                        if (split.Length >= 2)
                                        {
                                            string reason = null;

                                            for (int j = 0; j < split.Length; j++)
                                            {
                                                reason += split[j].ToString() + " ";
                                            }

                                            reason = reason.TrimEnd(' ');

                                            if (!irc.GetChannelUser(User.channel, split[0]).IsOwner &&
                                                !irc.GetChannelUser(User.channel, split[0]).IsOp &&
                                                !irc.GetChannelUser(User.channel, split[0]).IsHalfOp &&
                                                !irc.GetChannelUser(User.channel, split[0]).IsProtected)
                                            {
                                                irc.Kick(User.channel, split[0].ToString(), reason.ToString());
                                            }
                                            else
                                            {
                                                AppendText(User.channel, "tag", "[");
                                                AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                                                AppendText(User.channel, "tag", "] ");
                                                AppendText(User.channel, "notice", " -!- Kick failed. " + split[0] + " is a protected user\n");
                                            }
                                        }
                                    }

                                    if (textBoxChatInput.Text.StartsWith("/nick "))
                                    {
                                        string message = textBoxChatInput.Text.Substring(6);
                                        string[] split = message.Split(' ');
                                        User.username = split[0];
                                        irc.Nick(split[0].ToString());
                                        this.Text = Application.Name + " [" + User.server + ":" + User.port.ToString() + ", " + split[0].ToString() + "] - " + irc.GetChannel(User.channel).Topic.ToString();
                                    }
                                }
                            }
                            else
                            {
                                AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", "[");
                                AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", DateTime.Now.ToShortTimeString().ToString());
                                AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", "] <");
                                AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", Nickname(irc.Nickname.ToString()));
                                AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", "> ");
                                AppendText(tabControlChatTabs.SelectedTab.Text, "text", textBoxChatInput.Text + "\n");

                                irc.Message(SendType.Message, tabControlChatTabs.SelectedTab.Text, textBoxChatInput.Text);
                                User.Log(tabControlChatTabs.SelectedTab.Text, "[" + DateTime.Now.ToShortTimeString().ToString() + "] <" + Nickname(irc.Nickname.ToString()) + "> " + textBoxChatInput.Text + "\n");
                            }
                        }
                        textBoxChatInput.Clear();
                    }
                }
                else
                {
                    if (e.KeyChar == (char)13)
                    {
                        if (textBoxChatInput.Text != "")
                        {
                            if (textBoxChatInput.Text.StartsWith("/"))
                            {
                                if (textBoxChatInput.Text.StartsWith("/server "))
                                {
                                    string message = textBoxChatInput.Text.Substring(8);
                                    User.server = message;

                                    Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser;
                                    key = key.CreateSubKey("SOFTWARE\\" + Application.Name);
                                    key.SetValue("server", User.server.ToString());

                                    AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                                    AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                                    AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                                    AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Server address has been set to " + message + "\n");
                                }

                                if (textBoxChatInput.Text.StartsWith("/port "))
                                {
                                    string message = textBoxChatInput.Text.Substring(6);

                                    try
                                    {
                                        User.port = Convert.ToInt32(message);
                                        Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser;
                                        key = key.CreateSubKey("SOFTWARE\\" + Application.Name);
                                        key.SetValue("port", User.port);

                                        AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Server port has been set to " + message + "\n");
                                    }
                                    catch (System.FormatException)
                                    {
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Server port must be a number\n");
                                    }
                                }

                                if (textBoxChatInput.Text.StartsWith("/channel "))
                                {
                                    string message = textBoxChatInput.Text.Substring(9);

                                    User.channel = message;
                                    Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser;
                                    key = key.CreateSubKey("SOFTWARE\\" + Application.Name);
                                    key.SetValue("channel", User.channel.ToString());

                                    tabControlChatTabs.TabPages[0].Text = User.channel;

                                    AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                                    AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                                    AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                                    AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Channel name has been set to " + message + "\n");
                                }

                                if (textBoxChatInput.Text.Equals("/config"))
                                {
                                    ShowConfigHelp();
                                }
                                
                                if (textBoxChatInput.Text.Equals("/admin"))
                                {
                                    ShowAdvancedCommandHelp();
                                }

                                if (textBoxChatInput.Text.Equals("/help"))
                                {
                                    ShowMainHelp();
                                }

                                if (textBoxChatInput.Text.Equals("/basic"))
                                {
                                    ShowBasicCommandHelp();
                                }

                                if (textBoxChatInput.Text.StartsWith("/autoconnect "))
                                {
                                    string message = textBoxChatInput.Text.Substring(13);

                                    if (message.Equals("on"))
                                    {
                                        menuItemAutoConnect.Checked = true;
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Auto Connect has been turned on\n");
                                    }

                                    if (message.Equals("off"))
                                    {
                                        menuItemAutoConnect.Checked = false;
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Auto Connect has been turned off\n");
                                    }
                                }
                               
                                if (textBoxChatInput.Text.StartsWith("/autoscroll "))
                                {
                                    string message = textBoxChatInput.Text.Substring(12);

                                    if (message.Equals("on"))
                                    {
                                        menuItemAutoScroll.Checked = true;
                                        timerAutoScroll.Enabled = true;
                                        ScrollChatWindow();
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Auto Scroll has been turned on\n");
                                    }

                                    if (message.Equals("off"))
                                    {
                                        menuItemAutoScroll.Checked = false;
                                        timerAutoScroll.Enabled = false;
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                                        AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Auto Scroll has been turned off\n");
                                    }
                                }

                                if (textBoxChatInput.Text.StartsWith("/nick "))
                                {
                                    string message = textBoxChatInput.Text.Substring(6);
                                    string[] split = message.Split(' ');

                                    AppendText(User.channel, "tag", "[");
                                    AppendText(User.channel, "time", DateTime.Now.ToShortTimeString().ToString());
                                    AppendText(User.channel, "tag", "] ");
                                    AppendText(User.channel, "notice", " -!- " + User.username.ToString() + " is now known as " + split[0].ToString() + "\n");

                                    User.username = split[0];
                                    this.Text = Application.Name + " [" + User.server + ":" + User.port.ToString() + ", " + User.username + "]";
                                }

                                if (textBoxChatInput.Text.StartsWith("/me "))
                                {
                                    AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                                    AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                                    AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                                    AppendText(tabControlChatTabs.SelectedTab.Text, "action", "* " + User.username.ToString() + " ");
                                    AppendText(tabControlChatTabs.SelectedTab.Text, "action", textBoxChatInput.Text.Substring(4));
                                    AppendText(tabControlChatTabs.SelectedTab.Text, "action", "\n");
                                }

                                if (textBoxChatInput.Text.Equals("/connect"))
                                {
                                    if (menuItemConnect.Enabled)
                                    {
                                        menuItemConnect_Click(sender, e);
                                    }
                                }

                                if (textBoxChatInput.Text.Equals("/disconnect"))
                                {
                                    if (menuItemDisconnect.Enabled)
                                    {
                                        menuItemDisconnect_Click(sender, e);
                                    }
                                }

                                if (textBoxChatInput.Text.Equals("/clear"))
                                {
                                    ClearText();
                                }

                                if (textBoxChatInput.Text.Equals("/quit") || textBoxChatInput.Text.Equals("/exit"))
                                {
                                    Exit();
                                }
                            }
                            else
                            {
                                AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", "[");
                                AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", DateTime.Now.ToShortTimeString().ToString());
                                AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", "] (offline) <");
                                AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", User.username.ToString());
                                AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", "> ");
                                AppendText(tabControlChatTabs.SelectedTab.Text, "yourself", textBoxChatInput.Text);
                                AppendText(tabControlChatTabs.SelectedTab.Text, "text", "\n");
                            }
                        }

                        textBoxChatInput.Clear();
                    }
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void IRCListenThread()
        {
            try
            {
                irc.Listen();
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void ChatForm_Resize(object sender, System.EventArgs e)
        {
            try
            {
                ScrollChatWindow();
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void ScrollChatWindow()
        {
            try
            {
                if (tabControlChatTabs.InvokeRequired)
                {
                    tabControlChatTabs.Invoke(new ScrollChatWindow_Delegate(ScrollChatWindow));
                }
                else
                {
                    if (menuItemAutoScroll.Checked)
                    {
                        for (int i = 0; i < tabControlChatTabs.SelectedTab.Controls.Count; i++)
                        {
                            if (tabControlChatTabs.SelectedTab.Controls[i].GetType().Name == "ExRichTextBox")
                            {
                                Khendys.Controls.ExRichTextBox chat = (Khendys.Controls.ExRichTextBox)tabControlChatTabs.SelectedTab.Controls[i];

                                SetScrollPos(chat.Handle, chat.Lines.Length, 0, true);
                                SendMessage(chat.Handle, EM_LINESCROLL, 0, 1);
                            }
                        }

                        textBoxChatInput.Focus();
                    }
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void SaveSettingsBeforeClosing()
        {
            try
            {
                Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser;
                key = key.CreateSubKey("SOFTWARE\\" + Application.Name);
                key.SetValue("server", User.server.ToString());
                key.SetValue("channel", User.channel.ToString());
                key.SetValue("fontsize", Convert.ToInt32(User.defaultFontSize));

                key.SetValue("port", User.port);
                key.SetValue("username", User.username);
                key.SetValue("quitmsg", mstrQuitMessage.ToString());
                key.SetValue("fontfamily", User.defaultFontFamily.ToString());

                if (User.defaultFontStyle == FontStyle.Bold)
                {
                    key.SetValue("fontstyle", "bold");
                }

                if (User.defaultFontStyle == FontStyle.Regular)
                {
                    key.SetValue("fontstyle", "regular");
                }

                if (menuItemAutoConnect.Checked)
                {
                    key.SetValue("autoconnect", 1);
                }
                else
                {
                    key.SetValue("autoconnect", 0);
                }

                if (menuItemAutoScroll.Checked)
                {
                    key.SetValue("autoscroll", 1);
                }
                else
                {
                    key.SetValue("autoscroll", 0);
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void ChatForm_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                Exit();
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        public void Exit()
        {
            try
            {
                if (this.InvokeRequired)
                {
                    this.Invoke(new Exit_Delegate(Exit));
                }
                else
                {
                    this.Opacity = 0;

                    irc.Quit(mstrQuitMessage);
                    System.Threading.Thread.Sleep(500);

                    irc.Disconnect();

                    SaveSettingsBeforeClosing();

                    System.Environment.Exit(0);
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void tabControlChatTabs_SelectedIndexChanged(object sender, System.EventArgs e)
        {
            try
            {
                for (int i = 0; i < tabControlChatTabs.TabPages.Count; i++)
                {
                    if (tabControlChatTabs.TabPages[i].Text.Equals(tabControlChatTabs.SelectedTab.Text))
                    {
                        tabControlChatTabs.TabPages[i].ForeColor = Color.Black;
                    }
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void listBoxUsers_DoubleClick(object sender, System.EventArgs e)
        {
            try
            {
                if (listBoxUserList.SelectedItem.ToString() != null)
                {
                    string user = listBoxUserList.SelectedItem.ToString().TrimStart(new char[] { '@', '+', '~', '&', '%' });
                    if (!alIgnoredHosts.Contains(irc.GetChannelUser(User.channel, user).Host))
                    {
                        if (!user.Equals(irc.Nickname.ToString()))
                        {
                            textBoxChatWindow = new Khendys.Controls.ExRichTextBox();

                            textBoxChatWindow.BackColor = User.background;
                            textBoxChatWindow.ForeColor = User.text;
                            textBoxChatWindow.ReadOnly = true;
                            textBoxChatWindow.Font = new Font(User.defaultFontFamily, User.defaultFontSize, User.defaultFontStyle, System.Drawing.GraphicsUnit.Point, ((System.Byte)(0)));

                            tab = new TabPage(user.ToString());

                            if (!alprivMsgs.Contains(user.ToString()))
                            {

                                textBoxChatWindow.Dock = System.Windows.Forms.DockStyle.Fill;
                                textBoxChatWindow.Visible = true;

                                tab.Controls.Add(textBoxChatWindow);

                                alprivMsgs.Add(user.ToString());

                                alPrivMsgWindows = new ArrayList();
                                alPrivMsgWindows.Add(textBoxChatWindow);
                                alPrivMsgWindowList.Add(alPrivMsgWindows);

                                addTabPageDelegate = new AddTabPage_Delegate(AddTabPage_DelegateFunction);
                                IAsyncResult r = BeginInvoke(addTabPageDelegate, new object[] { tab });
                                EndInvoke(r);

                                tabControlChatTabs.SelectedTab = tab;

                                string nickname = user;

                                AppendText(tab.Text, "tag", "[");
                                AppendText(tab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                                AppendText(tab.Text, "tag", "] ");
                                AppendText(tab.Text, "notice", " -!- " + nickname + " is " +
                                    irc.GetChannelUser(User.channel, nickname).Ident.ToString() + "@" +
                                    irc.GetChannelUser(User.channel, nickname).Host.ToString() + " (" +
                                    irc.GetChannelUser(User.channel, nickname).Realname.ToString() + ")\n");
                            }
                            else
                            {
                                for (int i = 0; i < tabControlChatTabs.TabPages.Count; i++)
                                {
                                    if (tabControlChatTabs.TabPages[i].Text.ToString().Equals(user))
                                    {
                                        tab = tabControlChatTabs.TabPages[i];
                                    }
                                }

                                for (int i = 0; i < tab.Controls.Count; i++)
                                {
                                    if (tab.Controls[i].GetType().Name == "ExRichTextBox")
                                    {
                                        tabControlChatTabs.SelectedTab = tab;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void menuItemExit_Click(object sender, System.EventArgs e)
        {
            try
            {
                Exit();
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void menuItemConnect_Click(object sender, System.EventArgs e)
        {
            try
            {
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Attempting connection to " + User.server + ":" + User.port.ToString() + "\n");

                if (File.Exists("ignore"))
                {
                    using (StreamReader sr = new StreamReader("ignore"))
                    {
                        string line = null;

                        while ((line = sr.ReadLine()) != null)
                        {
                            alIgnoredHosts.Add(line.ToString());
                        }
                    }
                }

                irc = new IrcClient();
                irc.SendDelay = 200;
                irc.AutoRetry = false;
                irc.ChannelSyncing = true;

                irc.OnTopic += new TopicEventHandler(irc_OnTopic);
                irc.OnDisconnected += new SimpleEventHandler(irc_OnDisconnected);
                irc.OnDeop += new DeopEventHandler(irc_OnDeop);
                irc.OnOp += new OpEventHandler(irc_OnOp);
                irc.OnOwner += new OwnerEventHandler(irc_OnOwner);
                irc.OnHalfOp += new HalfOpEventHandler(irc_OnHalfOp);
                irc.OnProtect += new ProtectEventHandler(irc_OnProtect);
                irc.OnDeOwner += new DeOwnerEventHandler(irc_OnDeOwner);
                irc.OnDeHalfOp += new DeHalfOpEventHandler(irc_OnDeHalfOp);
                irc.OnDeProtect += new DeProtectEventHandler(irc_OnDeProtect);
                irc.OnDevoice += new DevoiceEventHandler(irc_OnDevoice);
                irc.OnVoice += new VoiceEventHandler(irc_OnVoice);
                irc.OnWho += new WhoEventHandler(irc_OnWho);
                irc.OnModeChange += new MessageEventHandler(irc_OnModeChange);
                irc.OnUserModeChange += new MessageEventHandler(irc_OnUserModeChange);
                irc.OnUnban += new UnbanEventHandler(irc_OnUnban);
                irc.OnBan += new BanEventHandler(irc_OnBan);
                irc.OnKick += new KickEventHandler(irc_OnKick);
                irc.OnQueryAction += new ActionEventHandler(irc_OnQueryAction);
                irc.OnQuit += new QuitEventHandler(irc_OnQuit);
                irc.OnNickChange += new NickChangeEventHandler(irc_OnNickChange);
                irc.OnChannelAction += new ActionEventHandler(irc_OnChannelAction);
                irc.OnReadLine += new ReadLineEventHandler(irc_OnReadLine);
                irc.OnJoin += new JoinEventHandler(irc_OnJoin);
                irc.OnPart += new PartEventHandler(irc_OnPart);
                irc.OnQueryNotice += new MessageEventHandler(irc_OnQueryNotice);
                irc.OnChannelMessage += new MessageEventHandler(irc_OnChannelMessage);
                irc.OnQueryMessage += new MessageEventHandler(irc_OnQueryMessage);

                if (irc.Connect(User.server, User.port))
                {
                    irc.Login(User.username, Application.Name + " " + Application.Version);
                    irc.Join(User.channel);

                    if (irc.Connected)
                    {
                        menuItemConnect.Enabled = false;
                        menuItemDisconnect.Enabled = true;

                        threadIrcConnection = new Thread(new ThreadStart(IRCListenThread));
                        threadIrcConnection.Start();
                    }
                    else
                    {
                        AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                        AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                        AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                        AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Unable to connect to " + User.server + ":" + User.port.ToString() + "\n");
                    }
                }
                else
                {
                    AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                    AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                    AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                    AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- A connection to " + User.server + ":" + User.port.ToString() + " could not be established.\n");
                }
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void tabControlChatTabs_DrawItem(object sender, System.Windows.Forms.DrawItemEventArgs e)
        {
            try
            {
                Font fntTab = e.Font;
                Brush bshBack = new SolidBrush(SystemColors.Control);
                Brush bshFore = new SolidBrush(Color.Black);

                for (int i = 0; i < alPrivMsgAlert.Count; i++)
                {
                    if (e.Index == Convert.ToInt32(alPrivMsgAlert[i]))
                    {
                        fntTab = e.Font;
                        bshBack = new SolidBrush(Color.Black);
                        bshFore = new SolidBrush(Color.Yellow);
                    }

                    if (e.Index == 0 && tabControlChatTabs.SelectedIndex != 0
                        && alPrivMsgAlert.Contains(0))
                    {
                        fntTab = e.Font;
                        bshBack = new SolidBrush(Color.Black);
                        bshFore = new SolidBrush(Color.Yellow);
                    }

                    else if (e.Index == 0 && tabControlChatTabs.SelectedIndex == 0)
                    {
                        fntTab = e.Font;
                        bshBack = new SolidBrush(SystemColors.Control);
                        bshFore = new SolidBrush(Color.Black);
                        alPrivMsgAlert.Remove(0);
                    }

                    else if (e.Index == tabControlChatTabs.SelectedIndex && e.Index != 0)
                    {
                        fntTab = e.Font;
                        bshBack = new SolidBrush(SystemColors.Control);
                        bshFore = new SolidBrush(Color.Black);
                        alPrivMsgAlert.Remove(tabControlChatTabs.SelectedIndex);
                    }
                }

                string tabName = this.tabControlChatTabs.TabPages[e.Index].Text;
                StringFormat sftTab = new StringFormat();

                e.Graphics.FillRectangle(bshBack, e.Bounds);

                Rectangle recTab = e.Bounds;
                recTab = new Rectangle(recTab.X, recTab.Y + 4, recTab.Width, recTab.Height - 4);
                e.Graphics.DrawString(tabName, fntTab, bshFore, recTab, sftTab);
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void menuItemDisconnect_Click(object sender, System.EventArgs e)
        {
            try
            {
                irc.Disconnect();

                allowInput = false;
                menuItemConnect.Enabled = true;
                menuItemDisconnect.Enabled = false;

                AppendText(User.channel, "notice", "-------------------------------------" + "\n");
                AppendText(User.channel, "notice", " -!- You have been disconnected from the IRC server.");
                AppendText(User.channel, "text", "\n");

                this.Text = Application.Name + " [" + User.server + ":" + User.port.ToString() + ", " + User.username + "]";
                listBoxUserList.Items.Clear();
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void ClearText()
        {
            try
            {
                exRichTextBoxChatOutput.Clear();
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void AppendText(string target, string type, string text)
        {
            try
            {
                if (exRichTextBoxChatOutput.InvokeRequired)
                {
                    exRichTextBoxChatOutput.Invoke(new RichTextBoxUpdate_Delegate(AppendText), new object[] { target, type, text });
                }
                else
                {
                    textBoxChatInput.Font = new Font(User.defaultFontFamily, User.defaultFontSize, User.defaultFontStyle, System.Drawing.GraphicsUnit.Point, ((System.Byte)(0)));
                    tabControlChatTabs.Font = new Font(User.defaultFontFamily, User.defaultFontSize, User.defaultFontStyle, System.Drawing.GraphicsUnit.Point, ((System.Byte)(0)));
                    listBoxUserList.Font = new Font(User.defaultFontFamily, User.defaultFontSize, User.defaultFontStyle, System.Drawing.GraphicsUnit.Point, ((System.Byte)(0)));

                    if (target.Equals(User.channel))
                    {
                        switch (type)
                        {
                            case "tag":
                                exRichTextBoxChatOutput.SelectionColor = User.tag;
                                break;
                            case "time":
                                exRichTextBoxChatOutput.SelectionColor = User.time;
                                break;
                            case "action":
                                exRichTextBoxChatOutput.SelectionColor = User.action;
                                break;
                            case "yourself":
                                exRichTextBoxChatOutput.SelectionColor = User.yourself;
                                break;
                            case "text":
                                exRichTextBoxChatOutput.SelectionColor = User.text;
                                break;
                            case "notice":
                                exRichTextBoxChatOutput.SelectionColor = User.notice;
                                break;
                            case "person":
                                exRichTextBoxChatOutput.SelectionColor = User.person;
                                break;
                        }

                        exRichTextBoxChatOutput.Font = new Font(User.defaultFontFamily, User.defaultFontSize, User.defaultFontStyle, System.Drawing.GraphicsUnit.Point, ((System.Byte)(0)));

                        exRichTextBoxChatOutput.AppendText(text);
                    }
                    else
                    {
                        ArrayList chatWindows = new ArrayList();
                        Khendys.Controls.ExRichTextBox chat = new Khendys.Controls.ExRichTextBox();
                        chat.Font = new Font(User.defaultFontFamily, User.defaultFontSize, User.defaultFontStyle, System.Drawing.GraphicsUnit.Point, ((System.Byte)(0)));

                        if (alprivMsgs.Count > 0)
                        {
                            chatWindows = (ArrayList)alPrivMsgWindowList[alprivMsgs.IndexOf(target)];

                            switch (type)
                            {
                                case "tag":
                                    chat = (Khendys.Controls.ExRichTextBox)chatWindows[0];
                                    chat.SelectionColor = User.tag;
                                    chatWindows[0] = chat;
                                    break;

                                case "time":
                                    chat = (Khendys.Controls.ExRichTextBox)chatWindows[0];
                                    chat.SelectionColor = User.time;
                                    chatWindows[0] = chat;
                                    break;

                                case "action":
                                    chat = (Khendys.Controls.ExRichTextBox)chatWindows[0];
                                    chat.SelectionColor = User.action;
                                    chatWindows[0] = chat;
                                    break;

                                case "yourself":
                                    chat = (Khendys.Controls.ExRichTextBox)chatWindows[0];
                                    chat.SelectionColor = User.yourself;
                                    chatWindows[0] = chat;
                                    break;

                                case "text":
                                    chat = (Khendys.Controls.ExRichTextBox)chatWindows[0];
                                    chat.SelectionColor = User.text;
                                    chatWindows[0] = chat;
                                    break;

                                case "notice":
                                    chat = (Khendys.Controls.ExRichTextBox)chatWindows[0];
                                    chat.SelectionColor = User.notice;
                                    chatWindows[0] = chat;
                                    break;

                                case "person":
                                    chat = (Khendys.Controls.ExRichTextBox)chatWindows[0];
                                    chat.SelectionColor = User.person;
                                    chatWindows[0] = chat;
                                    break;
                            }

                            chat = (Khendys.Controls.ExRichTextBox)chatWindows[0];
                            chat.AppendText(text);
                            chatWindows[0] = chat;

                            alPrivMsgWindowList[alprivMsgs.IndexOf(target)] = chatWindows;
                        }
                    }
                }

                ScrollChatWindow();
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void ShowChatWindow()
        {
            try
            {
                textBoxChatInput.Focus();
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void menuItemAutoScroll_Click(object sender, System.EventArgs e)
        {
            if (menuItemAutoScroll.Checked)
            {
                menuItemAutoScroll.Checked = false;
                timerAutoScroll.Enabled = false;
            }
            else
            {
                menuItemAutoScroll.Checked = true;
                timerAutoScroll.Enabled = true;
                ScrollChatWindow();
            }
        }

        private void exRichTextBoxChatOutput_LinkClicked(object sender, System.Windows.Forms.LinkClickedEventArgs e)
        {
            try
            {
                mstrUrl = e.LinkText;

                Thread threadOpenWebsite = new Thread(new ThreadStart(OpenWebsite));
                threadOpenWebsite.Start();
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void OpenWebsite()
        {
            try
            {
                System.Diagnostics.Process.Start(mstrUrl);
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void menuItemAutoConnect_Click(object sender, System.EventArgs e)
        {
            if (menuItemAutoConnect.Checked)
            {
                menuItemAutoConnect.Checked = false;
            }
            else
            {
                menuItemAutoConnect.Checked = true;
            }
        }

        private void ShowConfigHelp()
        {
            try
            {
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "\n[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- **** " + Application.Name + " Help ****\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Configuration Command Reference\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- (where /command [optional parameters])\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Example: /autoscroll on\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!-\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /autoconnect [on,off] :: Auto Connect\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /autoscroll [on,off] :: Auto Scroll\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /channel [channel name] :: Sets the channel name\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /port [port number] :: Sets the server's port number\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /server [server address] :: Sets the server's address\n");
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void ShowMainHelp()
        {
            try
            {
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "\n[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- **** " + Application.Name + " Help ****\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Enter /admin for Advanced Chat Command Reference (Administration)\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Enter /basic for Basic Chat Command Reference\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Enter /config for Configuration Command Reference\n");
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void ShowAdvancedCommandHelp()
        {
            try
            {
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "\n[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- **** " + Application.Name + " Help ****\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Advanced Chat Command Reference (Administration)\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- (where /command [optional parameters])\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Example: /op gavin\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- \n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /ban [someone's nickname] :: Sets a ban on someone (use /unban to remove it)\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /dehalfop [someone's nickname] :: Takes away someone's half-operator status\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /deop [someone's nickname] :: Takes away someone's operator status\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /deown [someone's nickname] :: Takes away someone's owner status\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /devoice [someone's nickname] :: Takes away someone's voice status\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /halfop [someone's nickname] :: Gives someone half-operator status\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /ignore [someone's nickname] :: Ignores the person (any messages from them are ignored)\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /kick [someone's nickname] :: Kicks someone out of the chat room\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /op [someone's nickname] :: Gives someone operator status\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /own [someone's nickname] :: Gives someone owner status\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /protect [someone's nickname] :: Gives someone protected status\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /topic [new topic] :: Changes the chat room's topic\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /voice [someone's nickname] :: Gives someone voice status\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /unban [someone's nickname] :: Removes the ban on someone\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /unprotect [someone's nickname] :: Takes away someone's protected status\n");
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }

        private void ShowBasicCommandHelp()
        {
            try
            {
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "\n[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- **** " + Application.Name + " Help ****\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Basic Chat Command Reference\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- (where /command [optional parameters])\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- Example: /whois gavin\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!-\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /clear :: Clears the text of the active chat window\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /close :: Closes the active private chat window\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /connect :: Connects to the server and joins the chat room\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /disconnect :: Terminates your connection to the server\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /me [an action] :: Performs an action\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /msg [someone's nickname] :: Starts a private chat with someone\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /nick [your new nickname] :: Changes your nickname\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /quit [your quit message] :: Quits " + Application.Name + "\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /topic :: Shows the chat room's topic\n");

                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "[");
                AppendText(tabControlChatTabs.SelectedTab.Text, "time", DateTime.Now.ToShortTimeString().ToString());
                AppendText(tabControlChatTabs.SelectedTab.Text, "tag", "] ");
                AppendText(tabControlChatTabs.SelectedTab.Text, "notice", " -!- /whois [someone's nickname] :: Show's the person's hostname\n");
            }
            catch (Exception ex)
            {
                User.ErrorLog(ex.ToString());
            }
        }
    }
}
