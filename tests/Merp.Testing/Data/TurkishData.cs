using System.Globalization;
using Bogus;

namespace Merp.Testing.Data;

/// <summary>
/// Realistic Turkish test data. Identity, tax and bank numbers carry valid check digits so they pass the
/// SharedKernel value objects.
/// </summary>
public static class TurkishData
{
    private static readonly Faker Faker = new("tr");

    public static Faker Bogus => Faker;

    /// <summary>Valid T.C. kimlik no.</summary>
    public static string Tckn()
    {
        var digits = new int[11];
        digits[0] = Faker.Random.Int(1, 9);
        for (var i = 1; i < 9; i++)
        {
            digits[i] = Faker.Random.Int(0, 9);
        }

        var odd = digits[0] + digits[2] + digits[4] + digits[6] + digits[8];
        var even = digits[1] + digits[3] + digits[5] + digits[7];
        digits[9] = (((odd * 7) - even) % 10 + 10) % 10;
        digits[10] = digits.Take(10).Sum() % 10;
        return string.Concat(digits);
    }

    /// <summary>Valid vergi kimlik no.</summary>
    public static string Vkn()
    {
        var digits = new int[10];
        var total = 0;
        for (var i = 0; i < 9; i++)
        {
            digits[i] = Faker.Random.Int(0, 9);
            var tmp = (digits[i] + 9 - i) % 10;
            total += tmp == 9 ? 9 : tmp * (1 << (9 - i)) % 9;
        }

        digits[9] = (10 - (total % 10)) % 10;
        return string.Concat(digits);
    }

    /// <summary>Valid Turkish IBAN (26 characters).</summary>
    public static string Iban()
    {
        var bban = Faker.Random.ReplaceNumbers("#####0################");
        // Check digits: 98 - (BBAN + "TR00" as numbers) mod 97; T = 29, R = 27
        var numeric = bban + "292700";
        var remainder = 0;
        foreach (var c in numeric)
        {
            remainder = ((remainder * 10) + (c - '0')) % 97;
        }

        return "TR" + (98 - remainder).ToString("00", CultureInfo.InvariantCulture) + bban;
    }

    /// <summary>Turkish mobile number in national format, e.g. <c>0532 123 45 67</c>.</summary>
    public static string MobilePhone() => Faker.Random.ReplaceNumbers("05## ### ## ##");

    public static string FirstName() => Faker.Name.FirstName();

    public static string LastName() => Faker.Name.LastName();

    public static string Email(string? firstName = null, string? lastName = null) =>
        Faker.Internet.Email(firstName, lastName, "example.com").ToLowerInvariant();

    public static string City() => Faker.PickRandom("İstanbul", "Ankara", "İzmir", "Bursa", "Antalya", "Konya", "Kocaeli", "Gaziantep");
}
