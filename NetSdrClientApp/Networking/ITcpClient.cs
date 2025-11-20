using System; // Required for IDisposable
using System.Threading.Tasks;

namespace NetSdrClientApp.Networking
{
    // Inherit from IDisposable to enable the .Dispose() method and 'using' statements
    public interface ITcpClient : IDisposable
    {
        void Connect();
        void Disconnect();
        Task SendMessageAsync(byte[] data);

        event EventHandler<byte[]> MessageReceived;
        public bool Connected { get; }
    }
}
