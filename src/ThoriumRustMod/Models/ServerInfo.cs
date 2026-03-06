namespace ThoriumRustMod.Models;

/// <summary>
/// Server metadata sent once when the WebSocket connection is established
/// </summary>
public class ServerInfo
{
    /// <summary>
    /// The hostname of the server
    /// </summary>
    public string HostName { get; set; } = string.Empty;
    
    /// <summary>
    /// The URL/name of the current level/map
    /// </summary>
    public string MapHash { get; set; } = string.Empty;
    
    /// <summary>
    /// The IP address of the server
    /// </summary>
    public string IpAddress { get; set; } = string.Empty;
    
    /// <summary>
    /// The port number the server is running on
    /// </summary>
    public int Port { get; set; }
}

