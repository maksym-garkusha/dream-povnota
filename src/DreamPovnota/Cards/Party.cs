using System.Text.Json.Serialization;

namespace DreamPovnota.Cards;

public enum PartyKind
{
    /// <summary>Схема UA-EDR і код з 8 цифр.</summary>
    Organisation,

    /// <summary>Схема UA-IPN або UA-EDR з 10 цифрами (це РНОКПП під чужою схемою).</summary>
    SoleProprietor,

    /// <summary>Схема UA-EDR, але код не з 8 і не з 10 цифр.</summary>
    OddCode,

    /// <summary>Іноземний або інший реєстр: FR-COA, CH_BS-CR тощо.</summary>
    OtherRegistry,
}

/// <summary>
/// Сторона проєкту. Для підприємця не зберігаємо ні код, ні назву: код це РНОКПП,
/// а назва це ім'я людини. Тому <see cref="Edrpou"/> і <see cref="Name"/> можуть бути null.
/// </summary>
public record Party(
    PartyKind Kind,
    string? Scheme,
    string? Edrpou,
    string? Name,
    IReadOnlyList<string> Roles)
{
    [JsonIgnore]
    public bool IsSoleProprietor => Kind == PartyKind.SoleProprietor;

    public bool HasRole(string role) => Roles.Contains(role);
}
