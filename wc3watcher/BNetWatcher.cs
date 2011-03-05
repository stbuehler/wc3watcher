using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.ComponentModel;
using System.Text.RegularExpressions;

using System.Windows.Forms;

using PcapDotNet.Core;
using PcapDotNet.Packets;
using PcapDotNet.Packets.IpV4;
using PcapDotNet.Packets.Transport;

namespace wc3watcher {
	class AsyncCommunicator {
		private AsyncOperation asyncOp;
		private PacketCommunicator comm;
		private Thread commthread;

		public AsyncCommunicator() {
			asyncOp = AsyncOperationManager.CreateOperation(null);
		}

		public event HandlePacket onPacketReceive;

		public PacketCommunicator Communicator {
			get { return comm; }
			set {
				if (null != comm) {
					Thread t = commthread;
					PacketCommunicator c = comm;
					commthread = null;
					comm = null;
					c.Dispose();
					if (!t.Join(50)) {
						t.Abort();
						t.Join();
					}
				}
				if (null != value) {
					comm = value;
					commthread = new Thread(listen);
					commthread.Start();
				}
			}
		}

		private void listen() {
			try {
				comm.ReceivePackets(-1, recv);
			} catch (System.InvalidOperationException) {
			} catch (System.Threading.ThreadAbortException) {
			}
		}

		private void recv(Packet packet) {
			asyncOp.Post(new SendOrPostCallback(delegate(object obj) {
				if (null != onPacketReceive) onPacketReceive(packet);
			}), null);
		}
	}

	struct BNCSPacket {
		public DateTime Timestamp;
		public byte ID;
		public IEnumerable<byte> Data;
	}

	class BNCSStream {
		private bool out_sync, in_sync;
		private byte[] out_buffer, in_buffer;

		public delegate void HandlePacket(BNCSPacket packet);

		public event HandlePacket OnOutPacket, OnInPacket;

		public BNCSStream() {
			out_sync = false;
			in_sync = false;
		}

		private byte[] join(ref byte[] a, byte[] b) {
			if (null == a) return b;
			byte[] d = new byte[a.Length + b.Length];
			a.CopyTo(d, 0);
			b.CopyTo(d, a.Length);
			a = null;
			return d;
		}

		public void Handle(Packet packet) {
			IpV4Datagram ip = packet.Ethernet.IpV4;
			TcpDatagram tcp = ip.Tcp;
			if (PortNumbers.BNCSPort == tcp.DestinationPort) {
				/* outgoing C -> S */
				ParseOut(packet.Timestamp, join(ref out_buffer, tcp.Payload.ToArray()));
			} else if (PortNumbers.BNCSPort == tcp.SourcePort) {
				/* incoming S -> C */
				ParseIn(packet.Timestamp, join(ref in_buffer, tcp.Payload.ToArray()));
			}
		}

		private void ParseOut(DateTime timestamp, byte[] data) {
			int pos = 0;

			if (0 == data.Length) return;

			if (!out_sync) {
				/* first byte in conversation to server */
				if (data[pos] == 0x01) {
					pos++;
					out_sync = true;
				}
			}

			while (pos < data.Length) {
				out_sync = (data[pos] == 0xff);
				if (!out_sync) return; /* throw away remaining part */

				/* need at least 4 bytes for header */
				if (4 > data.Length - pos) break;
				int messagelen = data[pos + 2] + 256 * data[pos + 3];
				if (messagelen > data.Length - pos) break;

				BNCSPacket packet;
				packet.ID = data[pos + 1];
				packet.Data = data.Skip(pos + 4).Take(messagelen - 4);
				packet.Timestamp = timestamp;
				if (null != OnOutPacket) OnOutPacket(packet);

				pos += messagelen;
			}

			out_buffer = pos < data.Length ? data.Skip(pos).ToArray() : null;
		}

		private void ParseIn(DateTime timestamp, byte[] data) {
			int pos = 0;

			while (pos < data.Length) {
				in_sync = (data[pos] == 0xff);
				if (!in_sync) return; /* throw away remaining part */

				/* need at least 4 bytes for header */
				if (4 > data.Length - pos) break;
				int messagelen = data[pos + 2] + 256 * data[pos + 3];
				if (messagelen > data.Length - pos) break;

				BNCSPacket packet;
				packet.ID = data[pos + 1];
				packet.Data = data.Skip(pos + 4).Take(messagelen - 4);
				packet.Timestamp = timestamp;
				if (null != OnInPacket) OnInPacket(packet);

				pos += messagelen;
			}

			in_buffer = pos < data.Length ? data.Skip(pos).ToArray() : null;
		}
	}

	class BNCSParser {
		private Regex[] join_regex;

		public BNCSParser(BNCSStream stream = null) {
			string[] join_patterns = Properties.Resources.join.Split(new char[2] {'\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			join_regex = new Regex[join_patterns.Length];
			for (int i = 0; i < join_patterns.Length; i++) {
				join_regex[i] = new Regex("^" + join_patterns[i] + "$");
			}
			
			if (null != stream) {
				stream.OnInPacket += HandleIn;
				stream.OnOutPacket += HandleOut;
			}
		}

		public delegate void JoinEvent(DateTime Timestamp, string User);
		public delegate void LeaveEvent(DateTime Timestamp, string User);
		public delegate void TalkEvent(DateTime Timestamp, string User, string Text);
		public delegate void WhisperEvent(DateTime Timestamp, string User, string Text);
		public delegate void EnterEvent(DateTime Timestamp, string Channel);
		public delegate void CommandEvent(DateTime Timestamp, string Text);
		public delegate void JoinGameEvent(DateTime Timestamp, string User, string GameName);
		public delegate void EmoteEvent(DateTime Timestamp, string User, string Text);

		public event JoinEvent OnJoin;
		public event LeaveEvent OnLeave;
		public event TalkEvent OnTalk;
		public event WhisperEvent OnWhisper;
		public event EnterEvent OnEnter;
		public event CommandEvent OnCommand;
		public event JoinGameEvent OnJoinGame;
		public event EmoteEvent OnEmote;

		public string readString(IEnumerable<byte> data, ref int offset) {
			byte[] sb = data.Skip(offset).TakeWhile(b => b != 0x00).ToArray();
			offset += sb.Length + 1;
			return Encoding.UTF8.GetString(sb);
		}
		public UInt32 readDWord(IEnumerable<byte> data, ref int offset) {
			return data.ToArray().ReadUInt(ref offset, Endianity.Small);
		}

		public void HandleOut(BNCSPacket packet) {
			int offset = 0;
			switch ((PacketIDConstants)packet.ID) {
			case PacketIDConstants.SID_CHATCOMMAND:
				string text = readString(packet.Data, ref offset);
				if (null != OnCommand) OnCommand(packet.Timestamp, text);
				break;
			}
		}

		public void HandleIn(BNCSPacket packet) {
			int offset = 0;
			switch ((PacketIDConstants)packet.ID) {
			case PacketIDConstants.SID_CHATEVENT:
				/* http://www.bnetdocs.org/?op=packet&pid=307 */
				UInt32 Event = readDWord(packet.Data, ref offset);
				offset = 0x18;
				string username = readString(packet.Data, ref offset);
				string text = readString(packet.Data, ref offset);
				switch (Event) {
				case 0x01:
				case 0x02:
					if (null != OnJoin) OnJoin(packet.Timestamp, username);
					break;
				case 0x03:
					if (null != OnLeave) OnLeave(packet.Timestamp, username);
					break;
				case 0x04:
					HandleWhisper(packet.Timestamp, username, text);
					break;
				case 0x05:
					if (null != OnTalk) OnTalk(packet.Timestamp, username, text);
					break;
				case 0x07:
					if (null != OnEnter) OnEnter(packet.Timestamp, text);
					break;
				case 0x17:
					if (null != OnEmote) OnEmote(packet.Timestamp, username, text);
					break;
				}
				break;
			}
		}

		private void HandleWhisper(DateTime Timestamp, string username, string text) {
			for (int i = 0; i < join_regex.Length; i++) {
				Match m = join_regex[i].Match(text);
				if (m.Success) {
					if (m.Groups[1].ToString() == username) {
						if (null != OnJoinGame) OnJoinGame(Timestamp, username, m.Groups[3].ToString());
					}
					break;
				}
			}
			if (null != OnWhisper) OnWhisper(Timestamp, username, text);
		}
	}

	class BNetWatcher {

		private AsyncCommunicator comm;
		private BNCSStream bncs_stream;
		private BNCSParser bncs_parser;

		public BNetWatcher() {
			bncs_stream = new BNCSStream();
			bncs_parser = new BNCSParser(bncs_stream);
			comm = new AsyncCommunicator();
			comm.onPacketReceive += bncs_stream.Handle;
			comm.onPacketReceive += handle;
		}

		public BNCSParser BNCSParser {
			get { return bncs_parser; }
		}

		public void open(IPacketDevice device) {
			if (null != device) {
				PacketCommunicator pc = device.Open(65536, PacketDeviceOpenAttributes.None, 1000);
				pc.SetFilter("ip and tcp and port " + PortNumbers.BNCSPort.ToString());
				comm.Communicator = pc;
			} else {
				comm.Communicator = null;
			}
		}

		public event HandlePacket onPacketReceive;

		private void handle(Packet packet) {
			if (null != onPacketReceive) onPacketReceive(packet);
		}
	}
}
