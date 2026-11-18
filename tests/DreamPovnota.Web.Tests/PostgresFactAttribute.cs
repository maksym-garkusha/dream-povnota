namespace DreamPovnota.Web.Tests;

/// <summary>
/// Тест зі справжньою PostgreSQL. Без змінної TEST_DATABASE_URL пропускається, тож
/// звичайний dotnet test базу не вимагає; у CI її дає сервісний контейнер postgres:18.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrEmpty(TestDatabase.ServerUrl))
            Skip = "Немає TEST_DATABASE_URL";
    }
}
