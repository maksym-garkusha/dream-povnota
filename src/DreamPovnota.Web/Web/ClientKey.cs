using System.Net;
using System.Net.Sockets;

namespace DreamPovnota.Web.Web;

/// <summary>
/// Ключ клієнта для обмежень частоти. IPv6 зводимо до /64: одному абонентові
/// зазвичай дають цілу мережу /64, і без цього кожна адреса з неї була б новим клієнтом.
/// Ключ живе тільки в пам'яті обмежувача й ніде не записується.
/// </summary>
public static class ClientKey
{
    public static string Of(HttpContext context) => Of(context.Connection.RemoteIpAddress);

    public static string Of(IPAddress? address)
    {
        if (address is null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return address.ToString();

        byte[] bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }
}
