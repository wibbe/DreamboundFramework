﻿using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;

namespace Dreambound.Telnet
{

	/**
	 * Represents a connected "Network Virtual Terminal".
	 */
	public class TelnetNVT
	{
		private TelnetServer m_server = null;
		private TcpClient m_client = null;
		private NetworkStream m_stream = null;

		private volatile Queue<string> m_inStream = new Queue<string>();
		private volatile Queue<string> m_outStream = new Queue<string>();
		private readonly object m_inLock = new object();
		private readonly object m_outLock = new object();

		private Thread m_inThread = null;
		private Thread m_outThread = null;

		private bool m_deadSocket = true;

		public int Id { get; private set; }

		public TelnetNVT(TelnetServer server, TcpClient client, int id)
		{
			m_server = server;
			Id = id;

			m_client = client;
			m_stream = m_client.GetStream();

			m_inThread = new Thread(InThreadMain);
			m_outThread = new Thread(OutThreadMain);
		}

		public void Write(string text)
		{
			lock (m_outLock)
			{
				m_outStream.Enqueue(text);
			}
		}

		public void WriteLine(string line)
		{
			lock (m_outLock)
			{
				m_outStream.Enqueue(line + "\r\n");
			}
		}

		public void Start()
		{
			m_deadSocket = false;
			m_inThread.Start();
			m_outThread.Start();
		}

		public void Stop()
		{
			m_inThread.Abort();
			m_outThread.Abort();
			m_deadSocket = true;

			m_stream.Close();
			m_client.Close();
		}

		public void Update()
		{
			if (m_client == null || !m_client.Connected)
				return;

			if (m_deadSocket)
			{
			}

			lock (m_inLock)
			{
				while (m_inStream.Count > 0)
					m_server.DataReceivedEvent(this, m_inStream.Dequeue());
			}
		}

		private void InThreadMain()
		{
			List<byte> inputStream = new List<byte>();

			while (m_client.Connected)
			{
				int input = m_stream.ReadByte();
				
				switch (input)
				{
					case -1:
						break;

					case (int)RFC854.IAC:
						int verb = m_stream.ReadByte();
						if (verb == -1)
							break;

						switch (verb)
						{
							case (int)RFC854.IAC:
								inputStream.Add((byte)verb);
								break;

							case (int)RFC854.DO:
							case (int)RFC854.DONT:
							case (int)RFC854.WILL:
							case (int)RFC854.WONT:
								int option = m_stream.ReadByte();
								if (option == -1)
									break;

								// Suppress everything
								m_stream.WriteByte((byte)RFC854.IAC);
								m_stream.WriteByte(verb == (int)RFC854.DO ? (byte)RFC854.WONT : (byte)RFC854.DONT);
								m_stream.WriteByte((byte)option);

								//Console.WriteLine("IAC, {0}, {1}", verb, option);
								break;
						}
						break;
		
					case (int)'\r':	// Ignore the line-feed character
						break;

					case (int)'\n':
						lock (m_inLock)
						{
							byte[] data = new byte[inputStream.Count];
							for (int i = 0; i < inputStream.Count; i++)
								data[i] = inputStream[i];

							inputStream.Clear();

							char[] encodedData = Encoding.UTF8.GetChars(data);
							m_inStream.Enqueue(new string(encodedData));
						}

						break;

					default:
						inputStream.Add((byte)input);
						break;
				}
			}
		}

		private void OutThreadMain()
		{
			const int SLEEP_TIME_INC = 20;
			const int SLEEP_TIME_MAX = 200;
			int sleepTime = SLEEP_TIME_MAX;

			while (m_client.Connected)
			{
				string data;

				lock (m_outLock)
				{
					if (m_outStream.Count > 0)
						data = m_outStream.Dequeue();
					else
						data = "";
				}

				if (data.Length > 0)
				{
					SendRaw(data);
				}
				else
				{
					Thread.Sleep(sleepTime);

					if (sleepTime < SLEEP_TIME_MAX)
						sleepTime += SLEEP_TIME_INC;
				}
			}
		}

		private void SendRaw(string data)
		{
			try
			{
				byte[] buffer = Encoding.UTF8.GetBytes(data);
				m_stream.Write(buffer, 0, buffer.Length);
			}
			catch (Exception exception)
			{
				if (exception is System.IO.IOException || exception is ObjectDisposedException)
					m_deadSocket = true;
				else
					throw;
			}
		}
	}
}

