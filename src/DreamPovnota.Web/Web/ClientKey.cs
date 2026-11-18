using System.Net;
using System.Net.Sockets;

namespace DreamPovnota.Web.Web;

/// <summary>
/// Ключ клієнта для обмежень частоти. IPv6 зводимо до /56: домашньому абонентові
/// часто дають /56, тобто 256 мереж /64, і з ключем за /64 кожна з них була б новим клієнтом.
/// Ширше (/48) не беремо, бо за ним бувають цілі мережі провайдера з багатьма людьми.
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
        Array.Clear(bytes, 7, 9);
        return new IPAddress(bytes) + "/56";
    }
}
