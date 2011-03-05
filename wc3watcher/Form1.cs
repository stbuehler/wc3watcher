using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using PcapDotNet.Packets;

namespace wc3watcher {
	public partial class Form1 : Form {
		private BNetWatcher bnetWatcher;

		public Form1() {
			InitializeComponent();
			bnetWatcher = new BNetWatcher();
			bnetWatcher.open(selectedInterface1.CurrentDevice);
			selectedInterface1.onInterfaceSelected += bnetWatcher.open;

			bnetWatcher.BNCSParser.OnCommand += delegate(DateTime Timestamp, string Text) {
				listBox1.Items.Add(Timestamp.ToShortTimeString() + ": " + "You: " + Text);
			};
			bnetWatcher.BNCSParser.OnTalk += delegate(DateTime Timestamp, string User, string Text) {
				listBox1.Items.Add(Timestamp.ToShortTimeString() + ": " + User + ": " + Text);
			};
			bnetWatcher.BNCSParser.OnEmote += delegate(DateTime Timestamp, string User, string Text) {
				listBox1.Items.Add(Timestamp.ToShortTimeString() + ": " + User + " " + Text);
			};
			bnetWatcher.BNCSParser.OnWhisper += delegate(DateTime Timestamp, string User, string Text) {
				listBox1.Items.Add(Timestamp.ToShortTimeString() + ": " + User + " whispered to you: " + Text);
			};
			bnetWatcher.BNCSParser.OnJoinGame += delegate(DateTime Timestamp, string User, string GameName) {
				listBox1.Items.Add(Timestamp.ToShortTimeString() + ": " + User + " joined the game '" + GameName + "'");
				Clipboard.SetText(GameName);
			};
		}

		private void Form1_FormClosed(object sender, FormClosedEventArgs e) {
			bnetWatcher.open(null);
			bnetWatcher = null;
		}
	}
}
