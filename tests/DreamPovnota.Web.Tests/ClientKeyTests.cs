using System.Net;
using DreamPovnota.Web.Web;

namespace DreamPovnota.Web.Tests;

public class ClientKeyTests
{
    [Fact]
    public void Addresses_from_one_ipv6_56_network_share_a_key()
    {
        Assert.Equal(
            ClientKey.Of(IPAddress.Parse("2001:db8:1:2:aaaa::1")),
            ClientKey.Of(IPAddress.Parse("2001:db8:1:2:bbbb::2")));
        // сусідні /64 одного абонента: 2001:db8:1:200::/56
        Assert.Equal(
            ClientKey.Of(IPAddress.Parse("2001:db8:1:201::1")),
            ClientKey.Of(IPAddress.Parse("2001:db8:1:2ff::1")));
        Assert.NotEqual(
            ClientKey.Of(IPAddress.Parse("2001:db8:1:200::1")),
            ClientKey.Of(IPAddress.Parse("2001:db8:1:300::1")));
        Assert.Equal("2001:db8:1:200::/56", ClientKey.Of(IPAddress.Parse("2001:db8:1:2ab:1::1")));
    }

    [Fact]
    public void Ipv4_mapped_into_ipv6_is_the_same_client_as_plain_ipv4()
    {
        Assert.Equal("203.0.113.7", ClientKey.Of(IPAddress.Parse("::ffff:203.0.113.7")));
    }
}
