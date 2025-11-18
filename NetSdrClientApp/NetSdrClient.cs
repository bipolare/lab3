using NetSdrClientApp.Messages;
using NetSdrClientApp.Networking;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
// 1. ЗАЛЕЖНІСТЬ ВИДАЛЕНО: using EchoServer; 
using static NetSdrClientApp.Messages.NetSdrMessageHelper;

namespace NetSdrClientApp
{
	/// <summary>
	/// Клієнт для взаємодії з NetSDR через TCP/UDP.
	/// </summary>
	public sealed class NetSdrClient : IDisposable
	{
		private readonly ITcpClient _tcpClient;
		private readonly IUdpClient _udpClient;
		private readonly object _lock = new();
		private TaskCompletionSource<byte[]>? _responseTaskSource;

		private const long DefaultSampleRate = 100_000;
		private const ushort AutomaticFilterMode = 0;
		private static readonly byte[] DefaultAdMode = { 0x00, 0x03 };
		private static readonly string SampleFileName = "samples.bin";
        
        // 2. ПОЛЕ _serverHarness ВИДАЛЕНО!
        // NetSdrClient (Application Layer) тепер не знає про EchoServer (Infrastructure/Test).


		/// <summary>
		/// Вказує, чи активний прийом IQ-даних.
		/// </summary>
		public bool IQStarted { get; private set; }

		public NetSdrClient(ITcpClient tcpClient, IUdpClient udpClient)
		{
			_tcpClient = tcpClient;
			_udpClient = udpClient;
			_tcpClient.MessageReceived += OnTcpMessageReceived;
			_udpClient.MessageReceived += OnUdpMessageReceived;
		}

		public async Task ConnectAsync()
		{
			if (_tcpClient.Connected)
				return;
			
			// _serverHarness.StartAsync(); // Видалено запуск тестового сервера з коду клієнта
			
			_tcpClient.Connect();

			if (!_tcpClient.Connected)
			{
				Console.WriteLine("Connection attempt failed.");
				return;
			}

			// 1. Set IQOutputDataSampleRate
			var sampleRateMsg = GetControlItemMessage(
				MsgTypes.SetControlItem, 
				ControlItemCodes.IQOutputDataSampleRate, 
				BitConverter.GetBytes(DefaultSampleRate).Take(5).ToArray()); // 5-byte long
			await SendTcpRequestAsync(sampleRateMsg).ConfigureAwait(false);

			// 2. Set RFFilter
			var rfFilterMsg = GetControlItemMessage(
				MsgTypes.SetControlItem, 
				ControlItemCodes.RFFilter, 
				BitConverter.GetBytes(AutomaticFilterMode).Take(2).ToArray()); // 2-byte ushort
			await SendTcpRequestAsync(rfFilterMsg).ConfigureAwait(false);

			// 3. Set ADModes
			var adModesMsg = GetControlItemMessage(
				MsgTypes.SetControlItem, 
				ControlItemCodes.ADModes, 
				DefaultAdMode);
			await SendTcpRequestAsync(adModesMsg).ConfigureAwait(false);
			
			Console.WriteLine("Client connected and initialized.");
		}

		public void Disconnect()
		{
			if (IQStarted)
			{
				StopIQAsync().Wait();
			}

			_tcpClient.Disconnect();
			// _serverHarness.Stop(); // Видалено зупинку тестового сервера
		}

		/// <summary>
		/// Зміна частоти приймача.
		/// </summary>
		public async Task ChangeFrequencyAsync(long frequency, byte channel)
		{
			if (!EnsureConnected()) return;

			// 1 байт (channel) + 5 байт (frequency)
			var parameters = new[] { channel }.Concat(BitConverter.GetBytes(frequency).Take(5)).ToArray();

			var frequencyMsg = GetControlItemMessage(
				MsgTypes.SetControlItem, 
				ControlItemCodes.ReceiverFrequency, 
				parameters);

			await SendTcpRequestAsync(frequencyMsg).ConfigureAwait(false);
			Console.WriteLine($"Frequency changed to {frequency} Hz on channel {channel}.");
		}

		/// <summary>
		/// Запуск передачі IQ-даних.
		/// </summary>
		public async Task StartIQAsync()
		{
			if (!EnsureConnected()) return;

			var receiverStateMsg = GetControlItemMessage(
				MsgTypes.SetControlItem, 
				ControlItemCodes.ReceiverState, 
				new byte[] { 0x01 }); // 0x01 = Start

			await SendTcpRequestAsync(receiverStateMsg).ConfigureAwait(false);

			_udpClient.StartListeningAsync();
			IQStarted = true;
			Console.WriteLine("IQ data transmission started.");
		}

		/// <summary>
		/// Зупинка передачі IQ-даних.
		/// </summary>
		public async Task StopIQAsync()
		{
			if (!EnsureConnected()) return;

			var receiverStateMsg = GetControlItemMessage(
				MsgTypes.SetControlItem, 
				ControlItemCodes.ReceiverState, 
				new byte[] { 0x00 }); // 0x00 = Stop

			await SendTcpRequestAsync(receiverStateMsg).ConfigureAwait(false);

			_udpClient.StopListening();
			IQStarted = false;
			Console.WriteLine("IQ data transmission stopped.");
		}

		private void OnUdpMessageReceived(object? sender, byte[] e)
		{
			var (type, itemCode, body) = TranslateMessage(e);

			if (type >= MsgTypes.DataItem0 && type <= MsgTypes.DataItem3)
			{
				ProcessIQData(body);
			}
		}

		private async void ProcessIQData(byte[]? body)
		{
			if (body == null) return;

			// The sample size is not explicitly defined in this code, 
			// assuming 2 bytes per sample (short) for I and Q data.
			// The original code uses 16, which suggests 16 bits = 2 bytes per sample.
			// The protocol spec states IQ data is transmitted as I/Q 32-bit (4-byte) samples.
			// Sticking to 16 based on the original code, but this might need clarification.
			var samples = GetSamples(16, body!); 

			await WriteSamplesAsync(samples).ConfigureAwait(false);
		}

		private static async Task WriteSamplesAsync(IEnumerable<int> samples)
		{
			await using var fs = new FileStream(
				SampleFileName, FileMode.Append, FileAccess.Write, FileShare.Read);

			await using var bw = new BinaryWriter(fs);
			foreach (var sample in samples)
			{
				bw.Write((short)sample);
			}
		}

		private async Task<byte[]?> SendTcpRequestAsync(byte[] msg)
		{
			if (!_tcpClient.Connected)
			{
				Console.WriteLine("No active connection.");
				return null;
			}

			var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);

			lock (_lock)
			{
				_responseTaskSource = tcs;
			}

			await _tcpClient.SendMessageAsync(msg).ConfigureAwait(false);

			var response = await tcs.Task.ConfigureAwait(false);
			return response;
		}

		private void OnTcpMessageReceived(object? sender, byte[] e)
		{
			TaskCompletionSource<byte[]>? tcs;
			lock (_lock)
			{
				tcs = _responseTaskSource;
				_responseTaskSource = null;
			}

			tcs?.SetResult(e);
		}

		private bool EnsureConnected()
		{
			if (_tcpClient.Connected)
				return true;

			Console.WriteLine("No active connection.");
			return false;
		}

		public void Dispose()
		{
			_tcpClient.MessageReceived -= OnTcpMessageReceived;
			_udpClient.MessageReceived -= OnUdpMessageReceived;
			_tcpClient.Dispose();
			_udpClient.Dispose();
		}
	}
}
