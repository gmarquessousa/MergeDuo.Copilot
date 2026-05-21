using System.Text.RegularExpressions;
using MergeDuo.Copilot.Domain.Exceptions;

namespace MergeDuo.Copilot.Domain.Rules;

public static partial class UserIdRules
{
    public static bool IsValid(string? userId) =>
        !string.IsNullOrWhiteSpace(userId) && UserIdRegex().IsMatch(userId);

    [GeneratedRegex("^usr_[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex UserIdRegex();
}

public static class AggregateCategories
{
    public const string Income = "income";
    public const string CreditCard = "credit_card";
    public const string Loan = "loan";
    public const string FixedExpense = "fixed_expense";
    public const string VariableExpense = "variable_expense";
    public const string Investment = "investment";

    public static readonly ISet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Income,
        CreditCard,
        Loan,
        FixedExpense,
        VariableExpense,
        Investment
    };
}

public static class AggregateKinds
{
    public const string In = "in";
    public const string Out = "out";
    public const string Invest = "invest";

    public static readonly ISet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        In,
        Out,
        Invest
    };
}

public readonly record struct YearMonth(int Year, int Month)
{
    public int MonthIdx => Month - 1;
    public string Value => $"{Year:D4}-{Month:D2}";
    public DateOnly FirstDay => new(Year, Month, 1);
    public DateOnly LastDay => new(Year, Month, DateTime.DaysInMonth(Year, Month));

    public static YearMonth FromRoute(int year, int month)
    {
        if (year is < 2000 or > 2100)
        {
            throw new CopilotBadRequestException("invalid_year", "Invalid year.");
        }

        if (month is < 1 or > 12)
        {
            throw new CopilotBadRequestException("invalid_month", "Invalid month.");
        }

        return new YearMonth(year, month);
    }

    public static YearMonth Parse(string value)
    {
        if (!TryParse(value, out var yearMonth))
        {
            throw new CopilotBadRequestException("invalid_year_month", "Invalid yearMonth.");
        }

        return yearMonth;
    }

    public static bool TryParse(string? value, out YearMonth yearMonth)
    {
        yearMonth = default;
        if (string.IsNullOrWhiteSpace(value) || value.Length != 7 || value[4] != '-')
        {
            return false;
        }

        if (!int.TryParse(value[..4], out var year) || !int.TryParse(value[5..], out var month))
        {
            return false;
        }

        if (year is < 2000 or > 2100 || month is < 1 or > 12)
        {
            return false;
        }

        yearMonth = new YearMonth(year, month);
        return true;
    }

    public YearMonth AddMonths(int months)
    {
        var date = FirstDay.AddMonths(months);
        return new YearMonth(date.Year, date.Month);
    }

    public bool IsBeforeOrEqual(YearMonth other) =>
        Year < other.Year || (Year == other.Year && Month <= other.Month);

    public static YearMonth FromDate(DateOnly date) => new(date.Year, date.Month);

    public override string ToString() => Value;
}

public static class AggregateDocumentId
{
    public static string For(string userId, YearMonth yearMonth) => $"agg_{userId}_{yearMonth.Value}";
}
