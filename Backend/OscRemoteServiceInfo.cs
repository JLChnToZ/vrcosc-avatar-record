using System.Net;

internal readonly struct OscRemoteServiceInfo {
    public readonly string id;
    public readonly string name;
    public readonly IPAddress address;
    public readonly int port;

    public OscRemoteServiceInfo(string id, string name, IPAddress address, int port) {
        this.id = id;
        this.name = name;
        this.address = address;
        this.port = port;
    }
}
