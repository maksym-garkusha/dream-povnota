using DreamPovnota.Codes;

namespace DreamPovnota.Tests;

public class EdrpouTests
{
    [Theory]
    [InlineData("42750837")]   // код до 30000000..60000000, зсунуті ваги
    [InlineData("03346331")]   // Київгаз, код менший за 30000000
    [InlineData("00100227")]   // Укренерго
    [InlineData("20077720")]   // Нафтогаз
    [InlineData("04412567")]   // сторона з project-active.json
    public void Edrpou_accepts_real_codes(string code)
    {
        Assert.True(Edrpou.IsValid(code));
    }

    [Theory]
    [InlineData("42750838")]   // остання цифра змінена
    [InlineData("03346313")]   // дві цифри переставлені місцями
    [InlineData("3346331")]    // сім цифр
    [InlineData("0334633A")]
    public void Edrpou_rejects_broken_codes(string code)
    {
        Assert.False(Edrpou.IsValid(code));
    }

    [Theory]
    [InlineData("3346331", "03346331")]
    [InlineData(" 135390 ", "00135390")]
    [InlineData("42750837", "42750837")]
    public void Edrpou_normalize_restores_leading_zeros(string raw, string expected)
    {
        Assert.Equal(expected, Edrpou.Normalize(raw));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234567890")]   // 10 цифр це вже РНОКПП
    [InlineData("35546472.")]
    public void Edrpou_normalize_returns_null_for_other_things(string raw)
    {
        Assert.Null(Edrpou.Normalize(raw));
    }
}
