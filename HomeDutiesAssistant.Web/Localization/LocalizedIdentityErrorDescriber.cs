using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;

namespace HomeDutiesAssistant.Web.Localization;

public sealed class LocalizedIdentityErrorDescriber(IStringLocalizer localizer) : IdentityErrorDescriber
{
    public override IdentityError DefaultError()
        => Error(nameof(DefaultError), localizer["An unknown failure has occurred."]);

    public override IdentityError ConcurrencyFailure()
        => Error(nameof(ConcurrencyFailure), localizer["Optimistic concurrency failure, object has been modified."]);

    public override IdentityError InvalidToken()
        => Error(nameof(InvalidToken), localizer["Invalid token."]);

    public override IdentityError InvalidUserName(string? userName)
        => Error(nameof(InvalidUserName), localizer["User name '{0}' is invalid, can only contain letters or digits.", userName ?? ""]);

    public override IdentityError InvalidEmail(string? email)
        => Error(nameof(InvalidEmail), localizer["Email '{0}' is invalid.", email ?? ""]);

    public override IdentityError DuplicateUserName(string userName)
        => Error(nameof(DuplicateUserName), localizer["User name '{0}' is already taken.", userName]);

    public override IdentityError DuplicateEmail(string email)
        => Error(nameof(DuplicateEmail), localizer["Email '{0}' is already taken.", email]);

    public override IdentityError PasswordTooShort(int length)
        => Error(nameof(PasswordTooShort), localizer["Passwords must be at least {0} characters.", length]);

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars)
        => Error(nameof(PasswordRequiresUniqueChars), localizer["Passwords must use at least {0} different characters.", uniqueChars]);

    public override IdentityError PasswordRequiresNonAlphanumeric()
        => Error(nameof(PasswordRequiresNonAlphanumeric), localizer["Passwords must have at least one non alphanumeric character."]);

    public override IdentityError PasswordRequiresDigit()
        => Error(nameof(PasswordRequiresDigit), localizer["Passwords must have at least one digit ('0'-'9')."]);

    public override IdentityError PasswordRequiresLower()
        => Error(nameof(PasswordRequiresLower), localizer["Passwords must have at least one lowercase ('a'-'z')."]);

    public override IdentityError PasswordRequiresUpper()
        => Error(nameof(PasswordRequiresUpper), localizer["Passwords must have at least one uppercase ('A'-'Z')."]);

    private static IdentityError Error(string code, string description)
        => new() { Code = code, Description = description };
}