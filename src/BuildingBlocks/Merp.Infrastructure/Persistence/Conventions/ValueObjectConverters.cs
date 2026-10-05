using Merp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Merp.Infrastructure.Persistence.Conventions;

// Single value objects of the shared kernel are stored as their normalized text. Values in the database were valid
// when written, so reading them back goes through the same factory and cannot fail.

public sealed class EmailConverter() : ValueConverter<Email, string>(v => v.Value, v => Email.Create(v).Value);

public sealed class PhoneNumberConverter() : ValueConverter<PhoneNumber, string>(v => v.Value, v => PhoneNumber.Create(v).Value);

public sealed class VknConverter() : ValueConverter<Vkn, string>(v => v.Value, v => Vkn.Create(v).Value);

public sealed class TcknConverter() : ValueConverter<Tckn, string>(v => v.Value, v => Tckn.Create(v).Value);

public sealed class IbanConverter() : ValueConverter<Iban, string>(v => v.Value, v => Iban.Create(v).Value);

public sealed class MersisNoConverter() : ValueConverter<MersisNo, string>(v => v.Value, v => MersisNo.Create(v).Value);

public sealed class SgkWorkplaceNoConverter() : ValueConverter<SgkWorkplaceNo, string>(v => v.Value, v => SgkWorkplaceNo.Create(v).Value);
