// Вміст Program.cs (EchoServer)

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Linq; 

// --- ТОЧКА ВХОДУ ДЛЯ КОНСОЛЬНОГО ЗАСТОСУНКУ ---
try
{
    Console.WriteLine("Starting EchoServer (TCP) and UdpTimedSender...");
    
    // Кваліфікуємо типи, щоб уникнути конфлікту імен 'EchoServer'
    using var server = new EchoServer.EchoServer(50000); 
    using var udpSender = new EchoServer.UdpTimedSender("127.0.0.1", 60000); 
    
    udpSender.StartSending(100);
    
    await server.StartAsync(); 
}
catch (Exception ex)
{
    Console.WriteLine($"Критична помилка: {ex.Message}");
}
finally
{
    Console.WriteLine("EchoServer stopped.");
}
// -------------------------------------------------------------------

namespace EchoServer
{
    // Повний патерн Dispose (для Sonar S3881)
    public class EchoServer : IDisposable
    {
        private readonly int _port;
        private TcpListener? _listener; 
        private readonly CancellationTokenSource _cancellationTokenSource; 
        private bool _disposed = false;

        public EchoServer(int port)
        {
            _port = port;
            _cancellationTokenSource = new CancellationTokenSource();
        }
        
        // ... (StartAsync та Stop) ...

        public async Task StartAsync()
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            Console.WriteLine($"Server started on port {_port}.");

            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync();
                    Console.WriteLine("Client connected.");

                    _ = Task.Run(() => HandleClientAsync(client.GetStream(), _cancellationTokenSource.Token));
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
            Console.WriteLine("Server shutdown.");
        }

        public void Stop()
        {
            _cancellationTokenSource.Cancel();
            _listener?.Stop(); 
        }

        // РЕФАКТОРИНГ: HandleClientAsync приймає Stream для тестування
        public async Task HandleClientAsync(Stream stream, CancellationToken token)
        {
            using (stream) 
            {
                byte[] buffer = new byte[1024];
                int bytesRead;

                try
                {
                    while (!token.IsCancellationRequested && (bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token)) != 0)
                    {
                        Console.WriteLine($"Received: {bytesRead} bytes.");
                        await stream.WriteAsync(buffer, 0, bytesRead, token); 
                        Console.WriteLine($"Sent: {bytesRead} bytes.");
                    }
                }
                catch (IOException ex) when (ex.InnerException is SocketException se && se.SocketErrorCode == SocketError.ConnectionReset)
                {
                    Console.WriteLine("Client disconnected forcefully.");
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error handling client: {ex.Message}");
                }
            }
            Console.WriteLine("Client disconnected.");
        }
        
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Stop(); 
                    _cancellationTokenSource.Dispose();
                }
                _disposed = true;
            }
        }
    }

    public class UdpTimedSender : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private readonly UdpClient _udpClient;
        private Timer? _timer; 
        private bool _disposed = false;
        
        // РЕФАКТОРИНГ: Зроблено статичним для потокобезпечного Random (для Sonar S2245)
        private static readonly Random Rnd = new Random(); 

        public UdpTimedSender(string host, int port)
        {
            _host = host;
            _port = port;
            _udpClient = new UdpClient();
        }
        
        // ... (StartSending) ...
        public void StartSending(int intervalMilliseconds)
        {
            if (_timer != null)
                throw new InvalidOperationException("Sender is already running.");

            _timer = new Timer(SendMessageCallback, null, 0, intervalMilliseconds);
        }

        ushort i = 0;

        private void SendMessageCallback(object? state)
        {
            try
            {
                byte[] samples = new byte[1024];
                
                // Використовуємо статичний Random з блокуванням
                lock (Rnd) 
                {
                    Rnd.NextBytes(samples);
                }
                i++;

                byte[] msg = (new byte[] { 0x04, 0x84 }).Concat(BitConverter.GetBytes(i)).Concat(samples).ToArray();
                var endpoint = new IPEndPoint(IPAddress.Parse(_host), _port);

                _udpClient.Send(msg, msg.Length, endpoint);
                Console.WriteLine($"Message sent to {_host}:{_port} ");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending message: {ex.Message}");
            }
        }

        // ... (StopSending) ...
        public void StopSending()
        {
            _timer?.Dispose();
            _timer = null;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    StopSending();
                    _udpClient.Dispose();
                }
                _disposed = true;
            }
        }
    }
}
