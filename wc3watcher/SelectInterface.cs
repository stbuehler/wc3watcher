using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using PcapDotNet.Core;

namespace wc3watcher {
	public partial class SelectInterface : UserControl {
		private IList<LivePacketDevice> allDevices;

		public SelectInterface() {
			InitializeComponent();

			allDevices = LivePacketDevice.AllLocalMachine;
			if (0 == allDevices.Count) {
				comboBox1.Items.Add("No Interfaces found");
				comboBox1.Enabled = false;
			} else {
				foreach (LivePacketDevice d in allDevices) {
					String name = d.Name;
					if (null != d.Description) name += " (" + d.Description + ")";
					comboBox1.Items.Add(name);
				}
			}
			comboBox1.SelectedIndex = 0;
		}

		public delegate void InterfaceSelectedEvent(IPacketDevice device);
		public event InterfaceSelectedEvent onInterfaceSelected;

		public IPacketDevice CurrentDevice {
			get {
				if (comboBox1.Enabled) {
					return allDevices[comboBox1.SelectedIndex];
				} else {
					return null;
				}
			}
		}

		private void comboBox1_SelectedIndexChanged(object sender, EventArgs e) {
			if (comboBox1.Enabled) {
				if (null != onInterfaceSelected)
					onInterfaceSelected(allDevices[comboBox1.SelectedIndex]);
			}
		}
	}
}
