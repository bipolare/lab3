using System; // Required for IDisposable
using System.Net; // Required for IPEndPoint
using System.Threading.Tasks;

namespace NetSdrClientApp.Networking
{
    // Inherit from IDisposable to enable the .Dispose() method 
    // for clean resource shutdown.
    public interface IUdpClient : IDisposable
    {
        event EventHandler<byte[]>? MessageReceived;

        // Core Communication Methods
        Task StartListeningAsync();
        void StopListening();
        
        // 🛑 ADDED: Methods for sending data
        Task SendMessageAsync(byte[] data, IPEndPoint remoteEndPoint);
        Task SendMessageAsync(string message, IPEndPoint remoteEndPoint);

        // Lifecycle/Cleanup Method
        void Exit();
    }
}
